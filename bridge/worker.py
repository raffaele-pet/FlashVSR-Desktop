"""Isolated desktop worker. JSON events on stdout; diagnostics on stderr.

The original pipeline center-crops. This adapter replaces its file-frame
resizer and incremental writer *in this worker process only*: the full frame
is stretched to the model canvas, then restored to the selected exact ratio
before encoding. The DiT and attention/model arithmetic are unchanged.
"""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import traceback

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
os.environ.setdefault("PYTHONUTF8", "1")
os.environ.setdefault("TRITON_CACHE_DIR", str(ROOT / ".cache" / "triton"))


def emit(kind, **values):
    print(json.dumps({"event": kind, **values}, ensure_ascii=False), flush=True)


def presets():
    return json.loads(Path(__file__).with_name("presets.json").read_text(encoding="utf-8-sig"))


def geometry(aspect, quality):
    return next(p for p in presets() if p["id"] == aspect)[quality]


def native_geometry(width, height, quality):
    if width <= 0 or height <= 0:
        raise ValueError("Invalid video dimensions.")
    for preset in presets():
        a, b = map(int, preset["id"].split(":"))
        if width * b == height * a:
            return preset[quality].copy()
    budget = 2560 * 1408 if quality == "high" else 2048 * 1152
    scale = math.sqrt(budget / width / height)
    divisor = math.gcd(width, height)
    a, b = width // divisor, height // divisor
    step = 2 if a % 2 or b % 2 else 1
    multiple = max(step, round(divisor * scale / step) * step)
    return dict(width=a * multiple, height=b * multiple,
                nativeWidth=max(128, round(width * scale / 128) * 128),
                nativeHeight=max(128, round(height * scale / 128) * 128))


def prepare_preview(source, destination):
    """A local H.264 playback copy; the source and inference input are untouched."""
    import imageio_ffmpeg
    target = Path(destination)
    if Path(source).resolve() == target.resolve():
        raise ValueError("Preview must not replace the original video.")
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_suffix(".partial.mp4")
    try:
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error",
                        "-y", "-i", str(source), "-map", "0:v:0", "-map", "0:a:0?",
                        "-vf", "scale=ceil(iw/2)*2:ceil(ih/2)*2,setsar=1,setpts=PTS-STARTPTS",
                        "-c:v", "libx264", "-preset", "veryfast", "-crf", "12",
                        "-pix_fmt", "yuv420p", "-r", str(probe(source)["fps"]), "-fps_mode", "cfr",
                        "-c:a", "aac", "-af", "asetpts=PTS-STARTPTS",
                        "-movflags", "+faststart", str(partial)], check=True)
        os.replace(partial, target)
    finally:
        partial.unlink(missing_ok=True)


def prepare_comparison(source, result, destination):
    """Pack corresponding frames into one playback stream to prevent decoder drift.

    Only the local preview is packed/scaled. The saved upscale stays untouched.
    """
    import imageio_ffmpeg
    info = probe(result)
    scale = min(1, 1920 / max(info['width'], info['height']))
    w = max(2, round(info['width'] * scale / 2) * 2)
    h = max(2, round(info['height'] * scale / 2) * 2)
    target = Path(destination)
    if target.resolve() in (Path(source).resolve(), Path(result).resolve()):
        raise ValueError("Comparison preview must not replace an input video.")
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_suffix('.partial.mp4')
    filters = (f'[0:v]scale={w}:{h}:flags=bicubic,setsar=1,setpts=N/({info["fps"]}*TB)[before];'
               f'[1:v]scale={w}:{h}:flags=bicubic,setsar=1,setpts=N/({info["fps"]}*TB)[after];'
               '[before][after]hstack=inputs=2:shortest=1[video]')
    try:
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-v','error','-y','-i',str(source),'-i',str(result),
                        '-filter_complex',filters,'-map','[video]','-map','0:a:0?',
                        '-c:v','libx264','-preset','veryfast','-crf','12','-pix_fmt','yuv420p',
                        '-r',str(info['fps']),'-fps_mode','cfr','-c:a','aac',
                        '-af','asetpts=PTS-STARTPTS','-movflags','+faststart',str(partial)],check=True)
        actual = probe(partial)
        if actual['frames'] != info['frames']:
            raise RuntimeError('Comparison frame count does not match the saved video.')
        os.replace(partial,target)
    finally:
        partial.unlink(missing_ok=True)


def stretch_frames(frames, target_height, target_width, **_):
    import numpy as np
    from PIL import Image
    return np.ascontiguousarray(np.stack([
        np.asarray(Image.fromarray(frame).resize(
            (target_width, target_height), Image.Resampling.BICUBIC
        )) for frame in frames
    ]))


def enable_full_frame_geometry(width, height):
    import cv2
    import inference.pipeline as pipeline
    import inference.preprocessing.input as inputs
    from inference.output import IncrementalVideoWriter, tensor_to_uint8_frame
    inputs.pil_center_crop_resize = stretch_frames

    class FullFrameWriter(IncrementalVideoWriter):
        def append(self, video, *, value_range="neg_one_to_one"):
            if video.ndim != 5 or video.shape[0] != 1:
                raise ValueError("Expected one video [1,C,F,H,W]")
            for tensor in video[0].permute(1, 0, 2, 3):
                frame = tensor_to_uint8_frame(tensor, value_range=value_range)
                if frame.shape[:2] != (height, width):
                    frame = cv2.resize(frame, (width, height), interpolation=cv2.INTER_CUBIC)
                if self.first_frame is None:
                    self.first_frame = frame
                self.writer.append_data(frame)
                self.frame_count += 1
    pipeline.IncrementalVideoWriter = FullFrameWriter


def probe(path, thumbnail=None):
    import cv2
    capture = cv2.VideoCapture(str(path))
    try:
        if not capture.isOpened():
            raise ValueError("Unable to open this video or unsupported format.")
        ok, frame = capture.read()
        if not ok:
            raise ValueError("This video has no readable frames.")
        fps = float(capture.get(cv2.CAP_PROP_FPS) or 24)
        count = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
        h, w = frame.shape[:2]
        if thumbnail:
            ratio = min(1000 / w, 700 / h, 1)
            thumb = cv2.resize(frame, (max(1, round(w * ratio)), max(1, round(h * ratio))))
            # imencode + Path.write_bytes also handles non-ASCII Windows paths.
            success, data = cv2.imencode(".jpg", thumb)
            if success:
                Path(thumbnail).write_bytes(data.tobytes())
        return dict(width=w, height=h, fps=fps, frames=count,
                    duration=count / fps if count else 0)
    finally:
        capture.release()


def doctor():
    import torch
    import triton
    from flashvsr_attn import stream_block_sparse_attention
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA unavailable. Install or update the NVIDIA driver and retry.")
    p = torch.cuda.get_device_properties(0)
    if p.major < 8 or not torch.cuda.is_bf16_supported():
        raise RuntimeError("This GPU does not support BF16. NVIDIA Ampere or newer is required.")
    with torch.inference_mode():
        q = torch.randn(1, 1, 1, 128, 128, device="cuda", dtype=torch.bfloat16)
        mask = torch.ones(1, 1, 1, 1, dtype=torch.bool, device="cuda")
        actual = stream_block_sparse_attention(q, q, q, mask)
        ref = torch.nn.functional.scaled_dot_product_attention(
            q.flatten(2, 3), q.flatten(2, 3), q.flatten(2, 3)
        ).reshape_as(q)
        torch.cuda.synchronize()
        torch.testing.assert_close(actual, ref, atol=0.02, rtol=0.02)
    emit("doctor", gpu=p.name, memoryGiB=p.total_memory / 1024**3,
         torch=torch.__version__, cuda=torch.version.cuda, triton=triton.__version__)


def run(args):
    import torch
    from inference import InferenceRequest, build_inference_pipeline
    from inference.presets import model_paths_for_mode, stream_config_for_mode
    info = probe(args.input)
    plan = native_geometry(info["width"], info["height"], args.quality) if args.aspect == "native" else geometry(args.aspect, args.quality)
    destination = Path(args.output).expanduser().resolve()
    source = Path(args.input).expanduser().resolve()
    if source == destination:
        raise ValueError("The output file must differ from the original video.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA unavailable. Check the NVIDIA driver.")
    enable_full_frame_geometry(plan["width"], plan["height"])
    torch.cuda.set_device(0)
    emit("status", message="Loading Turbo model…", progress=2)
    config = stream_config_for_mode(
        "turbo", device="cuda:0", decode_every=8,
        tiny_decoder_mode="serial", cache_offload_mode="always",
        attn_kernel="triton_block_sparse", hardware_profile="auto"
    )
    model = build_inference_pipeline(config, model_paths_for_mode(ROOT, "turbo"))
    count = 0
    total = min(info["frames"], args.max_frames) if args.max_frames else info["frames"]
    emit("status", message="Upscaling…", progress=6, **plan)

    def progress(message):
        nonlocal count
        match = re.search(r"(\d+) output frames", message)
        if match:
            count += int(match.group(1))
            emit("progress", frames=min(count, total) if total else count, total=total,
                 progress=min(94, 6 + 88 * count / max(total, 1)),
                 message="Upscaling…")
        else:
            emit("log", message=message)

    job = Path(args.work).resolve() if args.work else Path(tempfile.mkdtemp(prefix="flashvsr-"))
    job.mkdir(parents=True, exist_ok=True)
    scratch = job / "upscaled.mp4"
    model.infer(InferenceRequest(
        input_path=str(source), output_path=str(scratch),
        height=plan["nativeHeight"], width=plan["nativeWidth"],
        max_frames=args.max_frames,
    ), progress=progress, video_preset="veryfast")
    emit("status", message="Verifying video and audio…", progress=96)
    result = probe(scratch)
    if (result["width"], result["height"]) != (plan["width"], plan["height"]):
        raise RuntimeError("Output dimensions do not match the requested format.")
    if total and result["frames"] != total:
        raise RuntimeError(f"Incomplete video: {result['frames']} of {total} frames.")
    # Copy first, then replace atomically in the destination directory, even
    # if the work folder and destination live on different volumes.
    pending = destination.with_name(f".{destination.name}.{os.getpid()}.tmp")
    try:
        shutil.copyfile(scratch, pending)
        os.replace(pending, destination)
    finally:
        pending.unlink(missing_ok=True)
    emit("complete", output=str(destination), progress=100, **result)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--doctor", action="store_true")
    parser.add_argument("--probe")
    parser.add_argument("--thumbnail")
    parser.add_argument("--input")
    parser.add_argument("--output")
    parser.add_argument("--aspect", default="native", choices=["native"] + [p["id"] for p in presets()])
    parser.add_argument("--preview")
    parser.add_argument("--compare")
    parser.add_argument("--result")
    parser.add_argument("--quality", choices=["standard", "high"], default="standard")
    parser.add_argument("--work")
    parser.add_argument("--max-frames", type=int)
    args = parser.parse_args()
    if args.doctor:
        doctor()
    elif args.compare:
        if not args.result or not args.output:
            parser.error("--result and --output are required with --compare")
        prepare_comparison(args.compare, args.result, args.output)
        emit("preview", output=args.output)
    elif args.preview:
        if not args.output:
            parser.error("--output is required with --preview")
        prepare_preview(args.preview, args.output)
        emit("preview", output=args.output)
    elif args.probe:
        emit("probe", **probe(args.probe, args.thumbnail))
    else:
        if not args.input or not args.output:
            parser.error("--input and --output are required")
        run(args)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        message = str(error)
        if "out of memory" in message.lower():
            message = "GPU memory exhausted. Close other GPU applications or choose Standard."
        emit("error", message=message)
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
