---
name: windows-python-cuda
description: >-
  Use when setting up CUDA-accelerated Python libraries (ctranslate2,
  faster-whisper, torch) on Windows. Covers DLL loading gotchas for ctranslate2
  (LoadLibraryA vs AddDllDirectory), supplying CUDA runtime libraries via pip
  without a system CUDA Toolkit, CUDA driver-vs-toolkit version mismatches, and
  PyTorch CUDA wheel Python version caps.
allowed-tools: Read, Bash, Grep, Glob
---

# Windows Python CUDA

Reference for making CUDA-accelerated Python libraries work on Windows without
friction, especially ctranslate2 / faster-whisper.

**How to use this skill:** The mechanism entries (DLL loading, probe pattern) are
stable Windows OS behavior. The version-specific entries are observations from a
point in time -- verify them against the current package changelogs before acting
on them.

---

## ctranslate2 DLL loading: PATH, not AddDllDirectory

ctranslate2's CUDA backend uses `LoadLibraryA`, which searches the process `PATH`.
It does **not** use `LoadLibraryExW` with `LOAD_LIBRARY_SEARCH_USER_DIRS`, so
`os.add_dll_directory()` has no effect on it.

This is a property of how ctranslate2 loads CUDA DLLs internally. Verify it still
holds if ctranslate2 changes its loading strategy, but it is a stable OS-level
distinction unlikely to change.

**Fix:** inject the DLL directories into `os.environ["PATH"]` **before** any
ctranslate2 model loads.

```python
import os, sys, pathlib

def _register_nvidia_dll_paths() -> None:
    """Prepend nvidia pip-package DLL dirs to PATH for ctranslate2 (Windows only)."""
    if sys.platform != "win32":
        return
    added = []
    for site_dir in sys.path:
        nvidia_root = pathlib.Path(site_dir) / "nvidia"
        if not nvidia_root.is_dir():
            continue
        for bin_dir in nvidia_root.glob("*/bin"):
            if bin_dir.is_dir() and any(bin_dir.glob("*.dll")):
                added.append(str(bin_dir))
    if added:
        os.environ["PATH"] = os.pathsep.join(added) + os.pathsep + os.environ.get("PATH", "")
```

Call this before `WhisperModel(...)` or any `ctranslate2` import that triggers GPU init.

---

## Supplying CUDA runtime libraries via pip

The `nvidia-cublas-cuXX` family of pip packages ship CUDA runtime DLLs inside the
venv at `nvidia/<package>/bin/`. This lets you supply the exact CUDA major version
a library needs without installing the system CUDA Toolkit or worrying about what
the system Toolkit version ships.

**How to find the right package:**

1. Check ctranslate2's release notes or `INSTALL.md` for the CUDA major it requires.
   The pattern is: CUDA 12 libraries are `cublas64_12.dll`, CUDA 11 are `cublas64_11.dll`, etc.
2. Install the matching pip package, e.g. `nvidia-cublas-cu12` for CUDA 12.
3. Call `_register_nvidia_dll_paths()` above so ctranslate2 can find them.

DLL naming by CUDA major version (stable pattern -- nvidia has followed this convention
across generations, but verify if a new major departs from it):

| CUDA major | cuBLAS DLL name     |
|-----------|---------------------|
| 11        | `cublas64_11.dll`   |
| 12        | `cublas64_12.dll`   |
| 13        | `cublas64_13.dll`   |

### Why this matters: driver version vs. runtime version

NVML (`pynvml`, `nvidia-smi`, `ctranslate2.get_cuda_device_count()`) queries the
driver, not the toolkit. A GPU can be visible and its device count > 0 while the
required CUDA runtime DLLs are entirely absent. The failure surfaces at the first
encode/decode call, not at model load.

Installing the system CUDA Toolkit at a different major version than the library
expects does not help: each major ships a differently-named DLL. The pip-package
approach decouples the two entirely.

---

## Probing CUDA at startup

Detect missing DLLs during init, before the first real inference call, so you can
fall back to CPU with a clear message instead of crashing mid-session:

```python
def _probe_cuda(model) -> bool:
    """Return True if GPU encoding works; False if DLLs are missing."""
    import numpy as np
    try:
        model.transcribe(np.zeros(16_000, dtype=np.float32), ...)
        return True
    except RuntimeError as e:
        msg = str(e).lower()
        return not any(kw in msg for kw in ("cublas", "cudart", "dll", "cannot be loaded"))
```

The error message keywords to look for (`cublas`, `cudart`, `dll is not found`) are
stable indicators of a missing-library error vs. a model or input error.

---

## PyTorch CUDA wheels and Python version compatibility

**Verify before pinning.** PyTorch CUDA wheels have historically been built only for
a limited range of Python versions. Before starting a project that uses torch with
CUDA:

1. Check the PyTorch install page for which Python versions the current CUDA wheels support.
2. Pin `requires-python` in `pyproject.toml` to that range.
3. Pin `.python-version` to a version in that range so `uv` uses a compatible interpreter.

Failing to do this causes a confusing "no wheel for the current platform" error at
`uv sync` time rather than a clear version error.

### torch + torchaudio must come from the same index

If `torch` comes from the PyTorch CUDA index and `torchaudio` comes from PyPI,
their versions diverge. The result on Windows is typically `OSError: [WinError 127]
The specified procedure could not be found` when `torchaudio._torchaudio.pyd` is
loaded.

Both must come from the same source:

```toml
[tool.uv.sources]
torch      = { index = "pytorch-cuXXX" }
torchaudio = { index = "pytorch-cuXXX" }

[[tool.uv.index]]
name     = "pytorch-cuXXX"
url      = "https://download.pytorch.org/whl/cuXXX"  # check pytorch.org for the current URL
explicit = true
```

Replace `cuXXX` with the current CUDA version tag from pytorch.org.

---

## uv index syntax

```toml
[[tool.uv.index]]          # singular "index", not "indexes"
name     = "my-index"
url      = "https://..."
explicit = true            # only used for packages listed in [tool.uv.sources]
```

Without `explicit = true`, uv consults the custom index for all packages, which
causes unexpected version resolutions.
