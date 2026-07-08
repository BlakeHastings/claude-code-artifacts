---
name: python-audio-vad
description: >-
  Use when building voice activity detection, speech endpointing, or
  continuous speech-to-text pipelines in Python. Covers Silero VAD (ONNX
  interface, bypassing the Python wrapper), sounddevice for Windows audio
  capture, the IDLE/SPEAKING/TRAILING endpointing state machine with pre-roll
  and hangover, and faster-whisper as the STT backend.
allowed-tools: Read, Bash, Grep, Glob
---

# Python Audio and VAD Pipeline

Reference for building continuous voice listening pipelines:
microphone -> VAD -> endpointing -> STT -> handler.

**How to use this skill:** The state machine logic and sounddevice patterns are
stable technique. The Silero VAD ONNX interface and the silero-vad wrapper import
behavior are observations tied to a specific package version -- verify them against
the installed version before assuming they still hold.

---

## Silero VAD: check whether to use ONNX directly or the Python wrapper

The `silero-vad` pip package ships a bundled ONNX model and a Python wrapper. The
wrapper is more convenient, but at the time we built this it imported `torchaudio`
unconditionally at module level, even when you only wanted the ONNX model. On
Windows, if `torch` and `torchaudio` are from mismatched sources, this causes
`OSError: [WinError 127] The specified procedure could not be found` on import.

**Before reaching for the ONNX path:** check the release notes for the installed
version of `silero-vad`. If the wrapper no longer has this import, use the wrapper
-- it handles edge cases and resets more ergonomically. If the wrapper still
triggers the error, load the ONNX model directly.

### Finding the ONNX file

```python
import importlib.metadata, pathlib

def _find_onnx_model() -> pathlib.Path:
    files = importlib.metadata.files("silero-vad")
    if files is None:
        raise RuntimeError("silero-vad not installed")
    matches = [f for f in files if f.name == "silero_vad.onnx"]
    if not matches:
        raise RuntimeError("silero_vad.onnx not found -- check package layout for this version")
    return matches[0].locate()
```

The file was at `silero_vad/data/silero_vad.onnx` inside the package, but verify
the path if the package restructures its layout.

### ONNX model I/O

Verified for silero-vad 5.x. Verify against the installed version if these shapes
produce an ONNX shape mismatch error.

| Name     | Shape         | Dtype   | Notes |
|----------|---------------|---------|-------|
| `input`  | `(1, 512)`    | float32 | exactly 512 samples at 16 kHz |
| `state`  | `(2, 1, 128)` | float32 | LSTM state; zeros at session start |
| `sr`     | scalar        | int64   | sample rate (16000) |
| `output` | `(1, 1)`      | float32 | speech probability 0-1 |
| `stateN` | `(2, 1, 128)` | float32 | carry forward as next `state` |

**Chunk size is 512 samples at 16 kHz (32 ms).** Other sizes are rejected by the
streaming ONNX interface.

### Minimal streaming implementation

```python
import numpy as np
import onnxruntime as ort

_SAMPLE_RATE = np.array(16_000, dtype=np.int64)

class SileroVAD:
    def __init__(self) -> None:
        model_path = _find_onnx_model()
        self._session = ort.InferenceSession(
            str(model_path), providers=["CPUExecutionProvider"]
        )
        self._state = np.zeros((2, 1, 128), dtype=np.float32)

    def probability(self, chunk: np.ndarray) -> float:
        """Return speech probability for a 512-sample 16kHz chunk."""
        audio = chunk.reshape(1, -1).astype(np.float32)
        output, new_state = self._session.run(
            ["output", "stateN"],
            {"input": audio, "state": self._state, "sr": _SAMPLE_RATE},
        )
        self._state = new_state
        return float(output[0, 0])

    def reset(self) -> None:
        self._state = np.zeros((2, 1, 128), dtype=np.float32)
```

---

## Speech endpointing: IDLE / SPEAKING / TRAILING

A three-state machine turns per-frame VAD probabilities into complete speech
segments ready for transcription. The logic here is stable technique independent
of model or library versions.

Key parameters:

| Parameter | Typical value | Role |
|-----------|--------------|------|
| `onset_threshold` | 0.50 | prob to start a segment |
| `offset_threshold` | 0.35 | prob below which TRAILING begins |
| `pre_roll_ms` | 200-250 ms | silence buffered before onset to avoid clipping |
| `hangover_ms` | 500-700 ms | silence tolerated in TRAILING before emitting |
| `min_speech_ms` | 200-300 ms | minimum speech frames to accept a segment |

### State transition rules

```
IDLE     -- prob >= onset  --> SPEAKING  (flush pre-roll + current frame into segment)
IDLE     -- prob < onset   --> IDLE      (advance pre-roll ring buffer)

SPEAKING -- prob >= offset --> SPEAKING  (extend segment, reset silence counter, track _speech_frames)
SPEAKING -- prob < offset  --> TRAILING  (extend segment, start silence counter)

TRAILING -- prob >= onset  --> SPEAKING  (interrupt: restart counting, extend segment)
TRAILING -- silence_count >= hangover_frames --> IDLE  (emit segment)
```

### Critical implementation notes

**Pre-roll on onset:** use `list(self._pre_roll) + [current_frame]`, not
`self._advance_pre_roll(audio)` then append. Advancing first evicts the onset
frame from the ring buffer before it is captured.

**Track speech frames separately from segment length:** pre-roll silence and
trailing hangover silence are part of the segment but must not count toward
`min_speech_ms`. Keep a `_speech_frames` counter that increments only when
`prob >= onset_threshold` in the SPEAKING state. Filter on that in `_emit()`:

```python
def _emit(self):
    segment = self._segment
    speech_frames = self._speech_frames
    self._reset()
    if speech_frames < self._min_speech_frames:
        return None  # discard coughs, clicks, brief noise
    return np.concatenate(segment)
```

---

## sounddevice: Windows audio capture at 16 kHz

```python
import sounddevice as sd
import numpy as np

SAMPLE_RATE = 16_000
CHUNK_SAMPLES = 512

def _audio_callback(indata, frames, time, status):
    chunk = indata[:, 0].copy()  # take first channel from (N,1) array
    queue.put(chunk)

stream = sd.InputStream(
    samplerate=SAMPLE_RATE,
    channels=1,
    dtype="float32",
    blocksize=CHUNK_SAMPLES,
    callback=_audio_callback,
    device=device_index,  # None = system default
)
```

Print the actual device name after opening (the "default" alias resolves):

```python
with stream:
    info = sd.query_devices(stream.device, "input")
    print(f"[audio] using: {info['name']}")
```

List devices:

```python
sd.query_devices()  # returns all; filter by max_input_channels > 0
```

---

## faster-whisper as STT backend

- Use `compute_type="float16"` for CUDA, `"int8"` for CPU
- Set `vad_filter=False` when supplying clean speech segments from your own
  endpointer; the built-in filter adds latency and redundant VAD computation
- Run a silent 1-second probe through the encoder at startup to catch missing CUDA
  DLLs before the first real utterance (see `windows-python-cuda` skill)

```python
from faster_whisper import WhisperModel
import numpy as np

model = WhisperModel("base", device="cuda", compute_type="float16")
segments, _ = model.transcribe(audio_np, language="en", beam_size=1, vad_filter=False)
text = " ".join(seg.text.strip() for seg in segments).strip()
```

---

## Pipeline architecture

```
sounddevice callback (512 samples @ 16kHz)
  -> queue (thread boundary)
  -> SileroVAD.probability(chunk)
  -> SpeechEndpointer.process(audio, prob)
     -> None (mid-segment) | np.ndarray (complete segment)
  -> WhisperTranscriber.transcribe(segment)
  -> on_speech(text) handler
```

Keep VAD and endpointing on the audio thread or a single processing thread.
Transcription is blocking; run it in the same thread after the segment emits
rather than spawning threads per segment (avoids out-of-order results).
