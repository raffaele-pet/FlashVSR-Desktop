"""Isolated desktop worker. JSON events on stdout; diagnostics on stderr.

The original pipeline center-crops. This adapter replaces its file-frame
resizer and incremental writer *in this worker process only*: the full frame
is stretched to the model canvas, then restored to the selected exact ratio
before encoding. The DiT and attention/model arithmetic are unchanged.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
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
            raise ValueError("Il video non può essere aperto o il formato non è supportato.")
        ok, frame = capture.read()
        if not ok:
            raise ValueError("Il video non contiene fotogrammi leggibili.")
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
        raise RuntimeError("CUDA non disponibile. Installa o aggiorna il driver NVIDIA e riprova.")
    p = torch.cuda.get_device_properties(0)
    if p.major < 8 or not torch.cuda.is_bf16_supported():
        raise RuntimeError("Questa GPU non supporta BF16: serve una NVIDIA Ampere o successiva.")
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
    plan = geometry(args.aspect, args.quality)
    destination = Path(args.output).expanduser().resolve()
    source = Path(args.input).expanduser().resolve()
    if source == destination:
        raise ValueError("Il file di uscita deve essere diverso dal video originale.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA non disponibile. Verifica il driver NVIDIA.")
    enable_full_frame_geometry(plan["width"], plan["height"])
    torch.cuda.set_device(0)
    emit("status", message="Caricamento del modello Turbo…", progress=2)
    config = stream_config_for_mode(
        "turbo", device="cuda:0", decode_every=8,
        tiny_decoder_mode="serial", cache_offload_mode="always",
        attn_kernel="triton_block_sparse", hardware_profile="auto"
    )
    model = build_inference_pipeline(config, model_paths_for_mode(ROOT, "turbo"))
    count = 0
    total = min(info["frames"], args.max_frames) if args.max_frames else info["frames"]
    emit("status", message="Upscaling in corso…", progress=6, **plan)

    def progress(message):
        nonlocal count
        match = re.search(r"(\d+) output frames", message)
        if match:
            count += int(match.group(1))
            emit("progress", frames=min(count, total) if total else count, total=total,
                 progress=min(94, 6 + 88 * count / max(total, 1)),
                 message="Ricostruzione dei dettagli…")
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
    emit("status", message="Verifica del video e dell’audio…", progress=96)
    result = probe(scratch)
    if (result["width"], result["height"]) != (plan["width"], plan["height"]):
        raise RuntimeError("Le dimensioni del video non corrispondono al formato scelto.")
    if total and result["frames"] != total:
        raise RuntimeError(f"Video incompleto: {result['frames']} di {total} fotogrammi.")
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
    parser.add_argument("--aspect", default="16:9", choices=[p["id"] for p in presets()])
    parser.add_argument("--quality", choices=["standard", "high"], default="standard")
    parser.add_argument("--work")
    parser.add_argument("--max-frames", type=int)
    args = parser.parse_args()
    if args.doctor:
        doctor()
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
            message = "Memoria GPU esaurita. Chiudi le altre applicazioni GPU o scegli Standard."
        emit("error", message=message)
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
