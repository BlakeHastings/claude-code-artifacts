---
name: homelab-bootstrap
description: >-
  Reference guide for homelab-platform gotchas, known issues, and hard-won
  fixes. Use when troubleshooting Ansible, Terraform, bpg/proxmox provider,
  GitHub Actions runner registration, or VM provisioning failures in the
  homelab-platform repo.
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
