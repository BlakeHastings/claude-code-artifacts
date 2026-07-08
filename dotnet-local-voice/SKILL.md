---
name: dotnet-local-voice
description: Building self-hosted/local text-to-speech and real-time audio playback in a .NET/C# app. Use when choosing an open-source TTS engine for self-hosting (Kokoro, Orpheus, Kyutai, Piper, XTTS, Fish, Chatterbox, CosyVoice) and weighing latency, license, and VRAM; integrating KokoroSharp (in-process ONNX TTS) including getting raw PCM without auto-play, model download, voices, and espeak-ng; doing audio output with PortAudioSharp2 (callback-only, transitive native runtimes); stopping a continuously-listening assistant from transcribing its own TTS (acoustic echo cancellation with WebRTC APM via SoundFlow.Extensions.WebRtc.Apm, mic-mute vs AEC vs an inaudible cross-device watermark, full-duplex vs two independent PortAudio streams when mic and speaker are separate clocks, anti-aliased resampling via NAudio.Core, barge-in); diagnosing a render-path buzz/choppiness or an endpointer that stalls 10+ seconds (push-resampler imaging from draining it dry, jitter-buffer underruns from bursty TTS and the pre-roll fix, why a watermark/noise gate must zero frames not drop them, ratio detectors needing an absolute level floor); debugging audio artifacts offline with float-WAV stage taps, a numpy analyzer, and a deterministic resampler probe; or reflecting an unfamiliar audio/TTS NuGet assembly to find its real API. Covers commercial-license traps and the in-process-vs-hosted engine decision.
---

# Self-hosted voice (TTS + audio I/O) in .NET

How to wire local text-to-speech and audio output in a .NET app without a cloud API. Five parts: pick an engine, integrate KokoroSharp, play audio with PortAudio, stop the assistant from hearing itself (echo cancellation), and recover an unfamiliar library's API.

## Choosing a self-hosted TTS engine

> World-state: the landscape below was observed mid-2026 at the versions noted. TTS moves fast; re-check licenses and benchmarks before committing. The durable part is the **two-tier split** and the **license traps**.

Two tiers, matched to deployment:

- **In-process / small (CPU-friendly):** Kokoro-82M (Apache-2.0, 24 kHz, ~2x real-time on CPU, no voice cloning), Piper (tiny VITS, fast, but GPL-3.0). Ship inside the app, no extra service.
- **Hosted / GPU (expressive, cloning):** Orpheus-3B, Kyutai TTS, CosyVoice2, Chatterbox. Run as a server, call over HTTP.

**Orpheus** is the standout for reusing existing LLM infra: it is a Llama-3.2-3B model that emits SNAC audio-codec tokens, so it runs on llama.cpp / vLLM / LM Studio (GGUF quants) you may already operate. Needs a small SNAC decoder to turn tokens into PCM. Apache-2.0 (code + weights), 24 kHz, ~200 ms streaming, emotion tags + zero-shot cloning. **Kyutai TTS** is the one that streams *text in* (pipe LLM tokens as they generate) for lowest time-to-first-audio; CC-BY-4.0, Rust websocket server.

**License traps (the durable lesson):** a permissive *code* license does not mean permissive *weights*.
- Safe for commercial/self-host: Kokoro (Apache), Orpheus (Apache), Chatterbox (MIT, but English-only + watermarked), Kyutai (CC-BY).
- Not commercial: **XTTS-v2 (CPML)**, **F5-TTS weights (CC-BY-NC)**, **Fish Speech weights (CC-BY-NC-SA)**. Quality is good; the weight license disqualifies them.
- Copyleft: **Piper (GPL-3.0)**, and KokoroSharp bundles **espeak-ng (GPLv3)** — fine when invoked as a separate binary, flag it if redistributing.

**Decision rule:** default to in-process Kokoro for "runs anywhere, CPU, no infra." Put it behind an interface and add a hosted engine (Orpheus behind an OpenAI-compatible `/v1/audio/speech` endpoint, e.g. via a LiteLLM gateway) as a config-selected override when you want expressiveness/cloning. Both Kokoro and Orpheus output 24 kHz, so no resampling between them.

For lowest latency on long replies, synthesize per **clause/sentence** (split the LLM token stream on `.!?,` or a max-char cap) and stream each chunk's audio as it is ready, rather than waiting for the whole response. Time-to-first-audio drops from full-response to first-clause.

## KokoroSharp (in-process ONNX TTS in C#)

> Observed against KokoroSharp 0.6.7. The package API has churned; verify type/method names against the installed version (see "Discovering an unfamiliar NuGet's API" below).

Packages: `KokoroSharp` (core) + one runtime: `KokoroSharp.CPU` (plug-and-play, bundles onnxruntime CPU) or `KokoroSharp.GPU.Windows` / `KokoroSharp.GPU.Linux`. Licenses: engine MIT, model Apache-2.0, bundled **espeak-ng GPLv3**. The build copies `espeak/`, `voices/`, and `runtimes/` next to the app and into the publish output / Docker image.

**Get raw PCM without auto-playing** (the key gotcha): the high-level `KokoroTTS.Speak(text, voice, config)` and `SpeakFast(...)` AUTO-PLAY through NAudio — useless for a streaming pipeline that needs samples. Instead:

```csharp
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions; // Web SDK: disambiguate from AspNetCore.Builder.SessionOptions

var synth = new KokoroSharp.Utilities.KokoroWavSynthesizer(modelPath, new SessionOptions());
var voice = KokoroSharp.KokoroVoiceManager.GetVoice("af_heart"); // call LoadVoicesFromPath(AppContext.BaseDirectory + "/voices") if not auto-loaded
byte[] wav = synth.Synthesize(text, voice, new KokoroSharp.Processing.KokoroTTSPipelineConfig()); // WAV bytes, no playback; handles long-text segmentation
```

Decode the WAV bytes to float PCM yourself. Lower-level alternative (skips WAV, you own segmentation): `KokoroSharp.Core.KokoroModel.Infer(int[] tokens, float[,,] voiceStyle, float speed)` returns `float[]`; tokenize via `KokoroSharp.Processing.Tokenizer.Tokenize(text, langCode, preprocess: true)`.

**Voice blends: `GetVoice` does NOT parse the blend expression.** `KokoroVoiceManager.GetVoice(name)` is a literal `First(v => v.Name == name)`, so a weighted-blend string like `"bf_emma(0.4)+bm_lewis(0.2)+af_heart(0.4)"` (the same syntax some Kokoro UIs accept) throws `InvalidOperationException: Sequence contains no matching element`. Parse it yourself and call `KokoroVoiceManager.Mix((KokoroVoice, float)[])` (or `MixWith(a, b, wA, wB)` for two). Split on `+`, parse each `name(weight)` term (bare name = weight 1), `GetVoice` each component, and normalize the weights so they need not sum to one:

```csharp
var terms = expr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var parts = terms.Select(t => { /* parse "name" or "name(weight)" */ return (KokoroVoiceManager.GetVoice(name), weight); }).ToArray();
var total = parts.Sum(p => p.weight); for (int i=0;i<parts.Length;i++) parts[i].weight /= total;
KokoroVoice voice = KokoroVoiceManager.Mix(parts);
```

Symptom in an agent/voice app: the streaming/text path works fine, then synthesis faults per-chunk on `KokoroVoiceManager.GetVoice` deep in the TTS load. A plain single name still goes straight through `GetVoice`.

**Model file:** `KokoroWavSynthesizer`/`KokoroModel` take a *path* and do not auto-download. `KokoroTTS.LoadModel(KModel)` auto-downloads but you do not control the location. To manage it like other models (an untracked `models/` dir, your own bootstrapper), fetch from the **taylorchu/kokoro-onnx v0.2.0** GitHub release (this is the build KokoroSharp targets):
- `kokoro.onnx` (fp32, ~325 MB) — best quality
- `kokoro-quant.onnx` (~177 MB)
- `kokoro-quant-convinteger.onnx` (int8, ~92 MB) — smallest
- `kokoro-quant-gpu.onnx`

`KModel` enum is `float32 | float16 | int8`. Output is **24 kHz mono**. Serialize `Synthesize` calls (the ONNX session is not assumed reentrant) and run them off the request thread.

## Audio output with PortAudioSharp2

> Observed against PortAudioSharp2 1.0.6.

**Callback-only — there is no blocking `Write`.** `PortAudioSharp.Stream` exposes `Start/Stop/Abort/Dispose` and a constructor callback, but no `Write`/`Read`. Fill the output buffer from the real-time callback:

```csharp
StreamCallbackResult OnCallback(IntPtr input, IntPtr output, uint frameCount,
    ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
{
    // pull up to frameCount samples from a bounded ring buffer; pad the rest with silence
    Marshal.Copy(scratch, 0, output, (int)frameCount);
    return StreamCallbackResult.Continue;
}
```

Keep a field reference to the `Stream.Callback` delegate so the GC does not collect it while native code holds it. The ring-buffer pattern gives you the right behaviors for free: **silence on underrun** (callback pads when the buffer is empty, instead of glitching) and **backpressure** (producer blocks when the buffer is full).

**Native libs are transitive runtime packages**, not bundled in the wrapper: `org.k2fsa.portaudio.runtime.{win-x64, linux-x64, linux-aarch64, osx-x64, osx-arm64}`. There is **no 32-bit ARM** build — a 32-bit Raspberry Pi OS has no native lib; use 64-bit. On Linux you may still need `libportaudio2` (apt). Call `PortAudio.LoadNativeLibrary()` (guarded in try/catch) before `PortAudio.Initialize()`.

## Stopping the assistant from hearing itself (echo cancellation)

> Observed against `SoundFlow.Extensions.WebRtc.Apm` 1.4.0 and PortAudioSharp2 1.0.6.

A continuously-listening assistant transcribes its own TTS unless you stop it. Three escalating approaches; pick by whether you need barge-in and multiple devices.

**1. Coarse mute (weakest).** Mute the mic while TTS plays, keyed on playback start/finish events. The trap: the "finished" event usually fires when the synth finishes **sending** frames, not when the speaker finishes **playing** them — speaker/hub buffers hold seconds of audio, so a short fixed hangover unmutes mid-playout and she transcribes her own tail. It also cannot do **barge-in** (the mic is dead while she talks). Fine only for short playback with no barge-in.

**2. Acoustic echo cancellation (AEC).** The real fix; also enables barge-in (the human survives as the residual). AEC is intrinsically a **single acoustic-endpoint** concern — it needs the mic and speaker sharing a room/clock with the played signal (far-end) available locally as the reference. Do it at the **edge device**, not a central service (shipping the reference cross-process moves it away from where it belongs).

Library: **`SoundFlow.Extensions.WebRtc.Apm`** wraps the WebRTC Audio Processing Module. Use the standalone **`AudioProcessingModule`**, NOT the SoundFlow audio engine (`WebRtcApmModifier`). Verified in 1.4.0: native binaries for `win-x64/x86`, `linux-x64`, **`linux-arm64`** (+ `linux-arm`, osx, android, ios) — broader RID coverage than PortAudio. Accepts only **8/16/32/48 kHz**; frames are **10 ms** and `AudioProcessingModule.GetFrameSize(rate)` is **static** (160 at 16 kHz):

```csharp
var apm = new AudioProcessingModule();
var cfg = new ApmConfig();
cfg.SetEchoCanceller(enabled: true, mobileMode: false);
if (apm.ApplyConfig(cfg) != ApmError.NoError) throw ...;  // ApmError.NoError on success
apm.Initialize();
var sc = new StreamConfig(sampleRateHz: 16000, numChannels: 1);   // float[][] is channels x samples; mono = 1
// render thread, per 10ms far-end (what you play):
apm.AnalyzeReverseStream(new[] { farFrame }, sc);
// capture thread, per 10ms near-end (mic):
apm.SetStreamDelayMs(delayMs);
apm.ProcessStream(new[] { micFrame }, sc, sc, new[] { outFrame });  // outFrame = echo-cancelled mic
```

`ProcessStream` (forward/capture) and `AnalyzeReverseStream` (reverse/render) are designed to run on **separate threads** concurrently (the normal VoIP usage); keep their per-instance scratch buffers distinct and it is safe.

**Full-duplex vs two streams — the clock gotcha.** A single PortAudio **full-duplex** stream (mic + speaker in one callback) hands you sample-aligned near/far for free, but only works when mic and speaker are **one device, one clock** (a Pi voice-puck sound card). On a dev PC where the mic (USB) and speakers (HDMI/monitor) are **separate devices with independent clocks**, one full-duplex stream **drifts → popping/glitches + high latency**. Tell from the device-open log: `in 'USB mic'` / `out 'monitor'` = two devices. Fix: run **two independent PortAudio streams** (capture + render) and let APM's `SetStreamDelayMs` + internal estimator reconcile the clocks (the standard software-AEC topology). Feed the AEC far-end **tapped at playout** — copy what the render callback writes to the speaker, not what you queue — so the echo-path delay stays small regardless of render-buffer depth (APM's max stream delay is bounded; a multi-second output buffer breaks AEC entirely).

**Resampling.** Processor/APM want 16 kHz, TTS is often 24 kHz, the device 48 kHz. **`NAudio.Core`**'s `WdlResamplingSampleProvider` is pure-managed and cross-platform (incl. arm64) — only NAudio's *device* I/O (`WaveInEvent`/`WaveOutEvent`) is Windows-only, so referencing `NAudio.Core` for the resampler alone is safe on a Pi. The 48→16 **downsample must anti-alias** (low-pass < 8 kHz) or higher-frequency content folds into the band as a ~helicopter chop; WDL does this.

**Windows dev latency (world-state):** PortAudio defaults to the high-latency **MME** host API on Windows; **WASAPI** is the low-latency path. ALSA on a Pi does not have this. Check the host API first if dev latency is bad.

**3. Cross-device watermark (multiple units in a home).** Local AEC cancels only a unit's **own** speaker; a neighbor unit's voice bleeding into this mic has no local reference, so AEC cannot touch it. The standard fix (Amazon/Google hold patents on exactly this) is an **inaudible watermark** in the TTS — a near-ultrasonic tone any mic detects → "this is the assistant, not a person; ignore." Hard constraint: a *truly* inaudible (>18 kHz) mark needs the **entire chain at ≥48 kHz** (mic capture, TTS output, speaker); at 16 kHz capture or 24 kHz TTS it is physically impossible (Nyquist 8/12 kHz). Detect on the raw high-rate capture *before* downsampling (the anti-alias filter strips it). Layer the two: AEC for local echo + barge-in, watermark for cross-device. Gate rule: treat a mic frame as another unit's voice when the watermark is present, this unit is **not** currently rendering (otherwise it is your own speaker — trust the AEC), **and** the frame has speech-level energy (see the level-floor gotcha below). **Suppress a gated frame by zeroing it (emit silence), never by dropping it** — dropping frames stalls recognition; see the next section.

## Streaming-pipeline gotchas: jitter, resampling, frame-gating

The capture/render path feeds a real-time, time-based consumer (a VAD/endpointer downstream, the speaker clock upstream). Four traps, each one cost a debugging session; all reproduce off-hardware (see the offline-debug section).

**Never DROP frames from a stream feeding a silence-based endpointer — zero them.** A turn-end endpointer decides an utterance ended by counting trailing *silence*. If anything upstream (a watermark gate, a noise filter) **drops** mic frames, it compresses the timeline and deletes the very silence the endpointer is waiting for, so the turn never closes — recognition stalls by **10+ seconds**. Suppress unwanted audio by **zeroing the frame (emit silence)** instead: the stream stays real-time and a false positive costs a few ms of inserted silence, not a stall. General rule: a fixed-rate stream must keep its frame cadence; mutate sample values, never the frame count.

**Normalized ratio detectors explode on low-energy frames — gate with an absolute floor.** A detector like `binPower / totalEnergy` (e.g. a watermark tone's share of frame energy) looks clean until a near-silent frame: the tiny denominator (often a `1e-9` divide-by-zero guard) makes faint noise read as a strong tone, so the gate fires on **silence and room tone**. Combined with the drop-vs-zero trap above, that was the 10 s stall. Fix: require an **absolute level floor** (frame RMS above a tuned threshold) before trusting the ratio — quiet frames have nothing worth gating anyway.

**Bursty TTS needs a jitter buffer with a pre-roll gate.** A streaming synth emits audio **faster than real-time in sentence bursts with generation gaps** between them. A shallow render buffer drains during a gap and the speaker callback inserts silence → choppy, "buzzy" playback. Size the render ring to ride the gaps (~1 s), and **do not start playout until a pre-roll cushion (~200 ms) has built**; if it ever drains, re-arm the pre-roll (one clean pause beats continuous stutter). A barge-in FLUSH clears the ring and re-arms. Watch an `underrun%` metric: starvation during speech is the buzz.

**A push-driven resampler must keep a lookahead margin — do not drain it dry per frame.** Feeding a windowed resampler (NAudio `WdlResamplingSampleProvider`) one frame at a time and then **reading until it returns 0** forces it to emit output past the input its filter has, creating an edge artifact at every frame boundary. The artifacts **image content above Nyquist/2** (for a 24→48 k upsample, energy smears above 12 kHz) — audible as a persistent buzz, even though each frame "looks" fine. Fix: after writing a frame, only pull the output the buffered input fully supports, leaving a small **lookahead margin** of input un-read (verified ~64–128 input samples is plenty); flush the tail only at end-of-utterance. (The resampler may still gently roll off the top passband — that is mild muffling, distinct from the imaging buzz.)

## Debugging audio artifacts offline

You usually cannot reproduce a glitch by ear on demand, and the dashboard/log is gone when the run stops. Capture and analyze instead:

- **Tap the exact PCM at each stage to a float WAV** (raw TTS in, post-resample render out, raw mic) behind a config flag. Then analyze with a small numpy script: band-energy split (spurious energy above the source Nyquist = resampler imaging), exact-zero-run cadence (regular gaps = ring underruns; the gap period maps to the perceived buzz pitch), spectral peaks (a tone near 19 kHz = watermark leak). Generate a spectrogram PNG and **read it back as an image** to eyeball harmonic artifacts.
- **Isolate a DSP bug from the hardware with a deterministic probe.** Build a tiny console that runs the *real* resampler + the *real* driving pattern on a captured WAV and dumps the output — reproduce the artifact with no device, A/B the fix (e.g. drain-dry vs lookahead-margin) on the bench, then port it. This converts a "happens sometimes on the Pi" bug into a unit-test-grade loop.
- **WAV-reader gotchas:** Python stdlib `wave` **rejects IEEE-float (fmt 3)** WAV — parse the RIFF chunks by hand. And a streaming writer patches the `data` size on close, so a clip from a still-running (or killed) process has a **placeholder `0` size**; tolerate it by reading to EOF. Run the analyzer with `uv run --with numpy --with scipy --with matplotlib python ...` so you never manage a venv.

## Discovering an unfamiliar audio/TTS NuGet's API

These libraries are thinly documented and their APIs change between versions, so confirm signatures before writing code rather than guessing.

- **First choice:** the `dotnet-inspect` skill/tool — lists a package's types, members, and signatures.
- **Manual fallback (PowerShell reflection):** build the project first so all dependencies land in `bin/`, then load and enumerate:

```powershell
$dir = "path\to\bin\Debug\net10.0"
# Preload every DLL so dependency types resolve. Do NOT rely on an AssemblyResolve handler:
# Register-ObjectEvent ... AssemblyResolve FAILS with "Events that require a return value are not supported".
Get-ChildItem $dir -Filter *.dll | ForEach-Object { try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch {} }
$asm = [Reflection.Assembly]::LoadFrom("$dir\Target.dll")
$asm.GetExportedTypes() | ForEach-Object { $_.FullName }
```

`GetExportedTypes()` throws if a public signature references an unresolved dependency (e.g. KokoroSharp signatures pull in NAudio.Core) — preloading the whole `bin` dir avoids it. You can even invoke static helpers reflectively to recover hidden values (e.g. a `URL(enum)` method returning hardcoded model download URLs):

```powershell
$m = $asm.GetType("Ns.Type").GetMethod("URL", [Reflection.BindingFlags]'Public,NonPublic,Static')
$m.Invoke($null, @([Enum]::Parse($enumType, "int8")))
```
