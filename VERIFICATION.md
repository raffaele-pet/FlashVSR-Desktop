# Verification

## Release 1.1.1 — 7 November 2026

- Split output selection into a single-line Name and a single-line Save to
  folder, prefilled with the default Videos/FlashVSR directory. Generated
  names follow source/resolution; custom names remain independent of quality.
- 44 service checks passed, including blank defaults, MP4 extension handling,
  invalid/path-traversal/device names and original-file overwrite prevention.
- 23 real WPF player checks passed, including the central hand-cursor chooser,
  timeline clicks not requesting a file, and inward/outward full-screen icons.
- Both resolution buttons use the same height; their top edge shares the
  preview's header-row offset. The redundant Open video action was removed.
- Renamed repository entry points to Install.bat and Launch.bat; all 19
  Windows PowerShell launcher checks passed with the English filenames.
- Reviewed every published release asset: only its versioned EXE and 100-byte
  checksum were present. The checksum is required by the verified BAT download;
  no unnecessary uploaded assets were found.

## Release 1.1.0 — 7 November 2026

- Rebuilt the application and guided installer with a shared neutral dark
  theme, compact horizontal branding and English controls/status/errors.
- Removed aspect selection. Exact native ratios now select canonical presets
  or a native-ratio geometry for arbitrary dimensions; only the model canvas
  is rounded to multiples of 128. Standard and High retain their pixel budgets.
- 29 C# service checks, 19 Windows PowerShell launcher checks and all 10
  Python regression tests passed. Tests cover arbitrary native ratios, all
  canonical presets, frame borders, shorter audio, complete frame counts,
  playback copies and packed comparison frame correspondence.
- 18 integration checks passed using real WPF media decoding on Windows:
  both views share one decoded frame/clock, correctly clipped source regions,
  mode changes, divider drag, paused seeking, pause/resume, full screen and
  return, end/replay, clear and cancellation of pending media initialization.
  These media checks require a Windows desktop with Media Foundation; CI
  builds the harness while local checks exercise playback.
- The self-contained Setup upgraded the existing local installation and
  passed the real NVIDIA BF16/CUDA/Triton verification.
- A complete 107-frame / 24 fps source at 1376×768 was processed through
  the installed GUI. All 107 frames were verified at 2064×1152, preserving
  its native 43:24 ratio. The synchronized comparison opened automatically.
- Application, setup, actual video playback, comparison and full-screen
  rendering were visually checked. Playback previews are local disposable
  copies; saved output quality and original input are unaffected.
- No separate repository download archive is distributed. Installation is
  documented through Releases or the repository's two BAT entry points.

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
