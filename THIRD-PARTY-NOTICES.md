# Third-party notices

PhotoStudio is released under the [MIT License](LICENSE). The release builds bundle the
following third-party components, which remain under their own licenses.

| Component | Used for | License |
|---|---|---|
| [.NET runtime and WPF](https://github.com/dotnet/runtime) | Application runtime (self-contained build) | MIT |
| [LibRaw](https://www.libraw.org/) (via `Sdcb.LibRaw.runtime.win64`) | Decoding camera RAW files | CDDL-1.0 (chosen from LGPL-2.1-only OR CDDL-1.0) |
| [Sdcb.LibRaw](https://github.com/sdcb/Sdcb.LibRaw) | .NET wrapper for LibRaw | MIT |
| [Anthropic C# SDK](https://www.nuget.org/packages/Anthropic) | Optional AI editing with Claude | MIT |
| Microsoft.Extensions.AI.Abstractions, System.Text.Json, System.Net.ServerSentEvents | Dependencies of the Anthropic SDK | MIT |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) with DirectML (via `Microsoft.ML.OnnxRuntime.DirectML`, with System.Numerics.Tensors) | Running the face detector and the AI networks offline, on the graphics card or the processor | MIT |
| [DirectML](https://github.com/microsoft/DirectML) (`Microsoft.AI.DirectML`) | Running the AI networks on any DirectX 12 graphics card | MIT |
| [YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet) face detection model (`face_detection_yunet_2023mar.onnx`, © 2020 Shiqi Yu) | Finding the faces for the automatic light tools | MIT |
| [NAFNet](https://github.com/megvii-research/NAFNet) image restoration networks (width 32, SIDD and GoPro weights, © 2022 megvii-model), converted to ONNX with `tools/export_nafnet.py` | AI noise reduction and AI refocus; downloaded on first use, not bundled | MIT |
| [IS-Net / DIS](https://github.com/xuebinqin/DIS) general-use segmentation model (`isnet-general-use.onnx`, © Xuebin Qin), as published by [rembg](https://github.com/danielgatis/rembg) | AI subject masks; downloaded on first use, not bundled | Apache-2.0 |
| [MediaPipe Face Landmarker](https://ai.google.dev/edge/mediapipe/solutions/vision/face_landmarker) face landmarks model (© Google LLC), [ONNX conversion](https://huggingface.co/senty-au/face_landmarks_detector-ONNX), weights unchanged (`face_landmarks_mediapipe.onnx`) | Closed-eyes check of the culling | Apache-2.0 |
| [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) `realesr-general-x4v3` (© 2021 Xintao Wang), converted to ONNX with `tools/export_realesrgan.py` (`realesr-general-x4v3.onnx`) | AI enlargement 2× | BSD-3-Clause (text below) |

LibRaw is used unmodified. Its source code is available at <https://github.com/LibRaw/LibRaw>
and the native build used here at <https://github.com/sdcb/Sdcb.LibRaw>.

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) (Inno Setup License);
Inno Setup itself is not part of the installed application.

## Real-ESRGAN licence

BSD 3-Clause License

Copyright (c) 2021, Xintao Wang
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
