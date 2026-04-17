# Deployment & CI

## Ansible Playbook Phases

The main playbook `ansible/inference-setup.yml` has 6 phases, controlled by tags:

| Tag | Phase | Requires sudo | Description |
|-----|-------|---------------|-------------|
| `disk` | 1 | Yes | LVM extension for Ubuntu default install |
| `drivers` | 2 | Yes | NVIDIA drivers + reboot |
| `docker` | 3 | Yes | Docker + NVIDIA Container Toolkit |
| `lmstudio` | 4 | No | LM Studio user-space install |
| `services` | 5 | No | LiteLLM, Postgres, Node Exporter, DCGM Exporter, Alloy |
| `runner` | 6 | Yes | GitHub Actions runner service |

### Common Commands

```bash
# Full run
ansible-playbook -i ansible/inventory.ini ansible/inference-setup.yml \
  --ask-become-pass

# Services only (most common for config updates)
ansible-playbook -i ansible/inventory.ini ansible/inference-setup.yml \
  --ask-become-pass --tags services

# After reboot (skip disk/driver phases)
ansible-playbook ... --skip-tags disk,drivers
```

### Docker Group Gotcha

After the `docker` tag runs, log out and back in before running `services`. The new docker group membership isn't picked up in the current shell session.

## CI Workflow: update-services.yml

**Triggers:**
- Push to `main` when `configs/**`, `ansible/vars/main.yml`, or `ansible/inference-setup.yml` change
- Weekly schedule: Sundays at 3am UTC
- Manual via `workflow_dispatch`

**Runs on:** `[self-hosted, inference, gpu]` (the inference machine itself)

**What it does:**
1. Updates LM Studio and its inference runtime
2. Runs `ansible-playbook --tags services` with secrets from GitHub
3. Restarts LM Studio daemon

**Secrets passed as extra-vars:**
- `litellm_master_key`
- `anthropic_api_key`
- `openai_api_key`
- `litellm_postgres_password`

### Container Restart Behavior

The playbook registers config and env file copy results. If either file changed on disk, the LiteLLM container is restarted automatically via `restart: true` on the docker_container task.

If the config was already up-to-date on disk (e.g., deployed in a previous run but container wasn't restarted), you need a manual `docker restart litellm`.

## Switching to Cluster Config

When adding the Windows 3090 or rack nodes:

1. Fill in node IPs in `configs/litellm/litellm-config.cluster.yaml`
2. Update `litellm_config_src` in `ansible/vars/main.yml` to point to the cluster config
3. Push to main or run the workflow manually
