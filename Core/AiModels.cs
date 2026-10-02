using System;
using System.Collections.Generic;
using System.IO;
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
            public string Url => BaseUrl + File;
            public string Path => System.IO.Path.Combine(Folder, File);
            public bool Installed => System.IO.File.Exists(Path);
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
                r = Runner.Create(m.Path);
                if (r != null) Runners[m] = r;
                return r;
            }
        }

        /// <summary>One network, on the graphics card if possible; if the card fails it moves to the processor for good.</summary>
        internal sealed class Runner
        {
            readonly string _path;
            InferenceSession _session;
            public bool OnGpu { get; private set; }

            Runner(string path, InferenceSession session, bool gpu) { _path = path; _session = session; OnGpu = gpu; }

            public static Runner Create(string path)
            {
                var gpu = Open(path, true);
                if (gpu != null) return new Runner(path, gpu, true);
                var cpu = Open(path, false);
                return cpu != null ? new Runner(path, cpu, false) : null;
            }

            static InferenceSession Open(string path, bool gpu)
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
                    return new InferenceSession(path, options);
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
            public float[] Run(float[] planes, int side)
            {
                lock (this)
                {
                    try
                    {
                        return RunOn(_session, planes, side);
                    }
                    catch when (OnGpu)
                    {
                        var cpu = Open(_path, false) ?? throw new InvalidOperationException("ONNX Runtime");
                        _session.Dispose();
                        _session = cpu;
                        OnGpu = false;
                        return RunOn(_session, planes, side);
                    }
                }
            }

            static float[] RunOn(InferenceSession session, float[] planes, int side)
            {
                var shape = new long[] { 1, 3, side, side };
                using var input = OrtValue.CreateTensorValueFromMemory(planes, shape);
                using var options = new RunOptions();
                using var outputs = session.Run(options, new[] { "input" }, new[] { input }, new[] { "output" });
                return outputs[0].GetTensorDataAsSpan<float>().ToArray();
            }
        }
    }
}
