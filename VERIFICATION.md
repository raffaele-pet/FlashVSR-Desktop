# Verification — 30 October 2026

## Repository launchers

- 19 Windows PowerShell checks passed for the download UI compilation,
  default/custom installation discovery, incomplete/corrupt installations,
  checksum rejection, release URL/version validation and both actual BAT
  entry points. BAT dispatch was exercised from an unrelated working
  directory with spaces, an accented character and `&`, `!`, `%` in the
  extracted repository folder name, using a native argument recorder instead
  of installing or starting an application.
- The real latest-release download was exercised separately with launching
  disabled: the public EXE was downloaded, verified with the published
  SHA-256, cached, and the installed app was successfully resolved.

## Patch 1.0.1 — 6 November 2026

- Reproduced the audio-copy truncation on a real 107-frame / 24 fps input:
  its AAC audio ended at 4.450000 s while its video ended at 4.458333 s.
- After removing `-shortest`, the same input retained all 107 decoded video
  frames through audio/video stream copy. Strict worker validation remains.
- Four new real-FFmpeg regression tests passed, covering shorter audio,
  fractional FPS, separate input/encoded tracks and video-only input.
- All eight Python tests (audio and geometry) passed.

## Initial release

Verified on Windows x64 with an NVIDIA RTX 4070 Ti (12 GB), Python 3.10.20,
PyTorch 2.7.1+cu128 and triton-windows 3.3.1.post21.

- Published a self-contained Windows executable with .NET SDK 8.0.427.
- Ran the real installer service into a fresh application directory: isolated
  Python, fresh virtual environment, PyTorch/CUDA and runtime dependencies,
  editable engine/operator installation, dependency consistency check,
  model SHA-256 verification/reuse, actual CUDA/Triton numerical test,
  desktop/Start-menu/repair shortcuts and final installation manifest. Exit 0.
- Existing model files and cached dependency wheels were reused during the
  local installation test. The large-file HTTP downloader's resume and hash
  path was separately exercised against a real local HTTP Range server.
- 19 C# checks passed: all 16 preset geometries, resumed/checksummed transfer,
  cancellation of a transfer and cancellation of a subprocess tree.
- Four Python geometry tests passed: all presets, distinct border preservation
  through both resizes, the actual input reader and the actual encoded MP4
  output writer (including restored dimensions/frame count).
- Actual high-quality GPU inference: model canvas 2560×1408, final output
  2560×1440, all five source frames independently decoded and verified.
- Actual GUI inference: imported a video with spaces/accented characters in
  its path, selected 9:16 Standard, completed all nine source frames, decoded
  1152×2048 output at 24 fps and verified the source AAC audio was retained.
  No `max-frames` limit is set by the GUI.
- GUI cancellation was tested: the child worker was terminated, no partial
  final output was produced and the UI returned to its ready state.
- Inspected native installer/main windows, video preview, format menu,
  quality warning, geometry labels and start/cancel/completion states.
- GitHub Actions also builds/tests the source on a separate Windows runner.

The missing-GPU/driver, UAC and reboot branches are implemented but were not
executed against this PC's working graphics driver. No claim of validation
on every NVIDIA model is made. GPU inference was tested on the formats above;
all eight aspect ratios and both levels were verified by geometry/encoding
tests. The model remains CUDA-only and the installer cannot supply hardware.
