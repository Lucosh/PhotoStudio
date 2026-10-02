using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoStudio.Core
{
    /// <summary>What the culling found out about a shot: how sharp it is and whether someone has the eyes closed.</summary>
    public sealed class ShotQuality
    {
        /// <summary>0 (blurred) .. 2 (very sharp, about 1 for a normally sharp photo), measured on the faces or, without faces, on the most detailed areas.</summary>
        public double Sharpness { get; init; }
        public int Faces { get; init; }
        /// <summary>Faces large enough to check whose two eyes are both closed.</summary>
        public int ClosedEyes { get; init; }
        /// <summary>Set by <see cref="ShotCheck.Rank"/>: blurred, on its own or compared with the rest of its burst.</summary>
        public bool Blurry { get; internal set; }
        /// <summary>Set by <see cref="ShotCheck.Rank"/>: the sharpest shot without closed eyes of its burst.</summary>
        public bool Best { get; internal set; }
        public bool HasProblem => Blurry || ClosedEyes > 0;
    }

    /// <summary>
    /// Sharpness and closed eyes, measured on the preview of a photo (for a RAW the JPEG embedded by the camera),
    /// everything on the PC.
    /// </summary>
    public static class ShotCheck
    {
        const int PreviewSide = 2048;
        /// <summary>Below this a shot is blurred whatever the others are.</summary>
        const double BlurLimit = 0.5;
        /// <summary>In a burst, a shot this much less sharp than the sharpest one is blurred.</summary>
        const double BurstRatio = 0.7;
        /// <summary>Eye opening (eyelid gap over eye width) under which an eye is closed.</summary>
        const double ClosedEye = 0.16;

        public static ShotQuality Assess(string path) => Assess(PhotoLibrary.LoadPreview(path, PreviewSide));

        public static ShotQuality Assess(BitmapSource preview)
        {
            var src = new FormatConvertedBitmap(preview, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            return Assess(px, w, h);
        }

        public static ShotQuality Assess(byte[] px, int w, int h)
        {
            var faces = FindFaces(px, w, h);
            var gray = new float[w * h];
            for (int p = 0, i = 0; p < gray.Length; p++, i += 4)
                gray[p] = (0.114f * px[i] + 0.587f * px[i + 1] + 0.299f * px[i + 2]) / 255f;

            // People are what has to be sharp; without people, the most detailed part of the photo (the focus point).
            var regions = faces.Count > 0
                ? faces.Select(f => Expand(f, w, h, 1.1)).ToList()
                : new List<(int X0, int Y0, int X1, int Y1)> { (0, 0, w - 1, h - 1) };
            double sharp = Sharpness(gray, w, h, regions);

            int closed = 0;
            foreach (var f in faces)
                if (f.W * w >= 64 && EyesClosed(px, w, h, f)) closed++;
            return new ShotQuality { Sharpness = sharp, Faces = faces.Count, ClosedEyes = closed };
        }

        static IReadOnlyList<FaceBox> FindFaces(byte[] px, int w, int h)
        {
            int f = (int)Math.Ceiling(Math.Max(w, h) / 640.0);
            int sw = w / f, sh = h / f;
            var small = new byte[sw * sh * 4];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int yy = 0; yy < f; yy++)
                        for (int xx = 0; xx < f; xx++)
                        {
                            int i = ((y * f + yy) * w + x * f + xx) * 4;
                            b += px[i]; g += px[i + 1]; r += px[i + 2];
                        }
                    int o = (y * sw + x) * 4, n = f * f;
                    small[o] = (byte)(b / n); small[o + 1] = (byte)(g / n); small[o + 2] = (byte)(r / n); small[o + 3] = 255;
                }
            try { return FaceDetector.Detect(small, sw, sh); }
            catch { return Array.Empty<FaceBox>(); }
        }

        static (int X0, int Y0, int X1, int Y1) Expand(FaceBox f, int w, int h, double k)
        {
            double cx = f.Cx * w, cy = f.Cy * h, rx = f.W * w * k / 2, ry = f.H * h * k / 2;
            return (Math.Max(0, (int)(cx - rx)), Math.Max(0, (int)(cy - ry)), Math.Min(w - 1, (int)(cx + rx)), Math.Min(h - 1, (int)(cy + ry)));
        }

        /// <summary>
        /// Gradient energy at full and at half resolution, on the most textured blocks of the regions. A sharp edge
        /// keeps its steepness when the image is halved, so the energy halves with the pixel count: the ratio is 2.
        /// A blurred edge (wider than two pixels) gets twice as steep at half size: the ratio is 1. The result is ratio - 1;
        /// fine texture and grain can take it above 1.
        /// </summary>
        static double Sharpness(float[] g, int w, int h, List<(int X0, int Y0, int X1, int Y1)> regions)
        {
            int hw = w / 2, hh = h / 2;
            var half = new float[hw * hh];
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                {
                    int p = 2 * y * w + 2 * x;
                    half[y * hw + x] = (g[p] + g[p + 1] + g[p + w] + g[p + w + 1]) / 4;
                }
            const int B = 32;
            var blocks = new List<(double Fx, double Fy, double Hx, double Hy)>();
            foreach (var r in regions)
                for (int by = r.Y0; by + B <= r.Y1 + 1; by += B)
                    for (int bx = r.X0; bx + B <= r.X1 + 1; bx += B)
                    {
                        double fx = 0, fy = 0, hx = 0, hy = 0;
                        for (int y = by; y < by + B - 1 && y < h - 1; y++)
                            for (int x = bx; x < bx + B - 1 && x < w - 1; x++)
                            {
                                int p = y * w + x;
                                float dx = g[p + 1] - g[p], dy = g[p + w] - g[p];
                                fx += dx * dx; fy += dy * dy;
                            }
                        for (int y = by / 2; y < (by + B) / 2 - 1 && y < hh - 1; y++)
                            for (int x = bx / 2; x < (bx + B) / 2 - 1 && x < hw - 1; x++)
                            {
                                int p = y * hw + x;
                                float dx = half[p + 1] - half[p], dy = half[p + hw] - half[p];
                                hx += dx * dx; hy += dy * dy;
                            }
                        blocks.Add((fx, fy, hx, hy));
                    }
            if (blocks.Count == 0) return 0;
            // The most textured tenth of the blocks: flat areas (sky, walls) say nothing about focus.
            var top = blocks.OrderByDescending(b => b.Hx + b.Hy).Take(Math.Max(4, blocks.Count / 10)).ToList();
            double fxs = top.Sum(b => b.Fx), fys = top.Sum(b => b.Fy), hxs = top.Sum(b => b.Hx), hys = top.Sum(b => b.Hy);
            if (hxs < 1e-6 || hys < 1e-6) return 0;
            // Each direction on its own, the worse one counts: camera shake smears one direction only.
            return Math.Clamp(Math.Min(fxs / hxs, fys / hys) - 1, 0, 2);
        }

        // ================= Eyes =================

        static readonly int[] LeftEye = { 33, 133, 160, 144, 159, 145, 158, 153 };
        static readonly int[] RightEye = { 263, 362, 387, 373, 386, 374, 385, 380 };

        /// <summary>True when the two eyes of the face are closed.</summary>
        static bool EyesClosed(byte[] px, int w, int h, FaceBox f)
        {
            var o = EyeOpenings(px, w, h, f);
            return o.HasValue && o.Value.Left < ClosedEye && o.Value.Right < ClosedEye;
        }

        /// <summary>
        /// How open each eye is (eyelid gap over eye width; about 0.3 open, under 0.15 closed), from the MediaPipe
        /// face landmarks; null when the network does not see a face there. The crop is made a second time turned so
        /// the eyes are level, as MediaPipe expects.
        /// </summary>
        internal static (double Left, double Right)? EyeOpenings(byte[] px, int w, int h, FaceBox f)
        {
            var net = AiModels.Get(AiModels.FaceLandmarks);
            if (net == null) return null;
            double side = Math.Max(f.W * w, f.H * h) * 1.5, cx = f.Cx * w, cy = f.Cy * h, roll = 0;
            float[] pts = null;
            for (int pass = 0; pass < 2; pass++)
            {
                pts = Landmarks(net, px, w, h, cx, cy, side, roll);
                if (pts == null) return null;
                // Back to photo pixels, then the crop again around the face, turned by its roll.
                const int S = 256;
                double cos = Math.Cos(roll), sin = Math.Sin(roll);
                double Px(int k) => cx + ((pts[k * 3] - S / 2.0) * cos - (pts[k * 3 + 1] - S / 2.0) * sin) * side / S;
                double Py(int k) => cy + ((pts[k * 3] - S / 2.0) * sin + (pts[k * 3 + 1] - S / 2.0) * cos) * side / S;
                double lx = (Px(33) + Px(133)) / 2, ly = (Py(33) + Py(133)) / 2, rx = (Px(263) + Px(362)) / 2, ry = (Py(263) + Py(362)) / 2;
                roll = Math.Atan2(ry - ly, rx - lx);
                double mx = double.MaxValue, my = double.MaxValue, Mx = double.MinValue, My = double.MinValue;
                for (int k = 0; k < 468; k++) { mx = Math.Min(mx, Px(k)); Mx = Math.Max(Mx, Px(k)); my = Math.Min(my, Py(k)); My = Math.Max(My, Py(k)); }
                cx = (mx + Mx) / 2; cy = (my + My) / 2;
                side = Math.Max(Mx - mx, My - my) * 1.5;
            }
            double Opening(int[] e)
            {
                double D(int a, int b) => Math.Sqrt(Math.Pow(pts[a * 3] - pts[b * 3], 2) + Math.Pow(pts[a * 3 + 1] - pts[b * 3 + 1], 2));
                double width = D(e[0], e[1]);
                return width < 1 ? 1 : (D(e[2], e[3]) + D(e[4], e[5]) + D(e[6], e[7])) / (3 * width);
            }
            return (Opening(LeftEye), Opening(RightEye));
        }

        /// <summary>The 478 landmarks (x, y, z in the 256 × 256 crop), or null when there is no face in the crop.</summary>
        static float[] Landmarks(AiModels.Runner net, byte[] px, int w, int h, double cx, double cy, double side, double roll)
        {
            const int S = 256;
            double cos = Math.Cos(roll), sin = Math.Sin(roll), k = side / S;
            var input = new float[S * S * 3];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    double u = (x + 0.5 - S / 2.0) * k, v = (y + 0.5 - S / 2.0) * k;
                    int sx = Math.Clamp((int)(cx + u * cos - v * sin), 0, w - 1), sy = Math.Clamp((int)(cy + u * sin + v * cos), 0, h - 1);
                    int i = (sy * w + sx) * 4, o = (y * S + x) * 3;
                    input[o] = px[i + 2] / 255f; input[o + 1] = px[i + 1] / 255f; input[o + 2] = px[i] / 255f;
                }
            float[][] r;
            try { r = net.Run(input, new long[] { 1, S, S, 3 }, "input_12", "Identity", "Identity_1"); }
            catch { return null; }
            return 1 / (1 + Math.Exp(-r[1][0])) < 0.5 ? null : r[0];   // not a face after all (or turned away)
        }

        // ================= Bursts =================

        /// <summary>Marks the blurred shots and, in each burst, the best one, from the qualities measured so far.</summary>
        public static void Rank(IReadOnlyList<(ShotQuality Quality, int Burst)> shots)
        {
            foreach (var s in shots) { s.Quality.Blurry = s.Quality.Sharpness < BlurLimit; s.Quality.Best = false; }
            foreach (var burst in shots.Where(s => s.Burst != 0).GroupBy(s => s.Burst))
            {
                var list = burst.Select(s => s.Quality).ToList();
                if (list.Count < 2) continue;
                double best = list.Max(q => q.Sharpness);
                foreach (var q in list) if (q.Sharpness < best * BurstRatio) q.Blurry = true;
                var pick = list.Where(q => !q.HasProblem).OrderByDescending(q => q.Sharpness).FirstOrDefault();
                if (pick != null) pick.Best = true;
            }
        }
    }
}
