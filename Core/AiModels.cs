using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;

namespace PhotoStudio.Core
{
    /// <summary>
    /// The image restoration networks (NAFNet, MIT licence) used by the AI noise reduction and refocus. They are too
    /// large to ship inside the application, so each one is downloaded once, on first use, from the PhotoStudio
    /// releases on GitHub, checked against its SHA-256 and kept in %LOCALAPPDATA%\PhotoStudio\Models.
    /// They run on the graphics card when DirectML can use it, otherwise on the processor: nothing is sent anywhere.
    /// </summary>
    public static class AiModels
    {
        public sealed class Model
        {
            public string File { get; init; }
            public string Sha256 { get; init; }
            public long Size { get; init; }
            /// <summary>Set for the small networks embedded in the application: they are always installed.</summary>
            public string Resource { get; init; }
            /// <summary>False for a network so small that the processor runs it faster than the graphics card.</summary>
            public bool Gpu { get; init; } = true;
            public string Url => BaseUrl + File;
            public string Path => System.IO.Path.Combine(Folder, File);
            public bool Installed => Resource != null || System.IO.File.Exists(Path);
        }

        const string BaseUrl = "https://github.com/Lucosh/PhotoStudio/releases/download/models-1/";

        /// <summary>NAFNet width 32 trained on SIDD (real smartphone and camera noise).</summary>
        public static readonly Model Denoise = new Model
        {
            File = "nafnet-sidd32.onnx", Size = 117090831,
            Sha256 = "5545882502bded5ccd8031bd7bcf6926090ea2d7c445e28b69c8863ef18981c4",
        };

        /// <summary>NAFNet width 32 trained on GoPro (blur from camera shake and missed focus).</summary>
        public static readonly Model Deblur = new Model
        {
            File = "nafnet-gopro32.onnx", Size = 68898598,
            Sha256 = "3c205a807efe00fda45b9d382070ccfcb1f4cec195629a288ba7cc887bf37b99",
        };

        /// <summary>IS-Net (DIS, Apache 2.0) general use: the main subject of a photo, for the AI subject masks.</summary>
        public static readonly Model Subject = new Model
        {
            File = "isnet-general-use.onnx", Size = 178648008,
            Sha256 = "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a",
        };

        /// <summary>MediaPipe face landmarks (Apache 2.0), embedded: the eyelids, for the closed-eyes check of the culling.</summary>
        public static readonly Model FaceLandmarks = new Model
        {
            File = "face_landmarks_mediapipe.onnx", Resource = "PhotoStudio.Assets.FaceLandmarks.onnx", Gpu = false,
            Sha256 = "7d6e82dee82a1dca5fbddb282b3cc74571833a530de317fc22ae325c3358beeb",
        };

        /// <summary>Real-ESRGAN realesr-general-x4v3 (BSD-3), embedded: the AI enlargement.</summary>
        public static readonly Model Upscaler = new Model
        {
            File = "realesr-general-x4v3.onnx", Resource = "PhotoStudio.Assets.Upscaler.onnx",
            Sha256 = "ba3e0db279bdc225aff61f0dc031c5111450dd47d6c31a5813e02f928f8f6169",
        };

        /// <summary>
        /// LaMa (big-lama, Apache 2.0) exported to ONNX by Carve: rebuilds the background behind removed objects.
        /// It runs on the processor: DirectML does not support its Fourier layers.
        /// </summary>
        public static readonly Model Inpaint = new Model
        {
            File = "lama_fp32.onnx", Size = 208044816, Gpu = false,
            Sha256 = "1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6",
        };

        /// <summary>The networks that are downloaded on first use (the others are inside the application).</summary>
        public static readonly Model[] Downloadable = { Denoise, Deblur, Subject, Inpaint };

        public static string Folder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoStudio", "Models");

        /// <summary>Downloads a model; progress goes from 0 to 1. Throws when the download fails or is corrupt.</summary>
        public static async Task DownloadAsync(Model m, IProgress<double> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Folder);
            string part = m.Path + ".part";
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoStudio");
                using var response = await http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? m.Size;
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(Math.Min(1, done / (double)total));
                }
            }
            string hash;
            await using (var f = System.IO.File.OpenRead(part))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
            if (hash != m.Sha256)
            {
                System.IO.File.Delete(part);
                throw new InvalidDataException(Loc.T("Il file scaricato è danneggiato (controllo SHA-256 non superato)."));
            }
            System.IO.File.Move(part, m.Path, true);
        }

        static readonly Dictionary<Model, Runner> Runners = new Dictionary<Model, Runner>();

        /// <summary>The network ready to run, or null when it is not installed or cannot be loaded.</summary>
        internal static Runner Get(Model m)
        {
            lock (Runners)
            {
                if (Runners.TryGetValue(m, out var r)) return r;
                if (!m.Installed) return null;
                r = Runner.Create(m);
                if (r != null) Runners[m] = r;
                return r;
            }
        }

        /// <summary>One network, on the graphics card if possible; if the card fails it moves to the processor for good.</summary>
        internal sealed class Runner
        {
            readonly Model _model;
            InferenceSession _session;
            public bool OnGpu { get; private set; }

            Runner(Model model, InferenceSession session, bool gpu) { _model = model; _session = session; OnGpu = gpu; }

            public static Runner Create(Model m)
            {
                var gpu = m.Gpu ? Open(m, true) : null;
                if (gpu != null) return new Runner(m, gpu, true);
                var cpu = Open(m, false);
                return cpu != null ? new Runner(m, cpu, false) : null;
            }

            static InferenceSession Open(Model m, bool gpu)
            {
                SessionOptions options = null;
                try
                {
                    options = new SessionOptions
                    {
                        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL,
                        IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1),
                    };
                    if (gpu)
                    {
                        // DirectML wants these two off; any DirectX 12 card works (NVIDIA, AMD, Intel).
                        options.EnableMemoryPattern = false;
                        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                        options.AppendExecutionProvider_DML(0);
                    }
                    if (m.Resource == null) return new InferenceSession(m.Path, options);
                    using var stream = typeof(AiModels).Assembly.GetManifestResourceStream(m.Resource);
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    stream.ReadExactly(bytes);
                    return new InferenceSession(bytes, options);
                }
                catch
                {
                    return null;
                }
                finally
                {
                    options?.Dispose();
                }
            }

            /// <summary>Runs one 1 × 3 × side × side tile (RGB planes, 0..1) and returns the output planes.</summary>
            public float[] Run(float[] planes, int side) => Run(planes, new long[] { 1, 3, side, side }, "input", "output")[0];

            /// <summary>Runs the network on one input tensor and returns the outputs asked for, in order.</summary>
            public float[][] Run(float[] data, long[] shape, string input, params string[] outputs) =>
                Run(new[] { (input, data, shape) }, outputs);

            /// <summary>Runs the network on several input tensors and returns the outputs asked for, in order.</summary>
            public float[][] Run((string Name, float[] Data, long[] Shape)[] inputs, params string[] outputs)
            {
                lock (this)
                {
                    try
                    {
                        return RunOn(_session, inputs, outputs);
                    }
                    catch when (OnGpu)
                    {
                        var cpu = Open(_model, false) ?? throw new InvalidOperationException("ONNX Runtime");
                        _session.Dispose();
                        _session = cpu;
                        OnGpu = false;
                        return RunOn(_session, inputs, outputs);
                    }
                }
            }

            static float[][] RunOn(InferenceSession session, (string Name, float[] Data, long[] Shape)[] inputs, string[] names)
            {
                var values = new OrtValue[inputs.Length];
                try
                {
                    for (int i = 0; i < inputs.Length; i++) values[i] = OrtValue.CreateTensorValueFromMemory(inputs[i].Data, inputs[i].Shape);
                    using var options = new RunOptions();
                    using var results = session.Run(options, inputs.Select(x => x.Name).ToArray(), values, names);
                    var list = new float[names.Length][];
                    for (int i = 0; i < names.Length; i++) list[i] = results[i].GetTensorDataAsSpan<float>().ToArray();
                    return list;
                }
                finally
                {
                    foreach (var v in values) v?.Dispose();
                }
            }
        }
    }
}
