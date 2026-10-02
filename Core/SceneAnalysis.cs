using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoStudio.Core
{
    /// <summary>A face found in a photo: its box in the whole frame (before straighten and crop), 0..1.</summary>
    public readonly record struct FaceBox(double X, double Y, double W, double H, double Score)
    {
        public double Cx => X + W / 2;
        public double Cy => Y + H / 2;
        public double Area => W * H;
    }

    /// <summary>
    /// What the automatic tools know about the content of a photo: where the sky is and where the faces are.
    /// Found once per photo on a small copy, so the result is the same for the preview and for the full-size photo.
    /// </summary>
    public sealed class SceneAnalysis
    {
        const int SkySide = 320, FaceSide = 640;

        /// <summary>The faces, largest first.</summary>
        public IReadOnlyList<FaceBox> Faces { get; private init; } = Array.Empty<FaceBox>();
        /// <summary>The sky, or null when the photo has none.</summary>
        public SkyMap Sky { get; private init; }
        /// <summary>Share of the frame covered by faces, 0..1.</summary>
        public double FaceArea => Faces.Sum(f => f.Area);

        public static SceneAnalysis Of(RawImage img) => img.Scene.Get(img);

        /// <summary>
        /// The subject found by the AI network, or null when it is not installed (it is asked only when a tool needs
        /// it, as it is slower than the rest of the analysis).
        /// </summary>
        public static SubjectMap SubjectOf(RawImage img) => img.Scene.Subject(img);

        internal static SceneAnalysis Build(RawImage img)
        {
            // Neither search may stop the development of a photo: without a result the tools fall back to the tones alone.
            SkyMap sky = null;
            IReadOnlyList<FaceBox> faces = Array.Empty<FaceBox>();
            try { sky = SkyMap.Detect(img.Downscale(SkySide)); } catch { }
            try
            {
                var small = img.Downscale(FaceSide);
                faces = FaceDetector.Detect(RawDevelop.RenderForAnalysis(small), small.Width, small.Height);
            }
            catch { }
            return new SceneAnalysis { Sky = sky, Faces = faces };
        }
    }

    /// <summary>The analysis of a photo, computed on first use and shared by the image and its reduced copies.</summary>
    sealed class SceneCache
    {
        SceneAnalysis _value;

        public SceneAnalysis Get(RawImage img)
        {
            lock (this) return _value ??= SceneAnalysis.Build(img);
        }

        SubjectMap _subject;
        bool _subjectDone;
        readonly object _subjectLock = new object();

        public SubjectMap Subject(RawImage img)
        {
            lock (_subjectLock)
            {
                // Without the network nothing is remembered: once it is downloaded the subject is found.
                if (_subjectDone || !AiModels.Subject.Installed) return _subject;
                try { _subject = SubjectMap.Detect(img); } catch { _subject = null; }
                _subjectDone = true;
                return _subject;
            }
        }
    }

    /// <summary>
    /// Where the sky is, pixel by pixel: the smooth blue (or, under an overcast sky, bright) area that reaches the top
    /// edge of the photo.
    /// It is kept at low resolution as the coefficients of a guided filter (He et al.) whose guide is the colour
    /// of the photo, so that at any size the outline of the mask follows the outlines of the photo (mountains,
    /// roofs, branches) instead of a blur, also where the sky is as bright as what stands against it.
    /// </summary>
    public sealed class SkyMap
    {
        readonly int _w, _h;
        readonly float[] _r, _g, _b, _offset;   // around each low-resolution pixel: sky = r·R + g·G + b·B + offset
        readonly float[] _gamma;                 // linear channel (16 bit) -> perceptual value relative to the photo's white

        /// <summary>Share of the frame covered by the sky, 0..1.</summary>
        public double Fraction { get; }
        /// <summary>Share of the sky that is blue: the rest is cloud, haze or a sunset.</summary>
        public double Blue { get; }
        /// <summary>1 for a blue sky; lower for a bright top without colour, which could also be a wall.</summary>
        public double Confidence { get; }

        SkyMap(int w, int h, float[] r, float[] g, float[] b, float[] offset, float[] gamma, double fraction, double blue, double confidence)
        {
            _w = w; _h = h; _r = r; _g = g; _b = b; _offset = offset; _gamma = gamma;
            Fraction = fraction; Blue = blue; Confidence = confidence;
        }

        static float SmoothStep(float e0, float e1, float x)
        {
            float t = (x - e0) / (e1 - e0);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }

        static int Luminance16(ushort[] d, int i) => (int)((d[i] * 13933u + d[i + 1] * 46871u + d[i + 2] * 4732u) >> 16);

        /// <summary>How much the pixel (x, y) of an image of any size (w × h, data d) belongs to the sky, 0..1.</summary>
        public float Weight(ushort[] d, int x, int y, int w, int h)
        {
            double fx = Math.Clamp((x + 0.5) * _w / w - 0.5, 0, _w - 1), fy = Math.Clamp((y + 0.5) * _h / h - 0.5, 0, _h - 1);
            int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(_w - 1, x0 + 1), y1 = Math.Min(_h - 1, y0 + 1);
            float ax = (float)(fx - x0), ay = (float)(fy - y0);
            int i00 = y0 * _w + x0, i01 = y0 * _w + x1, i10 = y1 * _w + x0, i11 = y1 * _w + x1;
            float w00 = (1 - ax) * (1 - ay), w01 = ax * (1 - ay), w10 = (1 - ax) * ay, w11 = ax * ay;
            int i = (y * w + x) * 3;
            float q = (_r[i00] * w00 + _r[i01] * w01 + _r[i10] * w10 + _r[i11] * w11) * _gamma[d[i]]
                    + (_g[i00] * w00 + _g[i01] * w01 + _g[i10] * w10 + _g[i11] * w11) * _gamma[d[i + 1]]
                    + (_b[i00] * w00 + _b[i01] * w01 + _b[i10] * w10 + _b[i11] * w11) * _gamma[d[i + 2]]
                    + _offset[i00] * w00 + _offset[i01] * w01 + _offset[i10] * w10 + _offset[i11] * w11;
            return SmoothStep(0.15f, 0.85f, q);
        }

        /// <summary>Looks for the sky in a small copy of the photo; null when there is none.</summary>
        internal static SkyMap Detect(RawImage img)
        {
            int w = img.Width, h = img.Height, n = w * h;
            if (w < 16 || h < 16) return null;
            var d = img.Data;
            var alpha = img.Alpha;

            // The photo's own white (99th percentile of the luminance): the search must not depend on the exposure.
            var hist = new int[65536];
            for (int p = 0; p < n; p++) hist[Luminance16(d, p * 3)]++;
            int white = 65535;
            for (int acc = 0, target = n / 100; white > 256 && (acc += hist[white]) <= target; white--) { }
            float inv = 1f / white;
            var gamma = new float[65536];
            for (int i = 0; i < 65536; i++) gamma[i] = (float)Math.Pow(Math.Min(1.5f, i * inv), 1 / 2.2);

            var I = new float[n];
            var cool = new float[n];    // blue minus red: 0 for a grey, positive towards blue
            var blue = new bool[n];
            for (int p = 0; p < n; p++)
            {
                I[p] = gamma[Luminance16(d, p * 3)];
                float r = gamma[d[p * 3]], g = gamma[d[p * 3 + 1]], b = gamma[d[p * 3 + 2]];
                cool[p] = (b - r) / Math.Max(Math.Max(r, b), 0.05f);
                // Sky blue: blue over green over red (a violet or a magenta object has less green than red).
                blue[p] = g >= r - 0.02f && b >= g - 0.02f && cool[p] >= 0.10f && I[p] >= 0.22f;
            }

            // A sky is smooth: no outline, of brightness or of colour, runs through it.
            var smooth = new bool[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (alpha != null && alpha[p] < 128) continue;
                    int left = y * w + Math.Max(0, x - 1), right = y * w + Math.Min(w - 1, x + 1);
                    int up = Math.Max(0, y - 1) * w + x, down = Math.Min(h - 1, y + 1) * w + x;
                    smooth[p] = Math.Max(Math.Abs(I[right] - I[left]), Math.Abs(I[down] - I[up])) <= 0.10f
                             && Math.Max(Math.Abs(cool[right] - cool[left]), Math.Abs(cool[down] - cool[up])) <= 0.12f;
                }

            // Every smooth area that reaches the top edge (or the upper part of the sides) may be sky.
            var area = new int[n];      // 0 = none, otherwise index in areas + 1
            var areas = new List<(int Count, int Blue, double Light, double Cool)>();
            var queue = new Queue<int>();
            void Grow(int seed)
            {
                if (area[seed] != 0 || !smooth[seed]) return;
                int id = areas.Count + 1, count = 0, blueCount = 0;
                double light = 0, coolness = 0;
                area[seed] = id;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue(), x = p % w, y = p / w;
                    count++;
                    light += I[p];
                    coolness += cool[p];
                    if (blue[p]) blueCount++;
                    void Visit(int q)
                    {
                        if (area[q] != 0 || !smooth[q] || Math.Abs(I[q] - I[p]) > 0.06f || Math.Abs(cool[q] - cool[p]) > 0.08f) return;
                        area[q] = id;
                        queue.Enqueue(q);
                    }
                    if (x > 0) Visit(p - 1);
                    if (x < w - 1) Visit(p + 1);
                    if (y > 0) Visit(p - w);
                    if (y < h - 1) Visit(p + w);
                }
                areas.Add((count, blueCount, light / count, coolness / count));
            }
            int margin = Math.Max(1, Math.Max(w, h) / 100);   // a few rows, not one: a scan can have a dark rim
            for (int y = 0; y < margin; y++)
                for (int x = 0; x < w; x++) Grow(y * w + x);
            for (int y = margin; y < h * 2 / 5; y++)
                for (int x = 0; x < margin; x++) { Grow(y * w + x); Grow(y * w + w - 1 - x); }

            // Where there is blue, the sky is the blue areas, the clouds that fade into them and the areas of nearly
            // the same colour (a paler stretch of sky beyond a cloud): a white wall against the sky is not a cloud.
            // Without blue it can only be an overcast sky: the bright areas.
            int smallest = Math.Max(8, n / 200);
            var large = areas.Select(a => a.Count >= smallest).ToArray();
            var blueAreas = areas.Where((a, i) => large[i] && a.Blue * 3 >= a.Count).ToList();
            double blueArea = blueAreas.Sum(a => (double)a.Count);
            bool anyBlue = blueArea >= n * 0.03;
            double skyLight = anyBlue ? blueAreas.Sum(a => a.Light * a.Count) / blueArea : 0;
            double skyCool = anyBlue ? blueAreas.Sum(a => a.Cool * a.Count) / blueArea : 0;
            var chosen = areas.Select((a, i) => large[i] && (anyBlue
                ? a.Blue * 3 >= a.Count || (a.Cool >= 0.04 && Math.Abs(a.Cool - skyCool) <= 0.12 && Math.Abs(a.Light - skyLight) <= 0.25)
                : a.Light >= 0.72)).ToArray();
            var sky = new bool[n];
            for (int p = 0; p < n; p++) sky[p] = area[p] != 0 && chosen[area[p] - 1];

            // The smoothness test leaves out the last pixels before an outline: take the sky up to it, as long as
            // the pixels look like the sky next to them. Without this a rim of sky would be left out around every roof.
            for (int step = 0; step < 3; step++)
            {
                var grown = (bool[])sky.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int q = y * w + x;
                        if (sky[q] || (alpha != null && alpha[q] < 128)) continue;
                        bool Like(int p) => sky[p] && Math.Abs(I[q] - I[p]) <= 0.05f && Math.Abs(cool[q] - cool[p]) <= 0.06f;
                        grown[q] = (x > 0 && Like(q - 1)) || (x < w - 1 && Like(q + 1)) || (y > 0 && Like(q - w)) || (y < h - 1 && Like(q + w));
                    }
                sky = grown;
            }
            FillHoles(sky, w, h);

            int total = 0, blueTotal = 0;
            double sumSky = 0, sumRest = 0;
            for (int p = 0; p < n; p++)
            {
                if (sky[p]) { total++; sumSky += I[p]; if (blue[p]) blueTotal++; }
                else sumRest += I[p];
            }
            double fraction = total / (double)n;
            if (fraction < 0.03 || fraction > 0.92) return null;
            double blueShare = blueTotal / (double)total, ev = img.SceneEv;
            double confidence;
            if (anyBlue)
            {
                if (ev < 6) return null;   // a blue wall in a room, not a sky (NaN = unknown: accepted)
                confidence = 1;
            }
            else
            {
                // A bright top without blue is an overcast sky only in daylight, and only when it is the light of
                // the photo; otherwise it is a wall, a ceiling or a window.
                if (ev < 10.5 || sumSky / total < sumRest / Math.Max(1, n - total) + 0.12) return null;
                confidence = double.IsNaN(ev) ? 0.5 : 0.8;
            }

            // Guided filter of the mask with the colour of the photo as guide: around each pixel the mask is
            // written as a combination of red, green and blue, which can then be evaluated on the full-size photo.
            int radius = Math.Max(2, Math.Max(w, h) / 100);
            const float eps = 0.0015f;
            float[] Mean(Func<int, float> value)
            {
                var v = new float[n];
                for (int p = 0; p < n; p++) v[p] = value(p);
                return RawDevelop.BoxMean(v, w, h, radius);
            }
            var cr = new float[n];
            var cg = new float[n];
            var cb = new float[n];
            for (int p = 0; p < n; p++) { cr[p] = gamma[d[p * 3]]; cg[p] = gamma[d[p * 3 + 1]]; cb[p] = gamma[d[p * 3 + 2]]; }
            float P(int p) => sky[p] ? 1 : 0;
            float[] mR = Mean(p => cr[p]), mG = Mean(p => cg[p]), mB = Mean(p => cb[p]), mP = Mean(P);
            float[] mRR = Mean(p => cr[p] * cr[p]), mRG = Mean(p => cr[p] * cg[p]), mRB = Mean(p => cr[p] * cb[p]);
            float[] mGG = Mean(p => cg[p] * cg[p]), mGB = Mean(p => cg[p] * cb[p]), mBB = Mean(p => cb[p] * cb[p]);
            float[] mRP = Mean(p => cr[p] * P(p)), mGP = Mean(p => cg[p] * P(p)), mBP = Mean(p => cb[p] * P(p));
            var ar = new float[n];
            var ag = new float[n];
            var ab = new float[n];
            var offset = new float[n];
            for (int p = 0; p < n; p++)
            {
                // Covariance of the colour (regularised) and of colour with mask; a = covariance⁻¹ · covariance with mask.
                double rr = mRR[p] - mR[p] * mR[p] + eps, rg = mRG[p] - mR[p] * mG[p], rb = mRB[p] - mR[p] * mB[p];
                double gg = mGG[p] - mG[p] * mG[p] + eps, gb = mGB[p] - mG[p] * mB[p], bb = mBB[p] - mB[p] * mB[p] + eps;
                double pr = mRP[p] - mR[p] * mP[p], pg = mGP[p] - mG[p] * mP[p], pb = mBP[p] - mB[p] * mP[p];
                double c00 = gg * bb - gb * gb, c01 = rb * gb - rg * bb, c02 = rg * gb - rb * gg;
                double c11 = rr * bb - rb * rb, c12 = rb * rg - rr * gb, c22 = rr * gg - rg * rg;
                double det = rr * c00 + rg * c01 + rb * c02;
                if (det > 1e-12)
                {
                    ar[p] = (float)((c00 * pr + c01 * pg + c02 * pb) / det);
                    ag[p] = (float)((c01 * pr + c11 * pg + c12 * pb) / det);
                    ab[p] = (float)((c02 * pr + c12 * pg + c22 * pb) / det);
                }
                offset[p] = mP[p] - ar[p] * mR[p] - ag[p] * mG[p] - ab[p] * mB[p];
            }
            return new SkyMap(w, h, RawDevelop.BoxMean(ar, w, h, radius), RawDevelop.BoxMean(ag, w, h, radius), RawDevelop.BoxMean(ab, w, h, radius),
                              RawDevelop.BoxMean(offset, w, h, radius), gamma, fraction, blueShare, confidence);
        }

        /// <summary>Small islands enclosed by the sky (the edge of a cloud, a bird, a wire) are sky too.</summary>
        static void FillHoles(bool[] sky, int w, int h)
        {
            int n = w * h, limit = Math.Max(4, n / 60);
            var seen = new bool[n];
            var stack = new Stack<int>();
            var island = new List<int>();
            for (int start = 0; start < n; start++)
            {
                if (sky[start] || seen[start]) continue;
                island.Clear();
                bool open = false;   // touches the left, right or bottom edge: it is the ground, not an island
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int p = stack.Pop(), x = p % w, y = p / w;
                    island.Add(p);
                    if (x == 0 || x == w - 1 || y == h - 1) open = true;
                    void Visit(int q)
                    {
                        if (sky[q] || seen[q]) return;
                        seen[q] = true;
                        stack.Push(q);
                    }
                    if (x > 0) Visit(p - 1);
                    if (x < w - 1) Visit(p + 1);
                    if (y > 0) Visit(p - w);
                    if (y < h - 1) Visit(p + w);
                }
                if (!open && island.Count <= limit)
                    foreach (int p in island) sky[p] = true;
            }
        }
    }
}
