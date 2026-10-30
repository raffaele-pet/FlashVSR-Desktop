# Attribution and third-party components

FlashVSR Desktop is an independent Windows interface for **FlashVSR+**, not an
official OpenImagingLab application. No affiliation or endorsement is implied.

- The inference engine is from [OpenImagingLab/FlashVSR_Plus](https://github.com/OpenImagingLab/FlashVSR_Plus), commit `b548a2cc2c929d3d4676f2973abe27876ec4dee7`. Its original Apache-2.0 license is preserved in `LICENSE`; its original documentation is in `README_ENGINE.md`. The authors and project citation remain there.
- The original FlashVSR+ icon was obtained from `https://flashvsr.tech/projects/flashvsr-plus/icon.svg` and `apple-touch-icon.png`. The desktop ICO is a size/format conversion of that icon. The FlashVSR name and logo belong to their respective owners; they identify the underlying project.
- Pretrained Turbo/TCDecoder weights are downloaded directly from [ruchangyao/FlashVSR_plus](https://huggingface.co/ruchangyao/FlashVSR_plus), pinned revision `46b79520946ac67dec94d1566490690953c7debc`, and verified against their LFS SHA-256 hashes. They are not distributed in the application installer or Git repository. Their upstream terms apply.
- PyTorch, NVIDIA CUDA runtime components, triton-windows, uv, FFmpeg (via imageio-ffmpeg), OpenCV, Pillow and other Python dependencies retain their own licenses. The installer obtains them from their official package indexes/releases. See the installed package metadata and the upstream projects for their terms.
- The desktop application and bridge code are distributed under the repository's Apache-2.0 license. The application is not code-signed; Windows may show the standard unsigned-application prompt.

The Desktop bridge stretches the complete input into the model canvas, then
resizes complete decoded frames to the exact selected aspect ratio before H.264
encoding. These geometry changes happen outside the released model. The DiT,
RoPE, attention and checkpoint arithmetic are unchanged.
