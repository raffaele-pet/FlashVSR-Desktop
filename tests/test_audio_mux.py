"""Real FFmpeg regression tests: audio must never truncate video frames."""
from pathlib import Path
import subprocess
import tempfile
import unittest
import sys

import cv2
import imageio_ffmpeg

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inference.output import mux_source_audio


class AudioMuxTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.root = Path(self.folder.name)
        self.ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()

    def tearDown(self):
        self.folder.cleanup()

    def encode(self, name, *, rate="24", audio_duration=None):
        output = self.root / name
        command = [self.ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                   "-f", "lavfi", "-i", f"testsrc2=size=128x72:rate={rate}"]
        if audio_duration is not None:
            command += ["-f", "lavfi", "-t", str(audio_duration), "-i",
                        "sine=frequency=440:sample_rate=32000"]
        command += ["-map", "0:v:0"]
        if audio_duration is not None:
            command += ["-map", "1:a:0", "-c:a", "aac"]
        command += ["-frames:v", "107", "-c:v", "libx264", "-bf", "3",
                    "-pix_fmt", "yuv420p", str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        return output

    def decoded_frames(self, path):
        video = cv2.VideoCapture(str(path))
        count = 0
        try:
            self.assertTrue(video.isOpened())
            while True:
                ok, frame = video.read()
                if not ok:
                    break
                self.assertEqual(frame.shape[:2], (72, 128))
                count += 1
            return count
        finally:
            video.release()

    def assert_audio(self, path):
        # Decode the actual output audio, not just its container metadata.
        result = subprocess.run([self.ffmpeg, "-v", "error", "-i", str(path),
                                 "-map", "0:a:0", "-f", "s16le", "-"],
                                capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertGreater(len(result.stdout), 0)

    def test_audio_ending_eight_ms_early_keeps_all_107_frames(self):
        source = self.encode("audio-shorter.mp4", audio_duration=4.45)
        self.assertEqual(self.decoded_frames(source), 107)
        output = self.root / "copied.mp4"
        mux_source_audio(source, source, output)
        self.assertEqual(self.decoded_frames(output), 107)
        self.assert_audio(output)

    def test_separate_upscaled_video_and_short_audio_keep_all_frames(self):
        source = self.encode("source.mp4", audio_duration=4.0)
        upscaled = self.encode("encoded.mp4")
        output = self.root / "result.mp4"
        mux_source_audio(upscaled, source, output)
        self.assertEqual(self.decoded_frames(output), 107)
        self.assert_audio(output)

    def test_fractional_frame_rate_and_short_audio_keep_all_frames(self):
        source = self.encode("fractional.mp4", rate="30000/1001", audio_duration=3.55)
        output = self.root / "result.mp4"
        mux_source_audio(source, source, output)
        self.assertEqual(self.decoded_frames(output), 107)
        self.assert_audio(output)

    def test_source_without_audio_keeps_all_frames(self):
        source = self.encode("silent.mp4")
        output = self.root / "result.mp4"
        mux_source_audio(source, source, output)
        self.assertEqual(self.decoded_frames(output), 107)


if __name__ == "__main__":
    unittest.main()
