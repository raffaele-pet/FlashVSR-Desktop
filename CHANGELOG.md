# Changelog

## 1.0.1 — 2026-11-06

- Preserve every encoded video frame when copying source audio. The previous
  `-shortest` stream-copy option could discard the final video packets when
  the audio ended a few milliseconds earlier than the video (for example,
  106 output frames from a 107-frame input).
- Keep strict final frame-count validation. This fix does not accept missing
  frames or duplicate the last frame to conceal an incomplete result.
- Add real FFmpeg regression tests for shorter AAC audio, fractional frame
  rates, separate encoded video/source audio, and inputs without audio.

## 1.0.0 — 2026-10-30

- Initial Windows desktop app, guided installer, Turbo/Triton integration,
  drag and drop, output selection, and 16 complete-frame aspect/quality presets.
