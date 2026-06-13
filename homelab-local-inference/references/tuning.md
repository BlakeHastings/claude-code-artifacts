# Performance tuning — local-moe & llama-server

How we tuned `--n-cpu-moe` on local-moe (Qwen3.6-35B-A3B on inference-01,
RTX 3060 12 GB, 150K context), plus the methodology you'd reuse for any
future engine_args sweep.

## TL;DR — current production settings

| Model | n_cpu_moe | KV quant | Decode tok/s | Notes |
|---|---|---|---|---|
| local-moe | **16** | q4_0 K + V | **47** | +17% vs the conservative starting value of 20 |
| local-reasoning | (no MoE) | q4_0 K + V | (untuned) | 9B model; bottlenecks differ |

Bumping `n_cpu_moe` further down (8) gave **~58 tok/s on typical prompts**
but crashed on some workloads. Stability won the trade-off; if you need
more headroom, sweep `cache_type_k/v` to free VRAM first, then re-test.

## Two non-obvious findings

**1. Decode tok/s vs `n_cpu_moe` is NOT monotonic.** I expected lower
n_cpu_moe (more experts on GPU) to be monotonically faster until VRAM ran
out. The actual measured curve:

```
  n_cpu_moe=20 → 40.0 tok/s   (baseline, very stable)
  n_cpu_moe=16 → 46.9 tok/s   (+17%, peak stable)
  n_cpu_moe=12 → 43.0 tok/s   (regressed — slower than 16!)
  n_cpu_moe=8  → ~58 tok/s on safe prompts, crashes on others
```

The 16 → 12 regression was reproducible across runs. Plausible causes:
layer-boundary alignment with KV cache layout, or a PCIe scheduling
sweet-spot where CPU-side expert dispatch overlaps optimally with GPU
work at exactly one offload depth. Either way: **don't extrapolate the
curve — sweep it.**

**2. The VRAM ceiling isn't a clean OOM.** At `n_cpu_moe=8` the container
loaded the model fine, served `/health 200`, handled short prompts at
57 tok/s, then crashed mid-decode on certain longer prompts. Symptom on
the client: `http.client.RemoteDisconnected`. Symptom on llama-server:
silent exit + `restart: unless-stopped` brings it back (you'd see
`n_decode_total` in `/metrics` reset to 0). It's a *runtime peak* VRAM
problem, not a load-time check — model fits, transient intermediates
during long generation don't.

Implication: "tiny gen works after restart" is NOT proof a setting is
stable. Probe with realistic prompt sizes (1-4K tokens) AND full
target `max_tokens` (256+) before locking a value in.

## Sweep methodology

### Bench client — no LiteLLM auth needed

llama-server is OpenAI-compatible and includes a `timings` block in every
response with `prompt_per_second` and `predicted_per_second` (i.e.
prefill + decode tok/s) straight from the C++ side. **The response
payload is a better instrument than Prometheus for the sweep itself** —
per-request granular, no scrape delay, no master key required.

Hit it directly. The UFW rule allows the gateway IP + the observability
IP; if you're sweeping from a different host on the LAN, briefly add a
matching `ufw allow from <your-IP> to any port 8080` on the inference
host.

```python
import json, time, urllib.request

URL = "http://ubuntu-tower.lan:8080/v1/chat/completions"

def one_call(prompt, max_tokens=256):
    body = json.dumps({
        "model": "Qwen3.6-35B-A3B-UD-IQ3_XXS.gguf",
        "messages": [{"role": "user", "content": prompt}],
        "max_tokens": max_tokens,
        "temperature": 0.2,
    }).encode()
    req = urllib.request.Request(URL, data=body,
                                 headers={"Content-Type": "application/json"})
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=600) as r:
        d = json.loads(r.read())
    t = d.get("timings", {})
    return {
        "wall_s": round(time.time()-t0, 2),
        "prompt_n": t.get("prompt_n"),
        "predicted_n": t.get("predicted_n"),
        "prefill_tps": round(t.get("prompt_per_second", 0), 1),
        "decode_tps": round(t.get("predicted_per_second", 0), 1),
    }
```

### Cached-prompt trick to isolate decode

llama-server enables prompt caching by default (`--cache-ram 0` would
disable). Run the same prompt twice:

- **Run 1**: cold-cache. Full prefill (`prompt_n` matches your input
  token count), reports both prefill and decode tok/s honestly.
- **Run 2-3**: warm-cache. `prompt_n` drops to ~4 (just the assistant
  turn delta), so the measurement is dominated by decode.

This is exactly what you want for tuning `--n-cpu-moe`: that flag mostly
affects decode (every generated token routes through MoE layers, some on
CPU). Prefill is more bandwidth-bound and shows less variation per
n_cpu_moe value.

### Iteration loop

1. Edit `models/manifest.yaml` — change one `engine_args` value (e.g.
   `n_cpu_moe: 16`)
2. `git add` the manifest, commit + push
3. `gh workflow run "Provision Inference Machine" -f host=inference-01 -f tags=llama-server`
4. Wait for the run to complete (~30 sec excluding queue; queue can be 5-15 min
   on a busy runner)
5. Probe `curl http://<host>:8080/health` until 200
6. Run bench (3 runs at the target prompt). First run is cold-cache
   prefill+decode; runs 2-3 are pure decode.
7. Record decode tok/s + any crashes

**Each cycle is roughly 2-15 min** depending on runner queue. Plan your
sweep — don't impulsively try 5 values back-to-back.

### Choose your step size

For a first pass, step by `4` is good. Steps of 2 are too fine to see
the curve through measurement noise (decode σ is ~0.5 tok/s within a
single n_cpu_moe value). Steps of 8+ skip past the optimum.

Once you've bracketed the peak (e.g. "16 is the best of 20/16/12"),
optionally test the midpoints (`18`, `14`) for a final refinement.

## What to do when you hit the ceiling

If a value causes `RemoteDisconnected` crashes on realistic workloads
but works on tiny prompts, you've found the dynamic VRAM ceiling. Two
options:

1. **Back off** to the next-higher `n_cpu_moe` value (safer KV margin).
2. **Free VRAM via KV quant**: drop `cache_type_k`/`cache_type_v` from
   `q4_0` to `q3_K` or change `flash_attn: on` (already on for us)
   — then re-sweep `n_cpu_moe`. Lower KV precision costs perplexity but
   buys you more aggressive expert offload room.

For local-moe at 150K context the math: KV cache at q4_0 is roughly 64 KB
per token × 150,000 = ~9.6 GB on a 12 GB GPU. That leaves ~2-3 GB for
the dense layers and remaining MoE experts at low n_cpu_moe — tight,
which is why the ceiling is workload-dependent rather than a clean OOM.

## Verifying after a manifest change

Cold-cache benchmark on a freshly-provisioned container:

```
=== PRODUCTION n_cpu_moe=16: 3 runs ===
  run 1: prefill= 752.2 tok/s  decode= 47.1 tok/s  wall=9.11s  (2706 in / 256 out)
  run 2: prefill=  65.0 tok/s  decode= 46.9 tok/s  wall=5.55s  (4 in / 256 out)
  run 3: prefill=  64.4 tok/s  decode= 46.8 tok/s  wall=5.57s  (4 in / 256 out)
```

Reproducibility check: run-to-run decode σ stays below 0.5 tok/s if the
container is healthy. Wider variance suggests background load, throttling,
or imminent OOM — investigate before trusting the average.

## Prometheus / Grafana — the right tool for ongoing observation

The response-payload approach above is best for the sweep itself
(immediate, per-request, no auth). For ongoing performance observation
and alerting, query Prometheus:

| Series | What it tells you |
|---|---|
| `rate(llamacpp:predicted_tokens_seconds_sum[1m]) / rate(_count[1m])` | True decode tok/s averaged over recent requests |
| `rate(llamacpp:prompt_tokens_seconds_sum[1m])` | Prefill tok/s — sensitive to GPU layer count, not expert offload |
| `llamacpp:kv_cache_usage_ratio` | KV cache fill — early warning before OOM |
| `llamacpp:requests_processing / requests_deferred` | Slot saturation (n_slots = 4 by default) |
| `DCGM_FI_DEV_FB_USED{Hostname="ubuntu-tower"}` | VRAM headroom during normal load |
| `node_memory_MemAvailable_bytes{node="inference-01"}` | RAM headroom for expert weights |
| `DCGM_FI_DEV_GPU_UTIL` | GPU busy % (drops when CPU expert dispatch bottlenecks) |

Prometheus jobs are `llama-server-inference-01` and `-02` (added to
`homelab-services/services/observability/prometheus.yml`); the
`engine="llama-server"` label distinguishes these scrapes from
node_exporter / DCGM on the same physical host.

## When to re-tune

Re-sweep if you change ANY of:
- the GGUF (different quant, different upload, model version)
- `cache_type_k` or `cache_type_v`
- `context_length`
- `flash_attn` state
- the GPU (different model, driver upgrade that changes scheduler)

In particular: a smaller KV quant frees VRAM and may unlock a lower
`n_cpu_moe` value than was stable before. Don't assume yesterday's
sweet spot still applies.

## See also

- Memory: `feedback_moe_n_cpu_moe_tuning.md` — short-form pointer to this doc
- Memory: `feedback_moe_vram_offload_tradeoff.md` — why MoE picks VRAM-rich hosts
- Memory: `feedback_llama_server_via_docker.md` — the engine recipe
- SKILL.md "Multi-engine architecture" — how engine_args plumbs from manifest into container ExecStart
