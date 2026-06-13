---
name: homelab-bootstrap
description: >-
  Reference guide for homelab-platform and homelab-services gotchas,
  known issues, and hard-won fixes. Use when troubleshooting Ansible
  (handler patterns, docker_container idempotency on bind-mount file
  changes, copy: vs template: for placeholder substitution,
  ANSIBLE_SSH_AGENT auto for 2.16+, ansible_hostname drift),
  Terraform (bpg/proxmox provider, kenske/technitium provider,
  dns-record CNAME vs A-record, output description interpolation,
  stale vm_ip after DHCP renewal), GitHub Actions (runner registration,
  offline / zombie-busy recovery, workflow_dispatch 422 from broken
  YAML, heredoc-in-block-scalar collisions), Infisical (OIDC trust
  bindings, env var pickup in Terraform), Docker (localhost in bridge
  containers, force-recreate after bind-mount edit, container UIDs for
  bind-mount permissions), DNS (.lan via systemd-resolved drop-ins,
  static-IP hosts need explicit A records), rsync deploy steps, or VM
  provisioning failures across the homelab fleet.
allowed-tools: Read
---

# Homelab Bootstrap — Known Issues & Fixes

Hard-won lessons from bootstrapping the homelab-platform terraform-runner
and provisioning VMs via Terraform + Ansible.

---

## Ansible Installation

**Never use `apt install ansible`** — it installs a version missing the Python
libraries needed by `community.docker` modules.

### Correct install (on the VM):
```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
source ~/.bashrc

uv tool install ansible-core --with ansible --with docker --with requests
export PATH="$HOME/.local/bin:$PATH"

ansible-galaxy collection install community.docker community.general
```

### Adding Python packages to an existing ansible-core uv install:
```bash
# DON'T: uv tool run --from ansible-core pip install <pkg>  (pip not available)
# DO:
uv tool install ansible-core --with <package> --force
```

---

## Ansible Stdout Callback

`ANSIBLE_STDOUT_CALLBACK: yaml` was removed in `community.general` v12.

**Fix:** Use the built-in default callback instead:
```yaml
env:
  ANSIBLE_STDOUT_CALLBACK: default
  ANSIBLE_CALLBACK_RESULT_FORMAT: yaml
```

---

## Docker Group Membership

After the Docker phase of `infra-runner.yml` adds the user to the `docker`
group, **the current SSH session does not pick up the new group**. Running
container tasks in the same session will fail with permission errors.

**Fix:** Log out and back in, then re-run with `--skip-tags docker`:
```bash
exit
ssh ubuntu@<VM_IP>
ansible-playbook ... --skip-tags docker
```

---

## bpg/proxmox Provider — Disk Resize During Clone

The `bpg/proxmox` Terraform provider fails when trying to resize a disk
during the clone operation. Two failure modes:

1. `"the server did not include a data object in the response"` — race condition
2. `"requested size (30G) is lower than current size (31.5G)"` — attempted shrink

**Root cause:** The Ubuntu 24.04 cloud-init template created with `qm resize 9000 scsi0 +28G`
results in a **31.5GB** disk (not 30GB — base image is ~3.5GB).

**Fix in `terraform/modules/proxmox-vm/main.tf`:**
- Remove the `disk` block from the VM resource entirely
- Add `lifecycle { ignore_changes = [disk] }`
- Use a `terraform_data` resource with `local-exec` curl to call the Proxmox
  resize API after VM creation
- `cloud-init growpart` handles partition expansion on first boot automatically

```hcl
lifecycle {
  ignore_changes = [disk]
}

resource "terraform_data" "disk_resize" {
  triggers_replace = { vm_id = ..., disk_gb = var.disk_gb }
  provisioner "local-exec" {
    command = <<-EOT
      curl -k -s -f -X PUT \
        -H "Authorization: PVEAPIToken=$PROXMOX_VE_API_TOKEN" \
        -d "disk=scsi0&size=${var.disk_gb}G" \
        "$PROXMOX_VE_ENDPOINT/api2/json/nodes/${var.target_node}/qemu/${proxmox_virtual_environment_vm.vm.vm_id}/resize"
    EOT
  }
  depends_on = [proxmox_virtual_environment_vm.vm]
}
```

---

## bpg/proxmox Provider — Cloud-Init Drive Storage

**Error:** `storage 'local' does not support content-type 'images'`

The `initialization.datastore_id` in the VM resource must be set to a storage
that supports images. `local` only has snippets enabled by default.

**Fix:** Use `local-lvm` for the initialization datastore:
```hcl
initialization {
  datastore_id = "local-lvm"   # NOT "local"
  ...
}
```

Note: The snippet file (user-data) is still uploaded to `local` — that's
correct and separate from the cloud-init drive datastore.

---

## GitHub Actions Branch Protection Bypass

User-level bypass actors in GitHub rulesets **do not work on personal repos**
— only on organization repos. Setting `bypass_actors` with your user ID will
appear to succeed via API but `current_user_can_bypass` will remain `"never"`.

**Workaround:** Remove the `pull_request` rule from the ruleset entirely for
personal repos where you're the sole contributor. Keep `deletion` and
`non_fast_forward` rules for safety.

---

## GitHub Actions Runner — Ansible on Windows

Ansible does not run natively on Windows (`os.get_blocking()` is Linux-only).
The terraform-runner bootstrap must be run **on the VM itself** (SSH in and
run locally), not from a Windows dev machine.

```bash
ansible-playbook -i "localhost," ansible/infra-runner.yml \
  -e "ansible_connection=local" \
  ...
```

---

## Infisical Secrets in Terraform — Read TF_VAR_* from Env Directly

When a Terraform provider supports env-var fallback (e.g. `TECHNITIUM_HOST`,
`TECHNITIUM_TOKEN` for `kenske/technitium`), wire it up with an **empty
provider block** and let the provider read process env directly. The
Infisical `secrets-action` already exports the env vars job-wide.

```hcl
# providers.tf
provider "technitium" {}   # reads TECHNITIUM_HOST / TECHNITIUM_TOKEN from env
```

**Don't** declare TF variables + pass them through `${{ env.X }}` in step
`env:` blocks:

```yaml
# BROKEN — silently loses the value
- name: Terraform Apply
  env:
    TF_VAR_technitium_host: ${{ env.TECHNITIUM_HOST }}   # ← evaluates wrong
  run: terraform apply ...
```

**Symptom:** Go provider errors like
`Get "***/api/...": unsupported protocol scheme ""` even though the secret
in Infisical is correct.

**Why:** `${{ env.X }}` in a step's `env:` block does not reliably pick up
vars set via `$GITHUB_ENV` by a prior Node.js action (which Infisical's
secrets-action is). GitHub still masks the value as `***` in logs, so it
looks set when it isn't.

**Diagnosing without exposing the value:** add a step that prints the
length and prefix check only — never the value:

```bash
echo "TECHNITIUM_HOST length: ${#TECHNITIUM_HOST}"
if [ "${TECHNITIUM_HOST#http://}" != "$TECHNITIUM_HOST" ]; then
  echo "starts with http://: YES"
fi
```

---

## Infisical OIDC — Trust-Bound to refs/heads/main Only

Homelab repo machine identities (homelab-services, homelab-platform) have
their OIDC trust template scoped to `sub: repo:OWNER/REPO:ref:refs/heads/main`.
Workflows dispatched on any other ref fail at the secrets-action step with:

```
##[error]Access denied: OIDC subject not allowed.
statusCode: 403, ForbiddenError
```

**Implication for debugging:** diagnostic commits that need to load Infisical
secrets must land on `main`. Use a small PR + squash-merge instead of
`gh workflow run --ref <debug-branch>`.

**To relax (if branch builds become a frequent need):** Infisical UI →
Machine Identities → `<identity>` → OIDC Auth → broaden the sub template
to `repo:OWNER/REPO:ref:refs/heads/*`. Weakens posture; only do this if
the friction outweighs the security trade-off.

When the 403 appears, don't waste time checking secret values or path
permissions — the binding is the issue.

---

## rsync -a Won't Create Missing Parent Dirs

`rsync -a` fails on the destination if the *parent* of the target path
doesn't exist. On a freshly provisioned VM, `~/services/` doesn't exist,
so `rsync -az ./svc/ ubuntu@vm:~/services/svc/` errors with:

```
rsync: [Receiver] mkdir "/home/ubuntu/services/svc" failed:
  No such file or directory (2)
rsync error: error in file IO (code 11)
```

**Fix:** create the destination directory tree over SSH **before** rsync,
not after:

```yaml
- name: Create service directories on VM
  run: |
    ssh -i "$HOME/.ssh/deploy_key" -o StrictHostKeyChecking=no \
      ubuntu@${{ steps.vm-ip.outputs.ip }} \
      "mkdir -p ~/services/svc/data"

- name: Sync service files to VM
  run: |
    rsync -az -e "ssh -i $HOME/.ssh/deploy_key -o StrictHostKeyChecking=no" \
      services/svc/ ubuntu@${{ steps.vm-ip.outputs.ip }}:~/services/svc/
```

Alternative: `rsync --mkpath` on rsync ≥ 3.2.3, but the explicit mkdir is
clearer and works everywhere.

This bit `deploy-dns.yml` (first prod-zone deploy) and the same ordering
bug exists in `deploy-observability.yml` — only hidden because that VM was
provisioned before deploy order mattered.

---

## Self-Hosted Runner Offline Recovery

When a workflow sits in `queued` for minutes against `[self-hosted, *]`,
the runner is almost certainly offline. Check from any machine with
`gh`:

```bash
gh api repos/OWNER/REPO/actions/runners \
  --jq '.runners[] | {name, status, busy, labels: [.labels[].name]}'
```

If `status` is `"offline"`:

1. Verify the runner VM is powered on in Proxmox.
2. SSH in. The runner is a systemd unit named
   `actions.runner.<owner>-<repo>.<runner-name>.service`. Find the exact
   name and restart:
   ```bash
   sudo systemctl list-units 'actions.runner.*' --all
   sudo systemctl status actions.runner.OWNER-REPO.RUNNER.service
   sudo systemctl start  actions.runner.OWNER-REPO.RUNNER.service
   ```
3. The queued job picks up automatically once the runner reports
   `status: online` — no re-dispatch needed.

The terraform-runner serves multiple repos via separate `actions-runner-*`
directories (one per repo registration); each registration is its own
systemd unit.

---

## kenske/technitium Provider — Known Gaps

The provider (`~> 0.2.2`) covers zones, records, and DHCP. It does **not**
model:

- **Forwarders / recursion settings** — manage via UI
- **Block lists** — manage via UI

Both change rarely so this is acceptable. If you ever need to manage them
in code, the escape hatch is a `null_resource` + `local-exec curl` against
the Technitium HTTP API endpoints (`/api/settings/set`,
`/api/settings/setBlockListUrls`). See
`homelab-platform/docs/decisions/dns-management.md` for full rationale.

**Bootstrap is one-time and manual** — Terraform cannot mint its own
Technitium API token. Create a non-admin `terraform` user in the UI, grant
View + Modify on Zones, generate a token via Sessions, then store
`TECHNITIUM_HOST` + `TECHNITIUM_TOKEN` in Infisical at `/technitium/`. Full
runbook: `homelab-services/services/dns/BOOTSTRAP.md`.

**Zone-already-exists error:** if a manually-created zone matches a name
Terraform wants to create, either (a) delete the zone in the UI and let
Terraform create it fresh, or (b) add an `import` block to adopt it. The
former is simpler unless you have records you want to preserve.

---

## DNS-on-VM-Create — Reusable Pattern

The `homelab-platform/terraform/modules/dns-record/` module wraps a single
`technitium_dns_zone_record` for composition next to `proxmox-vm`. The
reusable `provision-vm.yml` workflow accepts an opt-in input
`create_dns_record: true` that loads `/technitium/` from Infisical
alongside the standard paths.

Per-consumer setup when adding DNS to a new repo:

1. In the repo's Terraform root: declare `kenske/technitium` provider,
   add `provider "technitium" {}` (env-direct — see Infisical/Terraform
   section above), compose `module "dns" { source = ".../dns-record" }`.
2. In the provision workflow: `with: create_dns_record: true`.
3. In Infisical: grant the consumer's machine identity **read** on the
   `/technitium/` path (one-time).
4. The `lan` zone itself is singleton and owned by
   `homelab-services/services/dns/terraform-config/` — don't create zones
   in consumer repos.

Full copy-pasteable example:
`homelab-platform/docs/examples/new-vm-with-dns/`.

---

## Ansible 2.16+ — `ANSIBLE_SSH_AGENT=auto` Required

When passing `ansible_ssh_private_key_file` (typical for GitHub-Actions-driven
Ansible runs that load the key from Infisical), Ansible 2.16+ tries to add
the key to an SSH agent. If no agent is running, the task fails immediately:

```
[ERROR]: Task failed: Failed to authenticate: Failed to add configured
private key into ssh-agent: Cannot utilize private_key with SSH_AGENT disabled
```

**Fix:** Set `ANSIBLE_SSH_AGENT: "auto"` in the step's `env:` block so Ansible
spawns an agent on demand. Required for every remote-Ansible step.

```yaml
- name: Run playbook against the new VM
  env:
    ANSIBLE_HOST_KEY_CHECKING: "False"
    ANSIBLE_SSH_AGENT: "auto"
  run: ansible-playbook -i "${{ needs.provision.outputs.vm_ip }}," ...
```

---

## Don't Install a Runner on Every VM (or Every Physical Box)

Default pattern for ANY host whose workflows don't need anything host-local:
**drive it via Ansible-over-SSH from terraform-runner**, do NOT install a
GitHub Actions runner on it. Same logic applies whether the host is a
Proxmox VM or a physical machine.

Per-host runners cost you:
- A `GH_PAT` (the default `GITHUB_TOKEN` can't mint registration tokens)
- A separate `register-runner` job that installs the runner service
- PATH issues (`ansible-playbook: command not found` — `uv tool install`
  only adds the bin dir to PATH via `~/.bashrc`, which non-interactive
  runner shells don't source)
- A reinstall on every reprovision
- A chicken-and-egg in `provision-X.yml` when the workflow that provisions
  the host is itself `runs-on: self-hosted-<that-host>` — can't bootstrap
  a host whose runner doesn't exist yet

Use a per-host runner only when **everything** host-local matters: heavy
GPU access required by every step, large local-disk operations that
can't tolerate SSH bandwidth, etc. Even GPU + GGUF management is fine
over SSH (proven by the inference-host refactor — see
`/homelab-local-inference` "Topology" section).

**Decommissioning an orphan runner** registered on a VM/box: delete from
GitHub side via API (`gh api -X DELETE /repos/.../actions/runners/{id}`).
The local systemd unit + actions-runner directory linger and are harmless
clutter. Clean them up by SSH-ing in and `rm -rf ~/actions-runner` if the
runner ran as a regular user; sudo + systemd disable/remove for the unit
file. If the host is going to be reprovisioned soon, leave the local files
— Terraform destroy+rebuild wipes them (VMs only).

Reference workflows in `local-inference-infrastructure/.github/workflows/`:
- `provision-litellm-gateway.yml` — gateway VM (Terraform + Ansible-SSH)
- `provision-inference.yml` — physical inference box (Ansible-SSH)
- `sync-models.yml`, `deploy-alloy.yml` — recurring per-host ops over SSH

All five share the same shape: `runs-on: [self-hosted, self-hosted-infra]`,
load `/ansible` from Infisical, write deploy key, run `ansible-playbook -i
"$IP," ...`, clean up.

---

## DNS Resolution From terraform-runner — `dig @<dns-vm>` at Workflow Time

`terraform-runner` sits on `192.168.1.0/24`; the authoritative `.lan`
resolver (dns-vm) is on `192.168.0.250`. terraform-runner does NOT use
dns-vm as its system resolver, so `.lan` hostnames don't resolve through
the normal stack:

```
ssh: Could not resolve hostname ubuntu-tower.lan: Name or service not known
```

This bit the inference-host refactor on first try. Two options:

**Don't**: reconfigure terraform-runner's `/etc/systemd/resolved.conf` to
use dns-vm. Works but you're now silently dependent on dns-vm for every
DNS lookup the runner makes (apt, docker pulls, GitHub API, etc.).

**Do**: have the workflow resolve `.lan` names against dns-vm explicitly,
just before the ansible step:

```yaml
- name: Resolve <hostname> to IP via dns-vm
  id: target
  run: |
    IP=$(dig @192.168.0.250 +short "$HOSTNAME" A 2>/dev/null | head -n1)
    if [ -z "$IP" ]; then
      IP=$(nslookup "$HOSTNAME" 192.168.0.250 2>/dev/null | awk '/^Address: 192\./ {print $2; exit}')
    fi
    [ -n "$IP" ] || { echo "ERROR: $HOSTNAME did not resolve"; exit 1; }
    echo "ip=$IP" >> "$GITHUB_OUTPUT"

- name: Run ansible
  run: |
    ansible-playbook -i "${{ steps.target.outputs.ip }}," ...
```

DHCP renumbering becomes transparent — every run gets a fresh lookup.
`dig` is preferred; `nslookup` fallback covers the case where
`bind9-dnsutils` isn't installed on the runner.

The manifest / inventory keeps `.lan` hostnames as the source of truth.
Only the SSH target changes (resolved IP instead of FQDN).

---

## Ad-Hoc Ansible Inventory `-i "<host>,"` Requires `hosts: all`

Common pattern in CI: pass the target via `-i "<ip-or-host>,"` (the
trailing comma is what tells ansible "this is an inline inventory, not
a file"). The single host lands in the `all` and `ungrouped` groups —
NOT in any named group.

If the playbook header is:
```yaml
- name: My playbook
  hosts: inference_machines
```

…it'll silently no-op the entire run with:
```
[WARNING]: Could not match supplied host pattern, ignoring: inference_machines
PLAY RECAP *********
(no PLAY RECAP rows, 0 hosts matched)
```

**And the job will report "success"** because zero-hosts-matched isn't
a failure to ansible. Real bug, silent symptom.

**Fix:** use `hosts: all` in playbooks that are invoked via inline
inventory:

```yaml
- name: My playbook
  hosts: all   # works with -i "host," AND with inventory.ini group filters
```

Local-debug runs with the inventory file still work because `all`
implicitly includes every host in the inventory.

---

## `gather_facts: true` When You Use `ansible_env.*`

When a playbook references `{{ ansible_env.HOME }}` or any other fact
(`ansible_user_id`, `ansible_distribution`, etc.), facts must be gathered
first. If you disabled fact-gathering for speed:

```yaml
- hosts: all
  gather_facts: false   # ← culprit
  tasks:
    - stat:
        path: "{{ ansible_env.HOME }}/.lmstudio/bin/lms"   # FAILS
```

…every task that touches `ansible_env` fails with:
```
Finalization of task args failed: Error while resolving value for 'path':
'ansible_env' is undefined
```

**Fix:** either set `gather_facts: true` (the default — just delete the
override), or pass the value via extra-vars instead of a fact:

```yaml
gather_facts: true
# …or:
# -e remote_home=/home/blake
```

---

## Make Provisioning Playbooks Self-Skip on Already-Done Phases

For playbooks that should be safe to re-run on already-provisioned hosts
AND to provision fresh ones, gate each disruptive phase on a cheap
precheck rather than relying on each task's own idempotency. Two patterns
from `inference-setup.yml`:

**Skip disk-expand on physical boxes without cloud-image LVM:**

```yaml
- name: "[disk] Precheck: does /dev/ubuntu-vg/ubuntu-lv exist?"
  stat:
    path: /dev/ubuntu-vg/ubuntu-lv
  register: lv_stat
  tags: disk

- name: "[disk] Extend LV"
  command: lvextend -l +100%FREE /dev/ubuntu-vg/ubuntu-lv
  when: lv_stat.stat.exists
  tags: disk
# …same when: on resize2fs
```

A bare-metal install without LVM gets a "skipped" message instead of an
`lvextend` failure.

**Skip NVIDIA driver install + reboot when driver already works:**

```yaml
- name: "[drivers] Precheck: nvidia-smi works?"
  command: nvidia-smi
  register: nvidia_smi_precheck
  ignore_errors: true
  changed_when: false
  tags: drivers

- name: "[drivers] Install ubuntu-drivers-common"
  apt: { name: ubuntu-drivers-common, state: present, update_cache: true }
  become: true
  when: nvidia_smi_precheck.rc != 0
  tags: drivers
# …same when: on each driver-install and reboot task
```

A box that already has a working driver gets a fast no-op; a fresh box
installs, reboots, and the playbook intentionally fails with a "REBOOT
IN PROGRESS — re-run with skip_tags=disk,drivers" message so the workflow
operator knows to dispatch a resume.

The cost is ~3 added tasks per phase. The benefit is one playbook for
fresh-vs-existing and confidence to re-run after every commit.

---

## Best-Effort Boot-Time Side Effects

Systemd `ExecStartPost` and ansible tasks that do "warm-start"
optimizations (preloading a model, warming a cache) should be tolerant
of missing prerequisites — otherwise they block fresh-box provisioning
on something that's downloaded later by a different workflow.

**Systemd:** prefix the line with `-` to ignore exit codes:

```ini
[Service]
ExecStartPost=/path/lms server start ...
-ExecStartPost=/path/lms load qwen3.5-9b -c 32768 -y   ← `-` makes it best-effort
```

**Ansible:** `failed_when: false`, register the result, optionally emit a
debug note:

```yaml
- name: "[lmstudio] Preload reasoning model (best-effort)"
  shell: "{{ ansible_env.HOME }}/.lmstudio/bin/lms load qwen3.5-9b -c 32768 -y"
  register: lms_load_result
  changed_when: lms_load_result.rc == 0
  failed_when: false

- name: "[lmstudio] Note if preload was skipped"
  debug:
    msg: "{{ 'Preload OK' if lms_load_result.rc == 0 else 'Skipped (model not downloaded yet — sync-models will fetch).' }}"
```

The truthful version of "skipped" beats a red-X job for a step the
operator never intended to be a gate.

---

## One-Time Deploy-Key Bootstrap for New Ansible-SSH Hosts

When adding a new host to the Ansible-over-SSH cluster (gateway, inference,
whatever), the Ansible deploy public key needs to land in the target user's
`~/.ssh/authorized_keys` BEFORE any workflow can SSH in. Pattern from
`local-inference-infrastructure/scripts/bootstrap-deploy-key.sh`:

1. Pull `ANSIBLE_PRIVATE_KEY` from Infisical (`/ansible`, prod) via
   `infisical secrets get ANSIBLE_PRIVATE_KEY --plain` into `mktemp(1)`.
2. Derive the public key locally: `ssh-keygen -y -f /tmp/key > /tmp/key.pub`.
3. `ssh-copy-id -i /tmp/key.pub user@new-host.lan` (prompts for password
   once; idempotent — a re-run skips if the key already works).
4. `trap rm -f /tmp/key /tmp/key.pub EXIT` so nothing leaks even on Ctrl-C.

Reusable, idempotent, no secrets persisted to disk outside `/tmp`. Works
for any host whose `<user>` has password auth enabled at least once.

Don't try to drive this from a workflow — the workflow ITSELF needs the
key to be in place. It's a strictly local one-time op (a human runs the
script from a shell where they're logged into Infisical).

---

## `git add -A` in User-Shared Working Trees Sweeps WIP

When committing inside a repo the user actively works in (i.e. almost any
homelab repo), use `git add -- <specific-paths>` rather than `git add -A`.
The user often has uncommitted WIP in the working tree (in-progress
playbooks, vars, etc.); `git add -A` pulls those into a commit whose
message doesn't describe them, and the no-PR direct-push policy on these
repos means there's no review gate to catch it.

**Habit:**
```bash
git status --short                    # confirm what's modified
git add -- path/I/edited.yml ...      # explicit
git status --short                    # confirm staged set matches intent
git commit -m "..."
```

`git add -A` is fine in fresh worktrees or repos with no human
collaborator in parallel.

---

## Per-Repo Runner Registration

`terraform-runner` (label `self-hosted-infra`) needs to be **registered
separately for every repo** it serves. Registration is per-repo, not
per-org for personal accounts.

When you add a new repo whose workflows target `[self-hosted, self-hosted-infra]`,
dispatch `homelab-platform/.github/workflows/register-runner.yml` against
the new repo URL — otherwise jobs queue forever. The runner can be registered
to N repos simultaneously; each gets its own `actions-runner-<repo>`
directory and its own systemd unit.

---

## Infisical-action — Pin to v1.0.15 (last Node 20)

`Infisical/secrets-action@v1.0.16` was retagged April 2026 to switch from
Node 20 to Node 24. Older self-hosted runner binaries (2.322.0 and below)
don't support Node 24:

```
##[error]Can't use 'using: node24' as it's not supported in the runtime
```

**Fix:** Pin to `@v1.0.15` in every workflow on every repo until the
self-hosted runner binary is upgraded.

```yaml
- uses: Infisical/secrets-action@v1.0.15
```

**Long-term fix:** Upgrade actions-runner to a version that ships Node 24
(2.324+), then unpin.

---

## Multi-Project Infisical Pattern

Default homelab setup uses **two Infisical projects** per repo that needs
secrets:

- **Shared infra project** (`homelab-platform-eu2-m`): holds `/proxmox`,
  `/terraform`, `/ansible` — used by every repo that calls the reusable
  `provision-vm.yml` workflow. Referenced via `vars.INFISICAL_PROJECT_SLUG`.
- **Per-repo project** (e.g. `local-inference-infrastructure-pca-l`): holds
  app-scoped secrets like `/litellm`. Referenced via a repo-specific variable
  like `vars.LITELLM_INFISICAL_PROJECT_SLUG`.

The **same OIDC machine identity** (`vars.INFISICAL_IDENTITY_ID`) is bound
to both projects, so only the `project-slug` differs between secret-load
steps. Trust binding on the identity must include both projects — granted
in the Infisical UI under the identity's "Project Access" tab.

```yaml
# Shared infra path
- uses: Infisical/secrets-action@v1.0.15
  with:
    identity-id:  ${{ vars.INFISICAL_IDENTITY_ID }}
    project-slug: ${{ vars.INFISICAL_PROJECT_SLUG }}
    secret-path:  /ansible

# Per-repo path
- uses: Infisical/secrets-action@v1.0.15
  with:
    identity-id:  ${{ vars.INFISICAL_IDENTITY_ID }}
    project-slug: ${{ vars.LITELLM_INFISICAL_PROJECT_SLUG }}
    secret-path:  /litellm
```

---

## Verifying Loaded Secrets Without Leaking Them

When debugging "extra-var came through empty," **don't** echo the value.
Echo the length:

```bash
for v in LITELLM_MASTER_KEY LITELLM_POSTGRES_PASSWORD; do
  len=$(eval echo \${#$v})
  [ "$len" -eq 0 ] && echo "MISSING: $v" || echo "OK: $v ($len chars)"
done
```

Zero-length means the secret-path doesn't contain that key, or the key
exists in a different env / project. Non-zero confirms the action loaded
it without exposing the value in logs. Pair with the `length-only` debug
pattern in the Infisical/Terraform section above.

---

## `gh run rerun --failed` Uses the Original SHA's Workflow File

Counterintuitive: re-running a failed job does **not** pick up workflow
edits made after the original run started. The workflow YAML is pinned to
the head SHA of the original dispatch.

When testing a workflow fix, always do a **fresh** `gh workflow run` after
the commit; don't `--failed`-rerun.

---

## `infra-runner.yml` — `svc.sh` Guard When Re-Registering

The `runner` tag in `homelab-platform/ansible/infra-runner.yml` historically
ran `./svc.sh stop` and `./svc.sh uninstall` unconditionally. On a fresh
runner directory those scripts don't exist yet, and the task fails:

```
/bin/sh: 1: ./svc.sh: not found  (exit 127)
```

`failed_when` didn't catch "not found" — only "not installed". The fix
(now upstream in `homelab-platform`) is a `stat` check before stop/uninstall:

```yaml
- name: "[runner] Check whether svc.sh exists"
  stat:
    path: "{{ github_actions_runner_dir }}/svc.sh"
  register: runner_svc_script
  tags: runner

- name: "[runner] Stop existing service if present"
  shell: ./svc.sh stop
  args: { chdir: "{{ github_actions_runner_dir }}" }
  become: true
  when: runner_svc_script.stat.exists
  ...
```

Any repo that forks or copies `infra-runner.yml` needs the same guard.

---

## Bind-Mounted Config Files That Don't Exist Yet

Pattern in `litellm-gateway-setup.yml`: the LiteLLM container is started
with `command: --config /app/litellm/litellm-config.yaml`, where the config
file is bind-mounted from `~/litellm/`. But the config is generated and
shipped by a **separate** workflow (`deploy-litellm.yml`) on every push.

On a brand-new VM, the provision playbook leaves the container in a
crash-loop until `deploy-litellm` runs at least once. This is expected
behavior — the first `deploy-litellm` after provisioning fixes it.

When adding a similar pattern: document the two-step bring-up explicitly,
or have the provision workflow trigger the deploy workflow at the end
via `gh workflow run`.

---

## `gh --jq` Instead of External `jq` in Monitor/Background Bash

When monitoring GitHub Actions runs from Claude Code on Windows via the
`Monitor` tool, the background bash environment does **not** have `jq` on
PATH. Loops that pipe through `jq` silently produce empty output, and
events never fire.

**Fix:** Use `gh`'s built-in `--jq` flag instead of an external `jq`:

```bash
gh run view $ID --json status,conclusion,jobs \
  --jq '.jobs[] | "\(.name): \(.status)/\(.conclusion)"'
```

Avoid `... | jq` in any script you intend to run inside the `Monitor` tool
or as a background Bash on a Windows host.

---

## dns-record Module — Prefer CNAME for VM Aliases

When using the `dns-record` module from homelab-platform to create a
friendly alias for a VM (e.g. `dashboard.lan` for `dashboard-vm`), use
the **CNAME mode** pointing at the VM's auto-registered hostname, not
the A-record mode pinned at `module.vm.vm_ip`:

```hcl
module "dns" {
  source   = "github.com/BlakeHastings/homelab-platform//terraform/modules/dns-record?ref=main"
  hostname = "dashboard"                       # → dashboard.lan
  cname    = "${module.vm.vm_name}.lan"        # → dashboard-vm.lan
  zone     = "lan"
}
```

**Why:** The router auto-registers each VM's hostname into Technitium as
a TTL-900 A record on DHCP lease (visible as `<vm_name>.lan` in
`records lan`). When the VM's IP changes (DHCP renewal without a
reservation), the router pushes the new A record automatically. A CNAME
pointing at `<vm_name>.lan` follows for free; a Terraform-managed A
record pinned at `module.vm.vm_ip` does not, and goes stale until
`terraform apply` runs again.

**Symptom of the wrong choice:** `dashboard.lan` returns the wrong IP
after a DHCP renewal while `dashboard-vm.lan` resolves correctly. Fix is
to switch from `ip =` to `cname =` and re-provision.

**When the A-record mode IS correct:**
- DHCP reservation on the router pins the MAC → IP (record stays valid)
- Target is not a VM (Proxmox host, external service) — no router
  auto-registration to chain off (use `terraform-config/a_records` map)
- Static IP assignment

Full module docs: `homelab-platform/terraform/modules/dns-record/README.md`.

---

## GitHub Actions — Zombie "In-Progress" Runs

Distinct from the runner-`offline` failure mode above. Symptom:

- Runner status shows `online`, `busy: true`
- Specific run shows `in_progress` for many minutes
- Step-level `gh run view <id> --json jobs` shows every step has
  `status: completed, conclusion: success` — yet the run-level state
  remains `in_progress`
- New queued runs cannot start (runner is "busy" with the zombie)

This is a stale-state bug where the runner's "I finished" heartbeat
never reached GitHub (network blip, runner restart, OOM kill of the
runner process mid-cleanup, etc.). The actual work is done; only the
control-plane status is wrong.

**Recovery:**

```bash
# 1. Confirm steps actually completed
gh run view <ID> --json jobs --jq '.jobs[0].steps[] | {name, status, conclusion}'

# 2. Cancel the zombie (this often won't propagate to the runner,
#    but flips the API state so queued runs can proceed)
gh run cancel <ID>

# 3. SSH into the runner VM and restart the systemd unit (same as
#    the offline-recovery pattern above)
sudo systemctl restart actions.runner.OWNER-REPO.RUNNER.service
```

After step 3, the zombie flips to `failure`/`cancelled` and queued
runs start within seconds.

**Diagnostic shortcut:** if all steps show `completed/success` but the
run-level conclusion is empty/null and time-since-last-step exceeds
~2 minutes, it's a zombie. Don't wait it out — restart the runner.

---

## Terraform — Output Description Fields Don't Allow `${}` Interpolation

Subtle but recurring. Bites at plan time with a confusing message:

```
Error: Variables not allowed
  on main.tf line 35, in output "vm_ip":
   35:   description = "VM IP, also reachable as ${module.dns.fqdn}"
Variables may not be used here.
```

Output `description` (and `variable` `description`) fields must be
**literal strings** — Terraform evaluates them statically for `terraform
output -json` introspection, before any module wiring is resolved.

**Fix:** make the description a plain string. Reference the related
value as a separate output so consumers can introspect both:

```hcl
output "vm_ip" {
  description = "VM IP — also reachable by FQDN from the fqdn output below."
  value       = module.vm.vm_ip
}

output "fqdn" {
  description = "Friendly alias FQDN (a CNAME to the router-registered hostname)."
  value       = module.dns.fqdn
}
```

Same rule applies to:
- `variable "x" { description = "..." }` — no interpolation
- `output "x" { description = "..." }` — no interpolation
- Most resource attribute `description` fields (varies by provider)

Interpolation IS allowed in `value`, `default`, and most other fields.

---

## `localhost` Inside a Bridge-Networking Container is the Container, Not the Host

Surprising every time. A container in default Docker bridge networking
that does `curl http://localhost:3100` hits **the container itself**, not
the host's 3100. So:

- Prometheus container scraping `localhost:9100` — scrapes Prometheus, not
  the host's Node Exporter.
- Alloy container pushing to `http://localhost:3100/loki/api/v1/push` —
  pushes to Alloy itself, not the host's Loki.

The host's bound port (`-p 3100:3100`) is reachable from inside the
container at the host's LAN IP / hostname, **not** at `localhost`. Two
right ways:

1. **Hostname** — `<host>.lan:<port>` (resolves to host's LAN IP, reaches
   the published Docker port). Preferred — survives DHCP and the same
   config works from other hosts too.
2. **`host.docker.internal`** — Docker's special hostname for the host
   gateway. On Linux requires `extra_hosts: ["host.docker.internal:host-gateway"]`
   in compose. Less portable.
3. **Host networking** — `network_mode: host` puts the container ON the
   host's network namespace. Then `localhost` IS the host. Node Exporter
   uses this pattern (it must, to read host metrics). For most services
   it's overkill and removes the published-port isolation.

If a stack works fine when you `curl` from outside the VM but fails when
one container talks to another, suspect this. Especially common for
Prometheus self-scrapes and Alloy pushing to a local Loki.

---

## `docker compose up -d` Won't Recreate a Container After a Bind-Mount File Change

Compose's `up -d` is idempotent on its *own model* — image, env, ports,
volumes-as-paths. It does **not** look at the content of files inside
bind-mounted volumes. So if you:

1. Change a config file on the host that's bind-mounted into a container
2. Run `docker compose up -d`

…the container stays unchanged, still reading the old in-memory config.
Same gotcha if the container CrashLooped from a prior bad config — `up
-d` sees it as "already there, restart-policy will keep handling it" and
leaves it.

**Force recreation:**

```bash
docker compose up -d --force-recreate
```

…recreates every container in the compose file even when nothing in the
compose model changed. The cost is 5-30s of downtime per service. For
deploys triggered by config-file edits (e.g. our `deploy-observability`),
this is the right default — guarantees the new config is actually in
effect.

Alternative for surgical recreation: `docker compose up -d --force-recreate <service>`.

---

## Ansible `community.docker.docker_container` Idempotency Doesn't See Bind-Mount Files

Parallel of the docker-compose gotcha. `docker_container` checks the
container's image, env, ports, volume paths — but **not** the contents of
the files at those volume paths. So if a `template:` task updates
`~/alloy/config.alloy` (bind-mounted into the container), the subsequent
`docker_container` task with `state: started` sees no change and does
nothing.

**Fix: handler triggered from the template task.**

```yaml
- name: Render config
  template:
    src: foo.j2
    dest: ~/foo/config.yaml
  notify: restart foo

- name: Run container
  community.docker.docker_container:
    name: foo
    image: foo:latest
    volumes:
      - ~/foo/config.yaml:/etc/foo/config.yaml
    # ... (idempotent on its own parameters only)

handlers:
  - name: restart foo
    community.docker.docker_container:
      name: foo
      state: started
      restart: true   # stop + start; container re-reads the bind-mount on boot
```

Caveat: handlers only fire when the notifying task reports `changed`.
If the on-disk file already matches what the template would render (e.g.
manual edit, or previous run already applied the new value), no diff →
no notify → no restart. For pathological cases (state drift between disk
and in-memory), add a manual `docker restart <name>` step or run the
play with `--force-handlers`.

---

## systemd-resolved Drop-In for Routing One Domain to One DNS Server

Ubuntu 24.04 uses systemd-resolved by default. To route only `.lan`
queries to Technitium (192.168.0.250) while keeping the system's default
resolvers for everything else, write a drop-in:

```ini
# /etc/systemd/resolved.conf.d/technitium.conf
[Resolve]
DNS=192.168.0.250
Domains=~lan
```

Then `sudo systemctl restart systemd-resolved`. The `~lan` syntax marks
the domain as **routing-only** — systemd-resolved sends only `*.lan`
queries to that DNS server, and other queries continue to the system's
configured resolvers.

This pattern is the right answer for "give this device `.lan` resolution
without changing its primary DNS" — used today in
`homelab-services/.github/workflows/deploy-observability.yml` (for the
runner) and centralized in `homelab-platform/ansible/base-vm.yml` (for
every provisioned VM).

The dropin file persists across reboots and survives cloud-init
re-runs (cloud-init writes `/etc/netplan/...`, not resolved.conf.d).

---

## Container User UIDs for Bind-Mount Permissions

When a containerized service writes to a bind-mounted host directory, the
host directory must be owned by the UID the container runs as. The
process inside the container can't see host usernames — only numeric
UIDs map across the namespace boundary. If perms are wrong the container
CrashLoops on startup ("permission denied" writing to its data dir).

**Reference table for the services we run:**

| Image | UID | GID |
|-------|-----|-----|
| `grafana/grafana` | 472 | 472 |
| `prom/prometheus` | 65534 (nobody) | 65534 |
| `grafana/loki` | 10001 | 10001 |
| `grafana/tempo` | 10001 | 10001 |
| `technitium/dns-server` | 0 (root) | 0 |
| `nginx/nginx` | 0 (root, drops to 101 nginx) | 0 |
| `gethomepage/homepage` | 0 (root) | 0 |

**Idiomatic deploy fix** — in the workflow's `mkdir` step, also `chown`:

```bash
ssh ubuntu@$VM_IP <<EOSH
  mkdir -p ~/services/observability/data/{grafana,prometheus,loki,tempo}
  sudo chown -R 472:472     ~/services/observability/data/grafana
  sudo chown -R 65534:65534 ~/services/observability/data/prometheus
  sudo chown -R 10001:10001 ~/services/observability/data/loki
  sudo chown -R 10001:10001 ~/services/observability/data/tempo
EOSH
```

Idempotent — re-chown is a no-op once ownership is correct.

To find a new image's UID: `docker run --rm --entrypoint id <image>` —
output line is `uid=NNN(name) gid=NNN(name) ...`.

---

## GitHub Actions: `workflow_dispatch` 422 Means the YAML Won't Parse

Symptom:

```
could not create workflow dispatch event: HTTP 422:
Workflow does not have 'workflow_dispatch' trigger
(https://api.github.com/repos/OWNER/REPO/actions/workflows/<id>/dispatches)
```

Misleading. The workflow DOES have `on: workflow_dispatch:` written in
the file, but GitHub's YAML parser failed on something else in the file
and the entire on-block (including `workflow_dispatch`) wasn't extracted.

**Diagnose locally before pushing:**

```bash
uv run --with pyyaml python -c \
  "import yaml; yaml.safe_load(open('.github/workflows/<file>.yml')); print('ok')"
```

Common causes we've hit:

- **Shell heredoc inside a YAML block scalar** — closing marker (`EOSH`,
  `PROBE`, etc.) at column 0 breaks YAML's indentation rule that
  everything in a `|`-block must be indented at least as much as the
  first content line. See next section.
- **Trailing tabs / mixed indentation** in `run: |` blocks
- **Unquoted `:` in a value** (e.g. `name: foo: bar`)

The `gh workflow list` output is a good sanity check: if GitHub parsed
the file, the name column shows the workflow's declared `name:` (e.g.
"Deploy observability stack"). If parsing failed, the column shows the
file path instead.

---

## Shell Heredocs Inside YAML `run: |` Block Scalars

A heredoc's closing marker must be at column 0 in shell. But YAML's
block-scalar (`run: |` or `script: |`) requires all content lines to be
indented at least to the first line's indent. These two rules conflict:

```yaml
run: |
  ssh user@host bash -s <<'PROBE'
    echo hi
PROBE          # ← column 0 — breaks YAML's block-scalar boundary detection
```

YAML treats the un-indented `PROBE` as ending the scalar and starting new
structure. Then the next nested line confuses the parser further.

**Fixes (any one works):**

1. **Don't heredoc — quote the whole command instead.** Each SSH gets one
   single-quoted command string:
   ```yaml
   run: |
     SSH="ssh user@host"
     $SSH "first command"
     $SSH "second command"
   ```
2. **Indented heredoc terminator** (`<<-` strips leading tabs; can also
   keep the terminator indented):
   ```yaml
   run: |
     ssh user@host bash -s <<'PROBE'
       echo hi
     PROBE     # ← keep at same indent as content lines — works if heredoc is unquoted-marker-indented
   ```
   ⚠️ Subtle — `<<-PROBE` (with dash) only strips leading **tabs**, not
   spaces. YAML usually emits spaces. Reliability depends on editor.
3. **Move the script to a separate file** under `.github/scripts/` and
   call it: `run: bash .github/scripts/foo.sh`.

Option 1 is what we settled on in `deploy-observability.yml` after this
bit us — fewest moving parts, no whitespace traps.

---

## Terraform State Stale `vm_ip` After DHCP Renewal

`bpg/proxmox`'s `vm_ip` output is populated from `qemu-guest-agent`
during `terraform apply`. If the VM later gets a new DHCP lease, the
state file still holds the IP from the last apply. Downstream workflows
that read `vm_ip` from state (e.g. `deploy-*.yml`) will SSH to the old
IP and get either connection refused or — worse — `Permission denied`
from a totally different machine that picked up the IP since.

**Symptom mix:** the *current* VM is reachable by hostname (`<vm>.lan`,
router-auto-registered), but the deploy workflow keeps hitting a dead
or wrong address.

**Fix:** re-dispatch the provision workflow. Terraform refreshes state
via qga and updates `vm_ip` in-place. No `taint`, no recreate needed —
the apply will report `0 to add, 0 to change, 0 to destroy` and the
output is current.

**Better long-term:** use `.lan` hostnames in deploy workflows instead
of reading `vm_ip` from state. Most workflows in this fleet now do
(`ubuntu@<vm>.lan` not `ubuntu@$(jq ... vm_ip)`); the older
`deploy-dns.yml` and `deploy-observability.yml` still read state because
that pattern predates the router-DNS-registration era. Migrate when
convenient.

---

## `ansible_hostname` Can Drift From What You Expect

Templates that bake `{{ ansible_hostname }}` (the target VM's *current
OS-level short hostname*) into rendered configs are silently fragile.
The OS hostname can drift from the VM's "real" name without anyone
noticing:

- A playbook intended for `terraform-runner` gets accidentally pointed
  at another VM and sets the wrong hostname — observed today on
  `searxng-gateway` where `/etc/hostname` had become `terraform-runner`
  from a misdirected `infra-runner.yml` run weeks earlier.
- Cloud-init `preserve_hostname` quirks leave the template's name
  (e.g. `ubuntu-2404-template`) sticking around past first boot.
- Someone manually `hostnamectl set-hostname`'d the box and never
  reverted.

**Symptom**: telemetry labeled with the *wrong* name, but no error
anywhere. Logs ship, metrics scrape, everything looks healthy — except
the dashboard groups data under a name that doesn't match what you
expect. See [[feedback-telemetry-label-check]] for the debugging method.

### Fix pattern (3 layers)

1. **Pass the truth from the workflow.** The reusable provision-vm.yml
   takes `vm_name` as an input — forward it to Ansible as
   `-e "vm_name=${{ inputs.vm_name }}"`. That value is always correct
   regardless of OS state.

2. **Enforce in base-vm.yml.** Early task that sets system hostname
   from `vm_name` and re-gathers facts so any subsequent task using
   `ansible_hostname` sees the fixed value:

   ```yaml
   - name: "[hostname] Ensure system hostname matches vm_name"
     ansible.builtin.hostname:
       name: "{{ vm_name }}"
     become: true
     when: vm_name is defined and vm_name | length > 0
     register: hostname_set

   - name: "[hostname] Re-gather facts if hostname changed"
     setup:
     when: hostname_set is defined and hostname_set.changed
   ```

3. **Decouple templates from OS hostname.** Templates use `vm_name`
   directly with a fallback, never raw `ansible_hostname`:

   ```jinja
   node = "{{ vm_name | default(ansible_hostname) }}",
   ```

   The fallback covers callers that don't pass `vm_name` (none in this
   fleet today, but defensive).

Three layers because each catches a different failure: layer 1 fixes
incorrect inputs at provision time, layer 2 corrects past drift even
when inputs are right, layer 3 ensures the label is right even if both
prior layers fail.

Same anti-pattern applies to `inventory_hostname` (the name from the
Ansible inventory file — for `-i "IP,"` style calls this is the IP
literal, which is never what you want labeled). Always pass the
authoritative name explicitly.

---

## Static-IP Hosts Don't Get DHCP Auto-DNS-Registration

The router's "push DHCP client hostnames into Technitium" pipeline only
works for hosts that **actually take a DHCP lease**. A host with a
statically-configured IP at the OS level — `/etc/netplan/*.yaml` or
similar — never asks for a DHCP lease, never sends a hostname via
DHCP option 12, and never gets an A record in Technitium.

**Symptoms:**

- `<host>.lan` doesn't resolve, even though the host is up and
  reachable by IP.
- Prometheus target shows `down` with
  `dial tcp: lookup <host>.lan on 127.0.0.11:53: no such host`.
- `technitium-api leases` doesn't list the host at all (DHCP leases
  vs DNS records are separate stores).
- `technitium-api records lan` doesn't have an A record for the host
  name.

**Fix:** add an explicit static A record to
`homelab-services/services/dns/terraform-config/variables.tf`'s
`a_records` map. That's the centralized place for records that don't
have their own Terraform root creating them via the `dns-record`
module. Push → `configure-dns` auto-applies → Technitium gets the
record → resolution works fleet-wide within seconds.

```hcl
default = {
  "dns-vm"        = "192.168.0.250"   # static IP, DHCP reservation
  "pve"           = "192.168.0.68"    # Proxmox hypervisor (not a VM)
  "infisical-vm"  = "192.168.0.161"   # static OS-level IP
  # ...
}
```

Today the affected host was `infisical-vm` (static IP 192.168.0.161
hardcoded in deploy workflows from when it was the Infisical URL).
Easy to miss: the host is reachable, base-vm.yml succeeded, Alloy
ships logs (it uses `vm_name` for the label, not DNS), but
**Prometheus scraping fails silently** because the scrape config
uses `<host>.lan`.

Audit checklist before declaring observability done for a VM:

- [ ] Host in `technitium-api leases`? (DHCP-managed)
- [ ] `<host>.lan` in `technitium-api records lan`? (either DHCP-auto
      or static A from `terraform-config`)
- [ ] Prometheus scrape target `up`?
- [ ] Loki has `node=<host>` label?

If "Prometheus down + Loki up", suspect static-IP / missing A record.
If "Loki absent entirely", suspect Alloy config (template + label).

---

## Ansible: `copy:` Ships Files Verbatim — `template:` Substitutes

The Ansible `copy:` module is bit-exact: it sends the source file's
bytes to the target as-is, no variable interpolation, no Jinja
processing. The `template:` module processes Jinja first, then writes
the result.

**Surprisingly easy mistake:** a config file with placeholder strings
(`OBSERVABILITY_SERVER_IP`, `${SOME_VAR}`, etc.) and a `// REPLACE`
comment, copied with `copy:`, ships the literal placeholder to the
host. The application starts up trying to use it as a hostname or
URL, fails silently or with cryptic resolution errors, and nobody
notices because the symptom is "no telemetry" not "config rejected."

Today's instance: `configs/alloy/alloy-config.alloy` in
local-inference-infrastructure had `url = "http://OBSERVABILITY_SERVER_IP:3100/..."`
with a `// REPLACE` comment; the playbook used `copy:`. Alloy on both
inference hosts had been failing DNS resolution for the literal
string "OBSERVABILITY_SERVER_IP" since deploy.

**Rule:** if a file has any placeholders intended to be substituted,
rename it `.j2` and use `template:`. Pass the variables via
`vars_files`, `-e` extra-vars, or task-level `vars:`. Whoever wrote
the placeholder file was thinking "someone will substitute these" —
that someone needs to be Ansible, not a human at deploy time.

**Diagnostic trick:** grep the rendered file on the host for known
placeholder shapes:

```bash
grep -nE '[A-Z_]{6,}_(IP|HOST|URL|KEY)|REPLACE|TODO|XXX' /path/to/config
```

Anything that matches is a placeholder that escaped substitution.

---

## `ANSIBLE_SSH_AGENT: auto` Often Missing in Older Workflows

Already covered earlier in this skill, but reinforcement: it's
**fleet-wide audit-worthy**. The Ansible 2.16+ ssh-agent gotcha bit
the original `infra-runner.yml` deploy (fixed), then later bit
`provision-infisical.yml` and `local-inference-infrastructure/.github/workflows/deploy-alloy.yml`
that predated the fix.

**Whenever you touch a workflow that runs `ansible-playbook` with
`ansible_ssh_private_key_file=...`, check whether it sets
`ANSIBLE_SSH_AGENT: "auto"` in the step `env:`.** If not, add it.
The symptom — `Cannot utilize private_key with SSH_AGENT disabled`
in PLAY RECAP — is identical every time and only happens once Ansible
has been upgraded on the runner.

To audit the fleet:

```bash
grep -rL "ANSIBLE_SSH_AGENT" --include="*.yml" \
  ~/source/repos/*/.github/workflows/ \
  | xargs grep -l "ansible-playbook"
```

Anything that grep prints is a candidate that should add the env var.

---

## Bind-Mounted Secrets to a Container: Both File AND Directory Perms Matter

Pattern: load a secret from Infisical at deploy time, write it to a file
on the host, bind-mount that file (or its parent dir) into a container
that needs it (e.g. Prometheus reading a bearer-token file for scrape
auth, or Alloy reading a private key).

Two perm layers must both be permissive enough for the container's UID:

1. **The file itself.** `0440` owned by the container's UID is correct —
   only that UID can read.
2. **The parent directory.** Needs at least **execute** for the
   container's UID to traverse to the file. `0700` owned by `ubuntu`
   (1000) blocks even a 65534-owned file inside it from being read by
   UID 65534, because UID 65534 can't `cd` / traverse the directory.

**Symptom:** `open /etc/secrets/foo: permission denied` from inside the
container even when `ls -la` on the host shows the file is owned by the
container's UID with mode 0440.

**Fix:** dir mode `0711` (everyone can traverse, but only owner can
list). File mode `0440` owned by container UID stays.

```bash
# On the host (run from the SSH step in the deploy workflow):
mkdir -p $SECRETS_DIR
chmod 0711 $SECRETS_DIR                       # ← THE bit that's easy to get wrong
sudo tee $SECRETS_DIR/token > /dev/null <<< "$SECRET_FROM_INFISICAL"
sudo chmod 0440 $SECRETS_DIR/token
sudo chown 65534:65534 $SECRETS_DIR/token     # 65534 = Prometheus
```

**Don't put the secret in the SSH command's argv** — it shows up in
process listings and possibly workflow logs. Pipe via stdin → `sudo
tee` instead (as shown above).

In docker-compose, bind-mount read-only:

```yaml
volumes:
  - ./secrets:/etc/prometheus/secrets:ro
```

Reference `credentials_file: /etc/prometheus/secrets/token` in
prometheus.yml (or equivalent in other tools' scrape/auth configs).

The mistake hit us today on the LiteLLM bearer-token scrape — file was
correctly 0440 owned by 65534, but dir was 0700 owned by ubuntu →
`permission denied` despite the file being "for" Prometheus. Toggled
dir to 0711 → green.
