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
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) (via `Microsoft.ML.OnnxRuntime`, with System.Numerics.Tensors) | Running the face detector on the processor, offline | MIT |
| [YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet) face detection model (`face_detection_yunet_2023mar.onnx`, © 2020 Shiqi Yu) | Finding the faces for the automatic light tools | MIT |

LibRaw is used unmodified. Its source code is available at <https://github.com/LibRaw/LibRaw>
and the native build used here at <https://github.com/sdcb/Sdcb.LibRaw>.

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) (Inno Setup License);
Inno Setup itself is not part of the installed application.
