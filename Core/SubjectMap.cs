using System;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Where the main subject of the photo is, pixel by pixel (a person, an animal, an object), found by IS-Net
    /// (Dichotomous Image Segmentation) on the PC. Kept at the 1024 × 1024 of the network, stretched over the whole
    /// frame, so the same map serves every size of the photo.
    /// </summary>
    public sealed class SubjectMap
    {
        const int Side = 1024;
        readonly float[] _map;

        SubjectMap(float[] map) => _map = map;

        /// <summary>Share of the frame covered by the subject, 0..1.</summary>
        public double Fraction { get; private init; }

        /// <summary>The subject of the photo, or null when the network is not installed or finds nothing.</summary>
        internal static SubjectMap Detect(RawImage img)
        {
            var net = AiModels.Get(AiModels.Subject);
            if (net == null) return null;
            var small = img.Downscale(Side);
            var px = RawDevelop.RenderForAnalysis(small);
            int w = small.Width, h = small.Height;

            // The photo stretched to 1024 × 1024 (bilinear), RGB planes, centred on 0 as the network was trained.
            var input = new float[3 * Side * Side];
            const int plane = Side * Side;
            Parallel.For(0, Side, y =>
            {
                double sy = Math.Clamp((y + 0.5) * h / Side - 0.5, 0, h - 1);
                int y0 = (int)sy, y1 = Math.Min(h - 1, y0 + 1);
                float fy = (float)(sy - y0);
                for (int x = 0; x < Side; x++)
                {
                    double sx = Math.Clamp((x + 0.5) * w / Side - 0.5, 0, w - 1);
                    int x0 = (int)sx, x1 = Math.Min(w - 1, x0 + 1);
                    float fx = (float)(sx - x0);
                    for (int c = 0; c < 3; c++)
                    {
                        int ch = 2 - c;   // BGRA -> R, G, B
                        float a = px[(y0 * w + x0) * 4 + ch], b = px[(y0 * w + x1) * 4 + ch];
                        float d = px[(y1 * w + x0) * 4 + ch], e = px[(y1 * w + x1) * 4 + ch];
                        float v = (a + (b - a) * fx) * (1 - fy) + (d + (e - d) * fx) * fy;
                        input[c * plane + y * Side + x] = v / 255f - 0.5f;
                    }
                }
            });
            var output = net.Run(input, new long[] { 1, 3, Side, Side }, "input_image", "output_image")[0];

            // The output is a probability map; it is stretched so the subject reaches 1 and the background 0.
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in output) { if (v < lo) lo = v; if (v > hi) hi = v; }
            if (hi - lo < 1e-3f) return null;
            var map = new float[plane];
            double covered = 0;
            for (int i = 0; i < plane; i++)
            {
                float v = (output[i] - lo) / (hi - lo);
                v = v <= 0.15f ? 0 : v >= 0.85f ? 1 : (v - 0.15f) / 0.7f;
                map[i] = v * v * (3 - 2 * v);
                covered += map[i];
            }
            double fraction = covered / plane;
            return fraction < 0.002 ? null : new SubjectMap(map) { Fraction = fraction };
        }

        /// <summary>How much the pixel (x, y) of a w × h image belongs to the subject, 0..1.</summary>
        public float Weight(int x, int y, int w, int h) => At((x + 0.5f) / w, (y + 0.5f) / h);

        /// <summary>The map at a point of the frame given as fractions of its width and height.</summary>
        public float At(float u, float v)
        {
            float sx = Math.Clamp(u * Side - 0.5f, 0, Side - 1), sy = Math.Clamp(v * Side - 0.5f, 0, Side - 1);
            int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(Side - 1, x0 + 1), y1 = Math.Min(Side - 1, y0 + 1);
            float fx = sx - x0, fy = sy - y0;
            float a = _map[y0 * Side + x0], b = _map[y0 * Side + x1], c = _map[y1 * Side + x0], d = _map[y1 * Side + x1];
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }
    }
}
