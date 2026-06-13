# Deployment & CI

## Playbooks

| Playbook | Target | Purpose |
|----------|--------|---------|
| `ansible/inference-setup.yml` | per-inference-host | LM Studio install, UFW (gateway-only on :1234), Node + DCGM exporters, Alloy, per-host runner |
| `ansible/litellm-gateway-setup.yml` | litellm-gateway VM | Postgres + LiteLLM + Open WebUI containers, .env from secrets |
| `ansible/litellm-deploy-config.yml` | litellm-gateway VM | Ship `litellm-config.yaml` + `*_hook.py`, restart litellm, wait for `/health/liveliness` |
| `ansible/tts-gateway-setup.yml` | tts-gateway VM | Kokoro-FastAPI container, UFW (litellm-gateway only on :8880), Phase 4 builds `zoe` weighted blend via `torch.save` inside the container |
| `ansible/searxng-gateway-setup.yml` | searxng-gateway VM | SearXNG metasearch (Open WebUI RAG backend), UFW LAN-wide on :8888 |
| `ansible/playwright-gateway-setup.yml` | playwright-gateway VM | Chromium headless WebSocket server (Open WebUI URL fetcher), UFW (litellm-gateway only on :3000) |

All run from `terraform-runner` (label `self-hosted-infra`) via SSH using the Ansible deploy key from Infisical `/ansible`. None of the gateway VMs host their own runner.

## Inference-host playbook tags

`ansible/inference-setup.yml` is phased by tag:

| Tag | Phase | Requires sudo |
|-----|-------|---------------|
| `disk` | LVM extension | yes |
| `drivers` | NVIDIA drivers + reboot | yes |
| `docker` | Docker + NVIDIA Container Toolkit | yes |
| `lmstudio` | LM Studio user-space install | no |
| `services` | Exporters + Alloy | no |
| `runner` | GitHub Actions runner systemd unit | yes |

After the `docker` tag, log out and back in before `services` so the new
`docker` group membership is visible (see `/homelab-bootstrap` "Docker
Group Membership").

## CI Workflows

### `provision-litellm-gateway.yml`

**Trigger:** `workflow_dispatch` only.

**Jobs (both on `self-hosted-infra`):**
1. `provision` — calls `homelab-platform/.github/workflows/provision-vm.yml@main`
   with `vm_name: litellm-gateway`, `terraform_working_dir: terraform/nodes/litellm-gateway`.
   Outputs `vm_ip`.
2. `deploy-services` — loads `/ansible` + `/litellm` from Infisical (note:
   `/litellm` lives in the per-repo project, not the shared one), SSHes
   into the new VM, runs `ansible/litellm-gateway-setup.yml`.

After this completes, LiteLLM is up but in a crash-loop because
`litellm-config.yaml` doesn't exist yet. Run `deploy-litellm.yml` next.

### `deploy-litellm.yml`

**Triggers:**
- Push to `main` on `models/manifest.yaml`, `configs/litellm/settings.yaml`,
  or `scripts/*_hook.py`
- `workflow_dispatch`

**Runs on:** `self-hosted-infra` (terraform-runner).

**Steps:**
1. Checkout + `astral-sh/setup-uv@v5` + `uv sync --group dev`.
2. `uv run pytest tests/test_generate_litellm_config.py -v` — note
   `tests/test_lmstudio_context_hook.py` is excluded (OOM).
3. Render config: `uv run python scripts/generate_litellm_config.py
   --output ./litellm-config.yaml`.
4. Load `/ansible` SSH key from Infisical (shared project).
5. Run `ansible/litellm-deploy-config.yml` against `vars.LITELLM_GATEWAY_IP`
   with extra-vars pointing at the rendered config and `scripts/`.
6. Cleanup SSH key.

The deploy playbook copies the config + hooks into `~/litellm/` on the
gateway (bind-mounted to `/app/litellm` in the container), restarts the
container, and waits on `/health/liveliness`.

### `provision-inference.yml`

Manual, per-host. Provisions a new inference VM, installs LM Studio,
configures UFW to allow `192.168.0.5:1234` only.

### `sync-models.yml`

Runs on the per-inference-host runner (label
`self-hosted-inference-01` or `-02`). On manifest change, iterates models
declared for that host and runs `lms get`.

## Required Workflow Env

Set as repo variables (see SKILL.md):

- `INFISICAL_IDENTITY_ID`
- `INFISICAL_DOMAIN`
- `INFISICAL_PROJECT_SLUG`             — shared infra
- `LITELLM_INFISICAL_PROJECT_SLUG`     — per-repo, holds `/litellm`
- `LITELLM_GATEWAY_IP`                 — so deploys don't re-Terraform

`GH_PAT` is **not** required — no per-VM runner registration.

## Common Workflow YAML Snippets

### SSH-based Ansible step (every remote-Ansible step needs these envs)

```yaml
- name: Run playbook against the VM
  env:
    ANSIBLE_HOST_KEY_CHECKING: "False"
    ANSIBLE_SSH_AGENT: "auto"   # required on Ansible 2.16+
  run: |
    ansible-playbook \
      -i "${{ vars.LITELLM_GATEWAY_IP }}," \
      ansible/some-playbook.yml \
      -e "ansible_user=ubuntu" \
      -e "ansible_ssh_private_key_file=$HOME/.ssh/deploy_key" \
      -e "ansible_ssh_common_args='-o StrictHostKeyChecking=no'" \
      -e "..."
```

### Loading secrets from the two Infisical projects

```yaml
# Shared infra path (SSH key, terraform creds, proxmox)
- uses: Infisical/secrets-action@v1.0.15
  with:
    method: oidc
    identity-id:  ${{ vars.INFISICAL_IDENTITY_ID }}
    project-slug: ${{ vars.INFISICAL_PROJECT_SLUG }}
    env-slug:     prod
    secret-path:  /ansible
    domain:       ${{ vars.INFISICAL_DOMAIN }}
    export-type:  env

# Per-repo path (litellm secrets)
- uses: Infisical/secrets-action@v1.0.15
  with:
    method: oidc
    identity-id:  ${{ vars.INFISICAL_IDENTITY_ID }}
    project-slug: ${{ vars.LITELLM_INFISICAL_PROJECT_SLUG }}
    env-slug:     prod
    secret-path:  /litellm
    domain:       ${{ vars.INFISICAL_DOMAIN }}
    export-type:  env
```

Pin to `@v1.0.15` — see `/homelab-bootstrap` "Infisical-action — Pin to
v1.0.15" for why.

### Diagnosing empty secrets without exposing values

```yaml
- name: Verify secrets are loaded
  run: |
    missing=0
    for v in LITELLM_MASTER_KEY LITELLM_POSTGRES_PASSWORD; do
      len=$(eval echo \${#$v})
      [ "$len" -eq 0 ] && { echo "MISSING: $v"; missing=1; } \
                       || echo "OK: $v ($len chars)"
    done
    exit "$missing"
```

## Container Restart Behavior

`litellm-deploy-config.yml` ends with:

1. `community.docker.docker_container { name: litellm, restart: true }`
2. `uri: url=http://localhost:4000/health/liveliness` with `until: status==200`,
   `retries: 24`, `delay: 5` (so up to 120s).

If the container fails to come healthy, the playbook fails the job and
the SSH cleanup step still runs. Common cause when this fires: a
malformed `litellm-config.yaml` (e.g., a hook module name in
`callbacks:` that doesn't exist on disk).

## Switching to a Cluster Config

When adding a new inference host:

1. Add it to `inference_hosts:` in `models/manifest.yaml` with its
   `api_base`.
2. Add the host's name to each model's `hosts: [...]` you want mirrored
   onto it.
3. Provision the host via `provision-inference.yml`; run `sync-models.yml`
   so the host has the GGUFs.
4. Push to `main` — `deploy-litellm.yml` regenerates the config and
   restarts the gateway.
5. Update `docs/architecture/network-topology.md` with the new IP / VMID /
   runner label.
