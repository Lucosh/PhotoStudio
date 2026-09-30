using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Finds the faces of a photo with YuNet, a small neural network embedded in the application and run on the
    /// processor with ONNX Runtime: nothing is sent anywhere. It never throws: without a detector there are no faces.
    /// </summary>
    static class FaceDetector
    {
        const int Side = 640;               // the network takes a 640 × 640 image
        const float MinScore = 0.8f, MaxOverlap = 0.3f;
        static readonly int[] Strides = { 8, 16, 32 };
        static readonly Lazy<InferenceSession> Model = new Lazy<InferenceSession>(Load);

        static InferenceSession Load()
        {
            try
            {
                using var stream = typeof(FaceDetector).Assembly.GetManifestResourceStream("PhotoStudio.Assets.FaceDetector.onnx");
                if (stream == null) return null;
                var model = new byte[stream.Length];
                stream.ReadExactly(model);
                using var options = new SessionOptions
                {
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
                };
                return new InferenceSession(model, options);
            }
            catch
            {
                return null;   // e.g. the native library cannot be loaded on this PC
            }
        }

        /// <summary>The faces of an upright BGRA image at most 640 pixels wide and high, largest first.</summary>
        public static IReadOnlyList<FaceBox> Detect(byte[] bgra, int w, int h)
        {
            var session = Model.Value;
            if (session == null || w > Side || h > Side || w < 32 || h < 32) return Array.Empty<FaceBox>();

            // The image goes in the top left corner, as it is (blue, green, red planes, 0..255); the rest stays black.
            var input = new DenseTensor<float>(new[] { 1, 3, Side, Side });
            var buffer = input.Buffer.Span;
            const int plane = Side * Side;
            for (int y = 0; y < h; y++)
                for (int x = 0, i = y * w * 4, o = y * Side; x < w; x++, i += 4, o++)
                {
                    buffer[o] = bgra[i];
                    buffer[plane + o] = bgra[i + 1];
                    buffer[2 * plane + o] = bgra[i + 2];
                }

            var found = new List<(float X, float Y, float W, float H, float Score)>();
            using (var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input) }))
            {
                var outputs = results.ToDictionary(r => r.Name, r => r.AsEnumerable<float>().ToArray());
                foreach (int stride in Strides)
                {
                    float[] cls = outputs["cls_" + stride], obj = outputs["obj_" + stride], box = outputs["bbox_" + stride];
                    int cols = Side / stride, rows = Side / stride;
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            int i = r * cols + c;
                            float score = (float)Math.Sqrt(Math.Clamp(cls[i], 0f, 1f) * Math.Clamp(obj[i], 0f, 1f));
                            if (score < MinScore) continue;
                            float cx = (c + box[i * 4]) * stride, cy = (r + box[i * 4 + 1]) * stride;
                            float bw = (float)Math.Exp(box[i * 4 + 2]) * stride, bh = (float)Math.Exp(box[i * 4 + 3]) * stride;
                            found.Add((cx - bw / 2, cy - bh / 2, bw, bh, score));
                        }
                }
            }

            // Keep the best box of each face (non-maximum suppression).
            var kept = new List<(float X, float Y, float W, float H, float Score)>();
            foreach (var f in found.OrderByDescending(f => f.Score))
            {
                bool duplicate = kept.Any(k =>
                {
                    float ix = Math.Min(f.X + f.W, k.X + k.W) - Math.Max(f.X, k.X), iy = Math.Min(f.Y + f.H, k.Y + k.H) - Math.Max(f.Y, k.Y);
                    if (ix <= 0 || iy <= 0) return false;
                    float inter = ix * iy;
                    return inter / (f.W * f.H + k.W * k.H - inter) > MaxOverlap;
                });
                if (!duplicate) kept.Add(f);
            }

            var faces = new List<FaceBox>();
            foreach (var f in kept)
            {
                double x0 = Math.Clamp(f.X / w, 0, 1), y0 = Math.Clamp(f.Y / h, 0, 1);
                double x1 = Math.Clamp((f.X + f.W) / w, 0, 1), y1 = Math.Clamp((f.Y + f.H) / h, 0, 1);
                if (x1 - x0 < 0.02 || y1 - y0 < 0.02) continue;   // too small to matter for the light of the photo
                faces.Add(new FaceBox(x0, y0, x1 - x0, y1 - y0, f.Score));
            }
            return faces.OrderByDescending(f => f.Area).ToList();
        }
    }
}
