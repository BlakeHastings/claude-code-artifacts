---
name: homelab-local-inference
description: >-
  Homelab local inference infrastructure reference. Use when working with
  the local-inference-infrastructure repo, the LiteLLM gateway VM, LM Studio
  on the inference machines, GPU node configuration, model selection,
  manifest-driven config generation, or Claude Code proxy setup through
  the homelab. Covers the specific homelab deployment, not general LiteLLM
  or LM Studio topics.
allowed-tools: Read, Bash, Grep, Glob
---

# Homelab Local Inference Infrastructure

Reference for the homelab's local inference setup. For general LiteLLM
knowledge, use `/litellm`. For cross-repo gotchas (Ansible, Terraform,
Infisical, runners, DNS), use `/homelab-bootstrap`.

Read `references/architecture.md` for network topology and component layout.
Read `references/deployment.md` for Ansible playbook usage and CI workflows.
Read `references/claude-code-setup.md` for how Claude Code routes through the proxy.
Read `references/tuning.md` for engine_args tuning methodology (n_cpu_moe
sweep, cached-prompt decode-isolation trick, when to re-sweep).

## Topology (current — Ansible-over-SSH era, May 2026)

LiteLLM was lifted off the inference machine into a dedicated VM in May 2026.
Shortly after, the inference machines themselves moved off the local-runner
pattern — they're now physical boxes managed Ansible-over-SSH from
terraform-runner, identical to how the gateway VM is managed. **No GitHub
Actions runners on any inference host.**

```
Agents (dev machines, compute-01, Zoe, opencode-litellm, …)
       │  :4000
       ▼
┌──────────────────────────────────┐
│   litellm-gateway VM             │  Proxmox VMID 300 · 192.168.0.5
│   - LiteLLM proxy :4000          │  2 CPU / 4 GB / 40 GB
│   - litellm-postgres :5432       │  No GH runner — managed remotely
│   - Open WebUI :3000             │   from terraform-runner over SSH
└────────┬───────────┬─────────────┘
         │           │
         ▼           ▼
┌──────────────────────────┐ ┌──────────────────────────┐
│ ubuntu-tower.lan         │ │ ubuntu-tower-02.lan      │  ← physical
│ (= inference-01)         │ │ (= inference-02)         │
│ i7-9700K · 8c/8t         │ │ i7-10700 · 8c/16t        │
│ 15 GiB RAM + 4 swap      │ │ 78 GiB RAM + 8 swap      │
│ RTX 3060 · 12 GB VRAM    │ │ RTX 2060 · 6 GB VRAM     │
│ 931 GB NVMe (WD Black)   │ │ 477 GB NVMe (WD SN530)   │
│ LM Studio :1234          │ │ LM Studio :1234          │
│ UFW: only .0.5           │ │ UFW: only .0.5           │
│ No GH runner             │ │ No GH runner             │
└──────────────────────────┘ └──────────────────────────┘
         ▲                          ▲
         └─ SSH (Ansible deploy key from Infisical /ansible) ─┐
                                                              │
                                  ┌─────────────────────────────┐
                                  │ terraform-runner            │
                                  │ 192.168.1.20                │
                                  │ Labels: self-hosted-infra   │
                                  │ Drives all gateway + infer- │
                                  │ ence work via Ansible/SSH   │
                                  └─────────────────────────────┘
```

Logical labels `inference-01` / `inference-02` are workflow inputs and
manifest keys; they resolve to the `.lan` hostnames inside each workflow
(see "DNS resolution at workflow time" below).

## Hardware profile (verified 2026-05-25)

The two boxes have **inverted bottlenecks** — be careful when matching
models to hosts:

|                    | inference-01 (ubuntu-tower)    | inference-02 (ubuntu-tower-02) |
|--------------------|--------------------------------|--------------------------------|
| CPU                | i7-9700K @ 3.60 GHz · 8c/8t    | i7-10700 @ 2.90 GHz · 8c/16t   |
| RAM                | **15 GiB** total + 4 GiB swap  | **78 GiB** total + 8 GiB swap  |
| GPU                | RTX 3060 · **12 GB VRAM**      | RTX 2060 · **6 GB VRAM**       |
| Disk               | 931 GB NVMe (WD Black SN770)   | 477 GB NVMe (WD SN530)         |
| Driver             | 595.71.05                       | 595.71.05                       |
| Effective ceiling  | GPU-rich, **RAM-starved** (27 GB total memory) | GPU-starved, **RAM-rich** (84 GB total memory) |

### Practical implications

- **inference-01** wins on raw GPU compute and VRAM, but only 15 GiB RAM
  caps total memory at ~27 GB. CPU-offloaded weights compete with KV
  cache for that 15 GiB. LM Studio's resource guardrail refuses loads
  past about ~17 GiB estimated total memory — that's why
  `lms load qwen3.6-35b-a3b -c 100000` says "will fail" and even 32K
  comes in at 15.43 GiB (right at the wall). Realistic ceiling for the
  35B model here is **32K-ish context** without further compromise.
- **inference-02** has only 6 GB VRAM but 78 GB RAM — *any* model whose
  weights fit in 6 GB serves comfortably with massive KV cache room.
  Qwen3.5-9B Q3_K_M at 170K estimates 15.19 GiB and passes the guardrail.
  Models bigger than ~6 GB will heavily CPU-offload to RAM (slow but
  possible thanks to the headroom).

### Use the right `lms load --estimate-only` before changing context

```
~/.lmstudio/bin/lms load <model-key> -c <ctx> --estimate-only -y
```

Returns "may be loaded" or "will fail to load" plus the estimated total
memory. This is the source of truth; the manifest's `context_length`
field is just what the LMStudioContextHook *tries* to load — see the
"LMStudioContextHook is broken" entry under Known Issues for why that
matters less than you'd expect.

## Key Facts

| Component | Host | Port | Notes |
|-----------|------|------|-------|
| LiteLLM proxy | litellm-gateway VM (192.168.0.5) | 4000 | Gateway; needs master key or virtual key |
| Postgres (LiteLLM DB) | litellm-gateway VM (docker: litellm-postgres) | 5432 | Docker network only |
| Open WebUI | litellm-gateway VM | 3000 | Points at LiteLLM over docker net |
| LM Studio (inference-01) | ubuntu-tower.lan | 1234 | UFW: only 192.168.0.5 allowed |
| **llama-server** (inference-01) | **ubuntu-tower.lan** | **8080** | **Docker container; MoE expert offload for local-moe** |
| LM Studio (inference-02) | ubuntu-tower-02.lan | 1234 | UFW: only 192.168.0.5 allowed (currently only Voxtral lives here) |
| **llama-server** (inference-02) | **ubuntu-tower-02.lan** | **8080** | **Docker container; serves local-reasoning at 170K context** |
| GitHub Actions runner | terraform-runner (192.168.1.20) | — | `self-hosted, self-hosted-infra`. The ONLY runner for this repo. |

## Multi-engine architecture (May 2026)

Each inference host runs **one or more inference engines**, each on its own
port. The manifest's `inference_hosts` schema makes this explicit:

```yaml
inference_hosts:
  inference-01:
    engines:
      lmstudio:     { api_base: http://ubuntu-tower.lan:1234/v1 }
      llama-server: { api_base: http://ubuntu-tower.lan:8080/v1 }   # MoE host
  inference-02:
    engines:
      lmstudio:     { api_base: http://ubuntu-tower-02.lan:1234/v1 }
      llama-server: { api_base: http://ubuntu-tower-02.lan:8080/v1 } # long-ctx host
```

Each model picks its engine + host(s):
```yaml
- name: local-moe
  engine: llama-server        # default lmstudio if omitted
  hosts: [inference-01]
  engine_args:                # engine-specific flags
    n_gpu_layers: 999
    n_cpu_moe: 16             # tuned: see references/tuning.md (20→40 t/s; 16→47 t/s)
    cache_type_k: q4_0
    cache_type_v: q4_0
    flash_attn: on
    port: 8080
```

See `references/tuning.md` for the n_cpu_moe sweep methodology — the curve
is NOT monotonic (12 was slower than 16) and the failure mode at low
values isn't a clean OOM, so the sweep matters.

**When to use which engine:**

| Engine | Use for | Strength | Limitation |
|---|---|---|---|
| **LM Studio** | Models that fit cleanly in VRAM, catalog-friendly names | Easy model management via `lms get`, auto-loading | No CLI access to MoE expert offload (UI toggle only — [bug #1190](https://github.com/lmstudio-ai/lmstudio-bug-tracker/issues/1190)); broken context-load hook |
| **llama-server** | MoE models needing expert offload, fine-grained KV/tensor control | Full llama.cpp CLI flags (`--n-cpu-moe`, `-ot`, `--cache-type-{k,v}`), runs as Docker container with `restart: unless-stopped` | One model per container; you template flags per model in the manifest |

**llama-server runs as a Docker container** templated from manifest:
- Image: `ghcr.io/ggml-org/llama.cpp:server-cuda`
- Container name: `llama-server-<model-name>` (e.g. `llama-server-local-moe`)
- Models bind-mounted from `~/llama-server/models/<hf_repo>/<hf_file>` (read-only)
- GPU access via nvidia-container-toolkit (installed in Phase 3)
- `--metrics` flag is always set → Prometheus endpoint at `/metrics` (key series:
  `llamacpp:predicted_tokens_seconds`, `llamacpp:prompt_tokens_seconds`,
  `llamacpp:kv_cache_usage_ratio`, `llamacpp:requests_processing`)
- UFW: only gateway IP (192.168.0.5) AND observability-vm IP (192.168.0.23) —
  the latter is required for Prometheus scrapes from observability-vm to land

**Performance** (tuned 2026-05-25):

| Model | Decode | Prefill (cold) | Context |
|---|---|---|---|
| local-moe @ n_cpu_moe=16 | **47 tok/s** | 752 tok/s | 150K (n_ctx=150016) |
| local-reasoning @ Q4_K_M | (untuned) | — | 170K (n_ctx=170240) |

Model architectural max is 262144 for local-moe (Qwen3.6) and 262144 for
local-reasoning (Qwen3.5).

## Current model layout

| Model | Engine | Host | Quant | Notes |
|---|---|---|---|---|
| local-moe (Qwen3.6-35B-A3B) | llama-server :8080 | inference-01 | UD-IQ3_XXS (~13 GB) | 150K ctx via MoE expert offload (n_cpu_moe=16, q4_0 KV) — see `references/tuning.md` |
| local-reasoning (Qwen3.5-9B) | llama-server :8080 | inference-02 | Q4_K_M (~5.7 GB) | 170K ctx, q4_0 KV. Migrated off LM Studio because high-context loads need flags `lms` doesn't expose |
| local-voice (Voxtral-Mini-3B) | lmstudio :1234 | inference-02 | Q4_K_M (~3.8 GB) | Architectural max ctx: 32K, so LM Studio default is fine |

**No runners on the inference hosts.** Everything (provisioning, config
deploys, model sync, container restarts) is driven from terraform-runner via
Ansible-over-SSH. The Ansible deploy key lives in Infisical at `/ansible`.

## Repo Layout

```
local-inference-infrastructure/
├── ansible/
│   ├── inference-setup.yml             # Per-inference-host playbook (LM Studio + UFW + observability)
│   ├── sync-models.yml                 # Per-host model reconcile (runs lms get over SSH)
│   ├── deploy-alloy.yml                # Push Alloy config, restart container
│   ├── litellm-gateway-setup.yml       # Gateway VM: Postgres + LiteLLM + Open WebUI containers
│   ├── litellm-deploy-config.yml       # Ships rendered config + hooks to gateway, restarts container
│   ├── tts-gateway-setup.yml           # tts-gateway VM: Kokoro-FastAPI + 'zoe' voice blend (Phase 4)
│   ├── searxng-gateway-setup.yml       # searxng-gateway VM: SearXNG metasearch (Open WebUI RAG backend)
│   ├── playwright-gateway-setup.yml    # playwright-gateway VM: Chromium headless server (Open WebUI fetcher)
│   ├── templates/litellm.env.j2        # Secrets template (rendered on gateway)
│   └── vars/
│       ├── main.yml                    # Non-secret vars for inference hosts
│       └── litellm-gateway.yml         # Non-secret vars for gateway VM
├── configs/
│   ├── alloy/                          # Grafana Alloy (log shipping) config
│   ├── litellm/settings.yaml           # Global LiteLLM settings (manifest-independent)
│   └── prometheus/                     # Prometheus scrape config
├── models/manifest.yaml                # Source of truth for models + LiteLLM routing
├── scripts/
│   ├── bootstrap-deploy-key.sh         # One-time SSH-key install on a new inference host
│   ├── generate_litellm_config.py      # Manifest → litellm-config.yaml
│   ├── lmstudio_context_hook.py        # Host-aware preload hook
│   └── sync_models.py                  # Local-debug equivalent of ansible/sync-models.yml
├── terraform/nodes/
│   ├── litellm-gateway/                # Gateway VM (VMID 300)
│   └── ...
└── .github/workflows/
    ├── provision-litellm-gateway.yml   # Provision + bring up gateway containers
    ├── deploy-litellm.yml              # Render config + ship to gateway
    ├── provision-inference.yml         # Provision/re-apply an inference host (SSH)
    ├── sync-models.yml                 # Manifest → GGUF on each inference host (SSH)
    └── deploy-alloy.yml                # Alloy config push (SSH)
```

## Workflows

| Workflow | Trigger | Runs on | What it does |
|----------|---------|---------|--------------|
| `provision-litellm-gateway.yml` | manual | `self-hosted-infra` | Terraform proxmox-vm + base-vm.yml + litellm-gateway-setup.yml over SSH. 2 jobs. |
| `deploy-litellm.yml` | push to `models/manifest.yaml`, `configs/litellm/settings.yaml`, `scripts/*_hook.py`; manual | `self-hosted-infra` | Generate config + run unit tests, ship to gateway via SSH, restart container, wait for `/health/liveliness`. |
| `provision-inference.yml` | manual, per-host | `self-hosted-infra` | Resolves `inference-XX` → `.lan` hostname → IP via dns-vm, runs `ansible/inference-setup.yml` over SSH. Accepts `tags` and `skip_tags` inputs. |
| `sync-models.yml` | push to `models/manifest.yaml`; manual | `self-hosted-infra` | Matrix over both inference hosts; each cell runs `ansible/sync-models.yml` over SSH to `lms get` the host's manifest slice. |
| `deploy-alloy.yml` | `configs/alloy/**` change; manual | `self-hosted-infra` | Matrix over both inference hosts; runs `ansible/deploy-alloy.yml` over SSH to copy config + conditionally `docker restart alloy`. |

**All five workflows share the same template** — resolve hostname → IP via
`dig @192.168.0.250`, load `/ansible` from Infisical via OIDC, write
`$HOME/.ssh/deploy_key`, run `ansible-playbook -i "$IP," ...`, clean up the
key. See `provision-inference.yml` for the canonical version.

## DNS resolution at workflow time

terraform-runner sits on 192.168.1.0/24; the authoritative `.lan` resolver
(dns-vm) is on 192.168.0.250. terraform-runner does NOT use dns-vm as its
system resolver, so `.lan` hostnames don't resolve through the normal stack.

**The pattern in every inference-host workflow:**

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
```

DHCP renumbering becomes transparent — each workflow run re-queries dns-vm.
No repo variable to update, no terraform-runner config change needed.

## First bring-up & ongoing ops

**Fresh gateway:**
1. `provision-litellm-gateway.yml` — VM exists, containers running, but
   LiteLLM crash-loops because `litellm-config.yaml` doesn't exist yet.
2. `deploy-litellm.yml` — config rendered, shipped, container restarted,
   `/health/liveliness` passes.

**Fresh inference host:**
1. `scripts/bootstrap-deploy-key.sh` (run from a shell with Infisical linked
   to the shared homelab project) — installs the Ansible deploy public key
   into `~blake/.ssh/authorized_keys` on the new host. Idempotent.
2. `provision-inference.yml` with `host=inference-XX` — full Phase 1–5 run.
   On a box without an NVIDIA driver, Phase 2 installs + reboots the box and
   the playbook exits with a "REBOOT IN PROGRESS" message.
3. Re-trigger with `skip_tags=disk,drivers` to resume from Phase 3.
4. `sync-models.yml` — downloads the host's manifest slice of GGUFs.
5. End-to-end check via LiteLLM `/health` with master key.

**Already-provisioned host — fast smoke test:**
`provision-inference.yml -f host=inference-XX -f tags=lmstudio,services`
skips disk/drivers/docker. Runs in ~30 sec, exercises the SSH plumbing and
re-applies LM Studio + observability config idempotently.

## Inference-setup precheck pattern

`ansible/inference-setup.yml` is the same playbook for fresh boxes and
already-provisioned ones; it self-skips disruptive phases:

- **Phase 1 (disk):** `stat /dev/ubuntu-vg/ubuntu-lv` first. If the LV
  doesn't exist (physical box without cloud-image LVM), the `lvextend` /
  `resize2fs` tasks are skipped with a "physical box without cloud-image
  LVM" note.
- **Phase 2 (drivers):** `nvidia-smi` precheck. If it returns 0, the
  `ubuntu-drivers install` + reboot + intentional-failure tasks are all
  gated `when: nvidia_smi_precheck.rc != 0` and skipped silently.
- **Phase 4 (lmstudio):** `lms load qwen3.5-9b` (both the systemd
  ExecStartPost and the standalone task) is best-effort. On a fresh box
  with no GGUFs yet, the model file doesn't exist and the load fails
  cleanly: systemd's `-` prefix on ExecStartPost ignores the failure;
  the standalone task has `failed_when: false`. `LMStudioContextHook`
  in LiteLLM also preloads on first request, so the boot-time load is
  a warm-start optimization, not a correctness requirement.

## Configured local models (per `models/manifest.yaml`)

Three local models, each **pinned to exactly one host** to prevent
thrashing (LM Studio serves one model at a time per host; even with
llama-server's containerised model loading, mirroring just doubles
disk + VRAM for no routing gain).

| Alias | Pinned to | Engine | Quant | File size | Download via |
|-------|-----------|--------|-------|-----------|--------------|
| `local-moe` | inference-01 (12 GB VRAM) | llama-server :8080 | UD-IQ3_XXS | ~13 GB | `get_url` (Ansible) — unsloth/Qwen3.6-35B-A3B-GGUF |
| `local-reasoning` | inference-02 (6 GB VRAM, 78 GB RAM) | llama-server :8080 | Q4_K_M | ~5.7 GB | `get_url` (Ansible) — unsloth/Qwen3.5-9B-GGUF |
| `local-voice` | inference-02 (6 GB VRAM, 78 GB RAM) | lmstudio :1234 | Q4_K_M | ~3.8 GB | `lms get` — bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF |

**Why this layout:** see the Hardware profile section above. The 35B MoE
needs the most memory and gets inference-01's 12 GB VRAM (with experts
offloaded to its 15 GB RAM via `--n-cpu-moe`). The 9B reasoning model
wants 170K context, which exceeds LM Studio's CLI-controllable settings
on inference-02's 6 GB VRAM, so it also runs through llama-server (with
q4_0 KV quant). Voxtral has a 32K architectural ceiling and serves
fine from LM Studio defaults.

Manifest's `inference_hosts:` map names each engine backend with its
`api_base`. Each model's `hosts: [...]` + `engine:` pair selects the
backend. The generator emits one `model_list` entry per `(model, host)`
pair, picking the named engine's port for that host.

### Download path (sync-models)

`ansible/sync-models.yml` dispatches on the model's `engine`:

| Engine | Download mechanism | Notes |
|---|---|---|
| `lmstudio` | `lms get` (catalog or HF URL) | `prefer_hf: true` on the model uses the HF URL form: `lms get https://huggingface.co/{hf_repo}@{lms_quant}`. The `@quant` token is the **bare quant** (`@IQ3_XXS`), not `@UD-IQ3_XXS` — LM Studio strips structural prefixes |
| `llama-server` | `ansible.builtin.get_url` | Pulls the specific `hf_file` to `~/llama-server/models/<hf_repo>/<hf_file>`. The parent dir is created explicitly with `file: state: directory` first — `get_url` does NOT `mkdir -p` |

## Cloud relay models

| Alias | Routes To | Auth |
|-------|-----------|------|
| `anthropic_auto` | complexity router → `claude-haiku-4-5` / `claude-sonnet-4-6` / `claude-opus-4-7` | ANTHROPIC_API_KEY env |
| `claude-sonnet` / `-haiku` / `-opus` | latest of each tier | ANTHROPIC_API_KEY env |
| `claude-sonnet-4-6`, `claude-opus-4-7`, `claude-opus-4-6`, `claude-haiku-4-5`, `claude-haiku-4-5-20251001` | exact-name passthrough for Claude Code | Forwarded OAuth (Max subscription) |

## TTS via Kokoro on tts-gateway

Open WebUI's "read reply aloud" feature talks to a dedicated `tts-gateway` VM running Kokoro-FastAPI. LM Studio doesn't expose `/v1/audio/speech` as of 2026-05 (lmstudio-bug-tracker#1715), so TTS lives separately rather than on inference-01/02.

| Property | Value |
|----------|-------|
| Host | `tts-gateway.lan` / 192.168.0.25 (VMID 303) |
| Image | `ghcr.io/remsky/kokoro-fastapi-cpu:v0.2.4` (CPU-only) |
| Port | 8880, UFW-restricted to 192.168.0.5 (litellm-gateway) |
| Voices dir | `/app/api/src/voices/v1_0/` (the only dir Kokoro scans) |
| Persistence | `tts-voices-api` Docker volume mounted over `v1_0` |
| Cluster default | `AUDIO_TTS_VOICE=zoe` (custom weighted blend) |

### Voice file format

Each voice is a plain PyTorch tensor of shape `[510, 1, 256]` saved at `/app/api/src/voices/v1_0/<name>.pt`. `list_voices()` literally `os.listdir`'s that one directory at request time — no caching, no registry. Drop a `.pt` file in and it's available immediately.

The 54 baked-in voices follow the `{lang}{gender}_{name}` convention: `af_*` American Female, `am_*` American Male, `bf_*` British Female, `bm_*` British Male, then `ef/ff/hf/if/jf/pf/zf` for Spanish/French/Hindi/Italian/Japanese/Brazilian-Portuguese/Mandarin. Voice naturalness varies — `af_nicole` is the most "real-sounding" but very quiet/whispery; `bf_emma` is the most authoritative British; `af_heart` is the most expressive; the four British male voices (`bm_daniel/fable/george/lewis`) are useful as low-pitch blend partners.

### Custom voice blends — two paths, only one of which actually accepts weights

**Inline at synthesis time** (works, supports weights): pass a `+`-delimited blend with parenthetical weights as the `voice` parameter of `/v1/audio/speech`, e.g. `voice: "bf_emma(0.7)+bm_lewis(0.3)"`. Kokoro normalises the weights to sum to 1.0 and computes the blend on the fly. Open WebUI's voice field accepts this string verbatim.

**Persisted as a named voice** (requires manual tensor materialisation): the `POST /v1/audio/voices/combine` endpoint exists, but its implementation is `torch.mean(torch.stack(voice_tensors), dim=0)` — **equal weighting only, no per-voice ratios**. The inline `(N)` syntax is parsed *only* by `/v1/audio/speech`, not by combine. The endpoint also 403s unless `ALLOW_LOCAL_VOICE_SAVING=true` (default false), and saves to `tempfile.gettempdir()` rather than to the voices_dir — so even when it works, the result isn't auto-discoverable.

To persist a *weighted* blend as a named, dropdown-selectable voice, build the tensor directly inside the container and `torch.save` it into the voices_dir. The pattern (see `ansible/tts-gateway-setup.yml` Phase 4):

```bash
docker exec -i tts python3 - <<'PY'
import torch
d = '/app/api/src/voices/v1_0'
spec = [('af_nicole', 0.4), ('af_heart', 0.3), ('af_bella', 0.15), ('bf_emma', 0.15)]
zoe = sum((torch.load(f'{d}/{v}.pt') * w for v, w in spec[1:]),
          torch.load(f'{d}/{spec[0][0]}.pt') * spec[0][1])
torch.save(zoe, f'{d}/zoe.pt')
PY
```

The current `zoe` blend (Nicole 40 / Heart 30 / Bella 15 / Emma 15) was tuned for: natural-sounding (Nicole), enough inflection to not read like a script (Heart), enough projection to not whisper (Bella), and a faint British edge (Emma).

**Idempotency caveat for Phase 4:** the existing playbook guards the rebuild on `when: zoe_check.rc != 0` — meaning weight tweaks in `blend_spec` are silently ignored once the file exists. To iterate on the recipe via the playbook, either drop the guard (always rebuild — sub-second CPU, deterministic so idempotency is preserved by content equality) or hash the spec to a sidecar. See `[[feedback_ansible_idempotency_swallows_spec_change]]`.

### Voice blending heuristics

Three principles do most of the work when designing or tuning a blend:

1. **One dominant voice (40-50%) + 2-3 flavor voices (15-25% each)** is more stable than two-voice 70/30 blends — fewer audible artifacts at phrase boundaries.
2. **15-25% of an opposite-gender voice drops perceived pitch without changing perceived gender.** `bm_george` (gentle British male) is the cleanest pitch-control lever for a female-dominant blend; `am_liam` similarly for American-leaning blends. The gender-ambiguity threshold is ~35% — past that, listeners start hearing the second voice as distinct rather than as a tonal shift.
3. **20-30% of `af_heart` smooths almost any blend.** Heart is the Kokoro team's polished flagship and acts as a naturalness anchor. If a blend sounds robotic, add Heart.

Voice-character notes built from listening:
- `af_nicole` — most "real-sounding" but quiet/whispery; dial down when projection is needed
- `af_bella` — bright, warm, expressive; the projection lever — also pulls pitch up, so balance with male blend partner if needed
- `af_heart` — flagship, polished, expressive; the naturalness anchor
- `af_sarah` / `af_nova` / `af_sky` — bright/clear; add for crispness, raise pitch
- `af_river` / `af_aoede` — soft, calm; use for meditation/narrator vibes
- `bf_emma` — most authoritative British female; best accent-flavor anchor
- `bf_alice` — robotic-leaning; avoid as primary
- `bm_george` — gentle British male; the pitch-drop lever for female blends
- `bm_fable` — narrator-flavored; great for audiobook builds

**Iteration loop without re-deploying:** paste the blend string directly into **Open WebUI → Settings → Audio → Voice** (e.g. `af_nicole(0.3)+af_heart(0.3)+af_bella(0.15)+bm_george(0.15)+bf_emma(0.1)`). Open WebUI passes the string verbatim through to Kokoro's `/v1/audio/speech` at synthesis time, so the speaker icon plays the new blend immediately. No `.pt` file, no playbook run, no container restart. Only materialise to a persisted named voice via Phase 4 once you've settled on weights.

For systematic exploration with a UI: [`ysharma/Make_Custom_Voices_With_KokoroTTS`](https://huggingface.co/spaces/ysharma/Make_Custom_Voices_With_KokoroTTS) has live sliders.

### Open WebUI integration knobs

The Open WebUI container on litellm-gateway is fed five env vars (set in `ansible/litellm-gateway-setup.yml`, values from `ansible/vars/litellm-gateway.yml`):

- `AUDIO_TTS_ENGINE=openai`
- `AUDIO_TTS_OPENAI_API_BASE_URL=http://192.168.0.25:8880/v1`
- `AUDIO_TTS_OPENAI_API_KEY=not-needed` (Kokoro doesn't auth, but Open WebUI rejects empty)
- `AUDIO_TTS_MODEL=kokoro`
- `AUDIO_TTS_VOICE={{ tts_default_voice }}`

**Env vars only seed the DB on first boot** — see `[[feedback_open_webui_persistent_config]]`. After changing `AUDIO_TTS_*`, you also need to update Admin Panel → Settings → Audio in the UI, or hit a fresh DB.

`Admin Panel → Settings → Audio → Response Splitting = Punctuation` materially improves Kokoro's prosody — Kokoro generates flatter audio on long inputs because it has limited "fresh-start inflection budget" per call. Sentence-level splits give it new prosody runway per request. Worth setting alongside any voice tuning.

### Volume mount gotcha to remember

The named volume must mount onto `/app/api/src/voices/v1_0` (the configured `voices_dir`), not a sibling like `/api`. Mounting elsewhere persists files Kokoro will never read. On first start with an empty named volume, Docker auto-populates it with the image's baked-in voices, so subsequent recreates retain both the 54 originals plus any custom `.pt` files. The tradeoff: future Kokoro versions with new baked-in voices won't appear unless you `docker volume rm tts-voices-api` (and re-run the playbook to rebuild custom blends).

## Infisical setup

Two Infisical projects, same OIDC machine identity bound to both:

| Project slug | Repo var | Contents |
|--------------|----------|----------|
| `homelab-platform-eu2-m` | `INFISICAL_PROJECT_SLUG` | `/proxmox`, `/terraform`, `/ansible` (shared with all homelab repos) |
| `local-inference-infrastructure-pca-l` | `LITELLM_INFISICAL_PROJECT_SLUG` | `/litellm` (`LITELLM_MASTER_KEY`, `LITELLM_POSTGRES_PASSWORD`, optional `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`) |

The OIDC identity (`vars.INFISICAL_IDENTITY_ID`) is granted Project Access
to both. See `/homelab-bootstrap` "Multi-Project Infisical Pattern".

## Repo variables

| Variable | Purpose |
|----------|---------|
| `INFISICAL_IDENTITY_ID` | OIDC machine identity for both Infisical projects |
| `INFISICAL_DOMAIN` | Self-hosted Infisical URL (`http://192.168.0.161`) |
| `INFISICAL_PROJECT_SLUG` | Shared-infra project slug |
| `LITELLM_INFISICAL_PROJECT_SLUG` | LiteLLM-secrets project slug |
| `LITELLM_GATEWAY_IP` | Gateway VM IP (192.168.0.5) — read by `deploy-litellm.yml` so it doesn't have to re-Terraform |

Repo secret `ANSIBLE_BECOME_PASSWORD` holds `blake`'s sudo password on the
inference hosts (`blake` is NOT NOPASSWD on either box).

## Project skills

- `/litellm` — general LiteLLM knowledge
- `/homelab-bootstrap` — cross-repo gotchas (Ansible, Terraform, Infisical, runners, DNS-resolution-from-runners)
- `/infisical-cli` — Infisical CLI operations
- `/technitium-api` — query Technitium directly (e.g. read current DHCP IP for a hostname)

## Known issues

- **High-context loads have moved off LM Studio** (resolved 2026-05-25 by
  migrating both `local-moe` and `local-reasoning` to the llama-server
  engine). The original `LMStudioContextHook` tried to POST
  `/api/v1/models/load` with a per-request context length, but LM Studio's
  REST schema rejects that body shape (`400 Unrecognized key(s)`) — and
  the protocol is now proprietary JSON-RPC anyway. Rather than chase
  LM Studio's moving target, models that need a non-default context now
  use llama-server, where context is baked into the container's
  `--ctx-size` at start. **`scripts/lmstudio_context_hook.py` was deleted**
  along with its memory-leaking test (`tests/test_lmstudio_context_hook.py`).
  The only lmstudio-engine model left is `local-voice` (Voxtral, 32K
  architectural max) where LM Studio's default context is plenty.
- **LM Studio serves one model at a time** per host. Still true for the
  one lmstudio-engine model we have (Voxtral on inference-02). Switching
  requires `lms load <model>`. With llama-server, each model is its own
  container so there's no switching cost.
- **LiteLLM reads config at startup only**; bind-mount changes require
  `docker restart litellm`. `litellm-deploy-config.yml` handles this every
  deploy.
- **First bring-up of a fresh gateway has a chicken-and-egg** between
  provision (writes `.env`, starts container) and deploy (ships
  `litellm-config.yaml`). LiteLLM crash-loops until the first
  `deploy-litellm.yml` run completes. Expected.
- **`OTEL_EXPORTER_OTLP_ENDPOINT`** in `litellm.env.j2` is conditional on
  `observability_server_ip` being set; safe to leave unset until the
  observability stack lands.
- **dcgm-exporter on inference-01 ("driver not loaded")** — observed
  2026-05-25 when re-running `provision-inference.yml` against ubuntu-tower
  with `tags=services`. The fresh dcgm container couldn't load NVML even
  though `nvidia-smi` works. Root cause likely a drift in
  `nvidia-ctk runtime configure` state on that specific box. Workaround:
  re-run provision with `--tags docker` to redo the toolkit setup +
  docker daemon restart. Doesn't affect inference itself, only GPU
  metrics export.

## Decommissioning notes

- **Pre-May-2026 (gateway-on-inference-machine era):** if you see refs to
  `[self-hosted, self-hosted-litellm-gateway]` runners or LiteLLM running
  on `192.168.0.52`, those are stale — gateway moved to its own VM and
  the `self-hosted-litellm-gateway` runner was deleted.
- **Pre-May-2026 (per-inference-host runner era):** if you see refs to
  `self-hosted-inference-01` / `self-hosted-inference-02` labels in
  workflow files, those workflows pre-date the Ansible-over-SSH refactor.
  The `ubuntu-tower` GH Actions runner registration was deleted; only
  `terraform-runner` is registered now.
- **Pre-May-2026 (`inference-02` at `192.168.0.211`):** the host was
  renumbered/replaced with `ubuntu-tower-02.lan` (currently 192.168.0.217
  via DHCP). All current code uses `.lan` hostnames + dns-vm lookup;
  if you find stale `.211` references in docs, they're cleanup follow-ups.
