using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>Develops linear RGB data into an 8-bit BGRA image.</summary>
    public static partial class RawDevelop
    {
        const int ToneSize = 4096;
        const double ToneMax = 4.0;    // perceptual domain covered by the tone LUT (≈ 4.5 stops over white)
        const int GammaSize = 16384;

        static readonly float[] EncodeLut = BuildEncodeLut();   // linear 0..1 -> sRGB 0..1
        static readonly float[] Pow22Lut = BuildPow22Lut();     // perceptual 0..1 -> linear 0..1

        static float[] BuildEncodeLut()
        {
            var l = new float[GammaSize + 1];
            for (int i = 0; i <= GammaSize; i++) l[i] = (float)Adjustments.LinearToSrgb(i / (double)GammaSize);
            return l;
        }

        static float[] BuildPow22Lut()
        {
            var l = new float[GammaSize + 1];
            for (int i = 0; i <= GammaSize; i++) l[i] = (float)Math.Pow(i / (double)GammaSize, 2.2);
            return l;
        }

        static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Global tone curve in the perceptual domain (x = Y^(1/2.2)): blacks, whites, highlight roll-off and contrast.
        /// Highlights and Shadows are local (see LocalOffset). Identity at 0 on display-referred data.
        /// </summary>
        static double Tone(double x, RawSettings s, bool scene)
        {
            double B = s.Blacks / 100, W = s.Whites / 100, C = s.Contrast / 100;
            x += B * 0.16 * (1 - SmoothStep(0, 0.5, x));
            x *= Math.Pow(2, W * 0.7 * SmoothStep(0.3, 1.0, x));
            if (scene)
            {
                // Soft highlight roll-off: values above white are compressed instead of clipped.
                const double knee = 0.78;
                if (x > knee) x = knee + (1 - knee) * Math.Tanh((x - knee) / (1 - knee));
            }
            x = Math.Clamp(x, 0, 1);
            double c = (scene ? 0.15 : 0) + C * 0.8;
            double sm = x * x * (3 - 2 * x);
            x += c * (sm - x);
            return Math.Clamp(x, 0, 1);
        }

        static float[] BuildToneLut(RawSettings s, bool scene)
        {
            var lut = new float[ToneSize + 2];
            for (int i = 0; i <= ToneSize; i++) lut[i] = (float)Tone(i * ToneMax / ToneSize, s, scene);
            lut[ToneSize + 1] = lut[ToneSize];
            return lut;
        }

        /// <summary>Brightness offset for a pixel whose smoothed (base) perceptual luminance is b.</summary>
        static double LocalOffset(double b, double x, double shadows, double highlights)
        {
            double bc = Math.Clamp(b, 0, 1);
            // Dark tones are lifted, but the pixel's own darkness keeps true black anchored (as in Camera Raw).
            double off = shadows * 0.25 * 6.75 * bc * (1 - bc) * (1 - bc) * SmoothStep(0.02, 0.22, x);
            off += highlights * 0.35 * SmoothStep(0.35, 1.0, b) * Math.Min(b, 1.5);    // bright areas, incl. RAW headroom
            return off;
        }

        /// <summary>
        /// Edge-aware smooth version of the luminance (self-guided "fast guided filter", He et al.):
        /// coefficients are computed at reduced resolution and upsampled, so edges stay sharp (no halos).
        /// </summary>
        static float[] LocalBase(float[] x, int w, int h)
        {
            int f = Math.Max(1, (int)Math.Round(Math.Min(w, h) / 360.0));
            int lw = Math.Max(1, w / f), lh = Math.Max(1, h / f);
            var I = new float[lw * lh];
            Parallel.For(0, lh, y =>
            {
                for (int xx = 0; xx < lw; xx++)
                {
                    double sum = 0;
                    for (int yy = y * f; yy < y * f + f; yy++)
                        for (int k = xx * f; k < xx * f + f; k++) sum += Math.Min(2f, x[yy * w + k]);
                    I[y * lw + xx] = (float)(sum / (f * f));
                }
            });
            int r = Math.Max(2, (int)(Math.Min(lw, lh) * 0.045));
            const float eps = 0.012f;
            var II = new float[I.Length];
            for (int i = 0; i < I.Length; i++) II[i] = I[i] * I[i];
            var meanI = BoxMean(I, lw, lh, r);
            var meanII = BoxMean(II, lw, lh, r);
            var a = new float[I.Length];
            var b = new float[I.Length];
            for (int i = 0; i < I.Length; i++)
            {
                float var = Math.Max(0, meanII[i] - meanI[i] * meanI[i]);
                a[i] = var / (var + eps);
                b[i] = meanI[i] - a[i] * meanI[i];
            }
            var ma = BoxMean(a, lw, lh, r);
            var mb = BoxMean(b, lw, lh, r);

            var q = new float[w * h];
            Parallel.For(0, h, y =>
            {
                double sy = Math.Clamp((y + 0.5) / f - 0.5, 0, lh - 1);
                int y0 = (int)sy, y1 = Math.Min(lh - 1, y0 + 1);
                float fy = (float)(sy - y0);
                for (int xx = 0; xx < w; xx++)
                {
                    double sx = Math.Clamp((xx + 0.5) / f - 0.5, 0, lw - 1);
                    int x0 = (int)sx, x1 = Math.Min(lw - 1, x0 + 1);
                    float fx = (float)(sx - x0);
                    int i00 = y0 * lw + x0, i01 = y0 * lw + x1, i10 = y1 * lw + x0, i11 = y1 * lw + x1;
                    float A = (ma[i00] * (1 - fx) + ma[i01] * fx) * (1 - fy) + (ma[i10] * (1 - fx) + ma[i11] * fx) * fy;
                    float B = (mb[i00] * (1 - fx) + mb[i01] * fx) * (1 - fy) + (mb[i10] * (1 - fx) + mb[i11] * fx) * fy;
                    int p = y * w + xx;
                    q[p] = A * Math.Min(2f, x[p]) + B;
                }
            });
            return q;
        }

        /// <summary>Mean over a (2r+1)² box with clamped edges.</summary>
        internal static float[] BoxMean(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float inv = 1f / (2 * r + 1);
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                double s = 0;
                for (int k = -r; k <= r; k++) s += src[row + Math.Clamp(k, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = (float)(s * inv);
                    s += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
                }
            });
            Parallel.For(0, w, x =>
            {
                double s = 0;
                for (int k = -r; k <= r; k++) s += tmp[Math.Clamp(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = (float)(s * inv);
                    s += tmp[Math.Min(y + r + 1, h - 1) * w + x] - tmp[Math.Max(y - r, 0) * w + x];
                }
            });
            return dst;
        }

        // Slider-to-multiplier model: at ±100 the red/blue ratio changes by e^±1.2 (≈3.3×, enough for tungsten).
        const double TempK = 0.6, TintK = 0.35;
        // Baseline exposure for camera RAW data (like Adobe's), so "as shot" is not too dark.
        const double BaseEv = 0.5;

        static void WhiteBalanceGains(RawSettings s, out double r, out double g, out double b)
        {
            double kT = s.Temperature / 100, kn = s.Tint / 100;
            r = Math.Exp(TempK * kT);
            b = Math.Exp(-TempK * kT);
            g = Math.Exp(-TintK * kn);
            double norm = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            r /= norm; g /= norm; b /= norm;
        }

        /// <summary>
        /// Develops the whole frame (straighten and crop are applied by <see cref="Develop"/>).
        /// maskPreview >= 0 tints the area of that mask in red, to show beginners what the mask selects.
        /// </summary>
        public static byte[] Render(RawImage img, RawSettings s, int maskPreview = -1)
        {
            int w = img.Width, h = img.Height, n = w * h;
            bool scene = img.SceneReferred;
            var d = img.Data;
            WhiteBalanceGains(s, out double gr, out double gg, out double gb);
            double ex = Math.Pow(2, s.Exposure + (scene ? BaseEv : 0)) / 65535.0;
            float fr = (float)(gr * ex), fg = (float)(gg * ex), fb = (float)(gb * ex);
            var tone = BuildToneLut(s, scene);
            const float toneScale = (float)(ToneSize / ToneMax);
            double scale = img.Scale > 0 ? img.Scale : 1;   // pixels of this image per full-resolution pixel

            // Pass 1: perceptual luminance for every pixel.
            var xt = new float[n];
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    float Y = 0.2126f * d[p * 3] * fr + 0.7152f * d[p * 3 + 1] * fg + 0.0722f * d[p * 3 + 2] * fb;
                    xt[p] = Y > 0 ? (float)Math.Pow(Y, 1 / 2.2) : 0;
                }
            });

            // Luminance noise reduction first, so the local contrast tools below do not amplify the noise.
            if (s.NoiseLuma >= 1) DenoiseLuminance(xt, w, h, s.NoiseLuma, scale);

            // Local Highlights / Shadows (like Adobe's): the correction follows an edge-aware base layer,
            // so bright and dark regions are adjusted while local detail and contrast are preserved.
            double S = s.Shadows / 100, H = s.Highlights / 100;
            if (Math.Abs(S) > 0.004 || Math.Abs(H) > 0.004)
            {
                var baseLayer = LocalBase(xt, w, h);
                Parallel.For(0, h, y =>
                {
                    for (int p = y * w, end = p + w; p < end; p++)
                    {
                        float v = xt[p] + (float)LocalOffset(baseLayer[p], xt[p], S, H);
                        xt[p] = v < 0 ? 0 : v;
                    }
                });
            }

            // Global tone curve (blacks, whites, roll-off, contrast), then the user's point curve.
            var pointCurve = CurveMath.BuildLut(s.CurveRgb);
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    float t = xt[p] * toneScale;
                    int i = (int)t;
                    float v = i >= ToneSize ? tone[ToneSize] : tone[i] + (tone[i + 1] - tone[i]) * (t - i);
                    xt[p] = pointCurve != null ? CurveMath.Apply(pointCurve, v) : v;
                }
            });

            // Texture: medium-frequency detail (skin, foliage), then Clarity: local contrast on the midtones.
            if (Math.Abs(s.Texture) >= 0.5) ApplyTexture(xt, w, h, s.Texture, scale);
            if (Math.Abs(s.Clarity) >= 0.5)
            {
                var lb = new byte[n];
                for (int p = 0; p < n; p++) lb[p] = (byte)(xt[p] * 255 + 0.5f);
                ImageOps.GaussianBlur(lb, w, h, 1, Math.Max(2, Math.Min(w, h) / 50.0));
                float a = (float)(s.Clarity / 100 * 0.9);
                Parallel.For(0, h, y =>
                {
                    for (int p = y * w, end = p + w; p < end; p++)
                    {
                        float x = xt[p];
                        float v = x + a * (x - lb[p] / 255f) * (4 * x * (1 - x) + 0.15f);
                        xt[p] = v < 0 ? 0 : v > 1 ? 1 : v;
                    }
                });
            }

            // Local adjustments (masks): brightness here, colour in pass 2.
            var frame = new Frame(w, h, s);
            var masks = MaskSet.Create(img, s, xt, frame, maskPreview);
            masks?.ApplyTone(xt);

            var haze = Math.Abs(s.Dehaze) >= 0.5 ? HazeMap.Build(d, xt, fr, fg, fb, w, h, s.Dehaze) : null;
            var curveR = CurveMath.BuildLut(s.CurveR);
            var curveG = CurveMath.BuildLut(s.CurveG);
            var curveB = CurveMath.BuildLut(s.CurveB);
            bool channelCurves = curveR != null || curveG != null || curveB != null;
            var color = ColorStage.Create(s);
            var effects = Effects.Create(s, frame, scale);

            // Pass 2: apply the luminance change to RGB, gamut map, encode, colour tools, effects.
            var outPx = new byte[n * 4];
            float satF = (float)(s.Saturation / 100), vibF = (float)(s.Vibrance / 100);
            var alpha = img.Alpha;
            Parallel.For(0, h, y =>
            {
                for (int x = 0, p = y * w; x < w; x++, p++)
                {
                    float r = d[p * 3] * fr, g = d[p * 3 + 1] * fg, b = d[p * 3 + 2] * fb;
                    float localSat = 0;
                    if (masks != null && masks.HasColor)
                    {
                        masks.Color(p, x, y, out float kT, out float kn, out localSat);
                        if (kT != 0 || kn != 0)
                        {
                            r *= (float)Math.Exp(TempK * kT);
                            b *= (float)Math.Exp(-TempK * kT);
                            g *= (float)Math.Exp(-TintK * kn);
                        }
                    }
                    float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float Yt = Pow22Lut[(int)(Math.Clamp(xt[p], 0f, 1f) * GammaSize + 0.5f)];
                    if (Y > 1e-7f) { float k = Yt / Y; r *= k; g *= k; b *= k; }
                    else r = g = b = Yt;

                    float m = Math.Max(r, Math.Max(g, b));
                    if (m > 1)
                    {
                        if (Yt >= 1) r = g = b = 1;
                        else
                        {
                            float t = (1 - Yt) / (m - Yt);
                            r = Yt + (r - Yt) * t; g = Yt + (g - Yt) * t; b = Yt + (b - Yt) * t;
                        }
                    }
                    float R = Encode(r), G = Encode(g), Bc = Encode(b);

                    haze?.Apply(x, y, ref R, ref G, ref Bc);
                    if (channelCurves)
                    {
                        R = CurveMath.Apply(curveR, R);
                        G = CurveMath.Apply(curveG, G);
                        Bc = CurveMath.Apply(curveB, Bc);
                    }
                    color?.Apply(ref R, ref G, ref Bc);

                    float sat = satF + localSat;
                    if (Math.Abs(sat) > 0.001f || Math.Abs(vibF) > 0.001f)
                    {
                        float mx = Math.Max(R, Math.Max(G, Bc)), mn = Math.Min(R, Math.Min(G, Bc));
                        float cur = mx - mn;
                        float factor = 1 + sat + (vibF > 0 ? vibF * (1 - cur) * (1 - cur) * 1.5f : vibF);
                        if (factor < 0) factor = 0;
                        float l = 0.299f * R + 0.587f * G + 0.114f * Bc;
                        R = l + (R - l) * factor; G = l + (G - l) * factor; Bc = l + (Bc - l) * factor;
                    }
                    effects?.Apply(x, y, ref R, ref G, ref Bc);
                    if (masks != null && masks.PreviewIndex >= 0)
                    {
                        float mw = masks.PreviewWeight(p, x, y) * 0.6f;
                        R = R + (1 - R) * mw; G *= 1 - mw; Bc *= 1 - mw;
                    }

                    int o = p * 4;
                    outPx[o] = ToByte(Bc);
                    outPx[o + 1] = ToByte(G);
                    outPx[o + 2] = ToByte(R);
                    outPx[o + 3] = alpha != null ? alpha[p] : (byte)255;
                }
            });

            if (s.NoiseColor >= 1) ReduceColorNoise(outPx, w, h, s.NoiseColor, scale);
            if (s.Sharpening >= 1)
            {
                double radius = Math.Max(0.4, 1.0 * img.Scale);
                outPx = Filters.UnsharpMask(outPx, w, h, s.Sharpening, radius, 2);
            }
            return outPx;
        }

        static float Encode(float v)
        {
            if (v <= 0) return 0;
            if (v >= 1) return 1;
            return EncodeLut[(int)(v * GammaSize + 0.5f)];
        }

        static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)(v * 255 + 0.5f);

        // ================= Automatic settings =================

        /// <summary>
        /// Estimates the R and B multipliers (G = 1) that neutralise the scene illuminant.
        /// Start: geometric mean of grey-world and white-patch (brightest 3%) estimates.
        /// Then: iterative refinement on pixels that look grey after the current correction.
        /// </summary>
        static void EstimateWhiteBalance(List<(float r, float g, float b, float y)> px, out double gr, out double gb, out double confidence)
        {
            confidence = 0;
            double wr = 0, wg = 0, wb = 0;
            var ys = new float[px.Count];
            for (int i = 0; i < px.Count; i++) { wr += px[i].r; wg += px[i].g; wb += px[i].b; ys[i] = px[i].y; }
            Array.Sort(ys);
            float thr = ys[(int)(ys.Length * 0.97)];
            double pr = 0, pg = 0, pb = 0;
            foreach (var p in px)
                if (p.y >= thr) { pr += p.r; pg += p.g; pb += p.b; }
            double er = Math.Sqrt(wr * pr), eg = Math.Sqrt(wg * pg), eb = Math.Sqrt(wb * pb);
            gr = er > 0 ? eg / er : 1;
            gb = eb > 0 ? eg / eb : 1;

            for (int it = 0; it < 8; it++)
            {
                double sr = 0, sg = 0, sb = 0;
                int cnt = 0;
                foreach (var p in px)
                {
                    double r = p.r * gr, g = p.g, b = p.b * gb;
                    double R = Math.Sqrt(r), G = Math.Sqrt(g), B = Math.Sqrt(b);
                    double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    if (mx <= 0 || (mx - mn) / mx > 0.10) continue;
                    sr += r; sg += g; sb += b; cnt++;
                }
                if (cnt < 30 || cnt < px.Count * 0.005 || sr <= 0 || sb <= 0) break;
                confidence = cnt / (double)px.Count;   // share of the image that looks neutral
                double cr = sg / sr, cb = sg / sb;
                gr *= cr;
                gb *= cb;
                if (Math.Abs(cr - 1) < 0.002 && Math.Abs(cb - 1) < 0.002) break;
            }
            gr = Math.Clamp(gr, 0.25, 4);
            gb = Math.Clamp(gb, 0.25, 4);
        }

        /// <summary>
        /// Automatic white balance (like the "Automatico" entry of Camera Raw's white balance menu).
        /// Returns Temperature/Tint slider values relative to "as shot".
        /// </summary>
        /// <param name="cautious">
        /// For the one-click tools, where nobody asked for a new white balance: only a slight cast is corrected,
        /// the one read on the surfaces that are already nearly grey. A photo without them, or one whose colour
        /// is its subject (a sunset, a lawn, a red dress), is left as it is.
        /// </param>
        public static (double Temperature, double Tint) AutoWhiteBalance(RawImage img, bool cautious = false)
        {
            bool scene = img.SceneReferred;
            var d = img.Data;
            var alpha = img.Alpha;
            int n = img.Width * img.Height;
            int step = Math.Max(1, n / 300000);
            var samples = new List<(float r, float g, float b, float y)>();
            for (int p = 0; p < n; p += step)
            {
                if (alpha != null && alpha[p] < 128) continue;
                float r = d[p * 3] / 65535f, g = d[p * 3 + 1] / 65535f, b = d[p * 3 + 2] / 65535f;
                float mx = Math.Max(r, Math.Max(g, b));
                float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                if (Y < 0.004f || mx > 0.97f) continue;   // noise / clipped
                samples.Add((r, g, b, Y));
            }
            if (samples.Count < 20) return (0, 0);
            // Slider values that apply the gains in part; "warmth" is how much of a cooling correction is kept,
            // so that warm scenes (sunsets, lamps) stay a little warm.
            static (double Temp, double Tint) Sliders(double gainR, double gainB, double strength, double warmth)
            {
                double kT = Math.Log(gainR / gainB) / (2 * TempK), kn = Math.Log(Math.Sqrt(gainR * gainB)) / TintK;
                if (kT < 0) strength *= warmth;
                return (kT * 100 * strength, kn * 100 * strength);
            }
            static double Slider(double v, double max) => Math.Abs(v) < 3 ? 0 : Math.Round(Math.Clamp(v, -max, max));

            EstimateWhiteBalance(samples, out double wbR, out double wbB, out double confidence);
            double trust = Math.Clamp(0.4 + confidence * 6, 0.4, 1);   // few neutral surfaces: correct less
            var full = Sliders(wbR, wbB, (scene ? 0.9 : 0.8) * trust, 0.85);
            if (!cautious) return (Slider(full.Temp, 90), Slider(full.Tint, 60));

            // Cautious: the cast is read on what is nearly grey as it is (not in the dark, where colour is noise)...
            double sr = 0, sg = 0, sb = 0;
            int grey = 0;
            foreach (var p in samples)
            {
                if (p.y < 0.03f) continue;
                double R = Math.Sqrt(p.r), G = Math.Sqrt(p.g), B = Math.Sqrt(p.b);
                double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                if ((mx - mn) / mx > 0.12) continue;
                sr += p.r; sg += p.g; sb += p.b; grey++;
            }
            double share = grey / (double)samples.Count;
            if (share < 0.04 || sr <= 0 || sb <= 0) return (0, 0);
            var slight = Sliders(sg / sr, sg / sb, 0.7 * SmoothStep(0.04, 0.15, share), 0.6);
            // ...and corrected only as far as the full estimate agrees.
            static double Agreed(double a, double b) => a * b <= 0 ? 0 : Math.Sign(a) * Math.Min(Math.Abs(a), Math.Abs(b));
            return (Slider(Agreed(slight.Temp, full.Temp), 25), Slider(Agreed(slight.Tint, full.Tint), 12));
        }

        /// <summary>
        /// The photo as a detector needs to see it: at a normal brightness whatever its exposure, with the shadows open.
        /// </summary>
        internal static byte[] RenderForAnalysis(RawImage img)
        {
            var d = img.Data;
            int n = img.Width * img.Height;
            var hist = new int[4096];
            for (int i = 0; i < n * 3; i += 3) hist[(d[i] * 13933u + d[i + 1] * 46871u + d[i + 2] * 4732u) >> 20]++;
            int median = 0;
            for (int acc = 0; median < 4095 && (acc += hist[median]) < n / 2; median++) { }
            double y = (median + 0.5) / 4096 * (img.SceneReferred ? Math.Pow(2, BaseEv) : 1);
            var s = RawSettings.Default(false);
            s.Exposure = Math.Clamp(Math.Log2(0.18 / Math.Max(y, 1e-4)), -1, 4);   // median to middle grey
            s.Shadows = 30;
            return Render(img, s);
        }

        sealed class ToneStats
        {
            public double Median, Std, MeanSat, FracBright, FracDark, P999Max, P001Lum, P99Lum, P999Lum, P998Max;
            /// <summary>
            /// Share of the photo covered by large bright areas (a sky, a window), as opposed to bright points
            /// (lamps, lit windows at night), which disappear when the photo is looked at in coarse blocks.
            /// </summary>
            public double BrightArea;
            /// <summary>Brightness of the skin of the faces (see <see cref="SkinLevel"/>), or -1 when none is in the photo.</summary>
            public double FaceLum = -1;
        }

        /// <summary>The middle of a face box, in pixels of a w × h image: the rim is hair and background.</summary>
        internal static (int X0, int Y0, int X1, int Y1) FaceCore(FaceBox f, int w, int h) =>
            (Math.Max(0, (int)((f.X + f.W * 0.2) * w)), Math.Max(0, (int)((f.Y + f.H * 0.2) * h)),
             Math.Min(w - 1, (int)((f.X + f.W * 0.8) * w)), Math.Min(h - 1, (int)((f.Y + f.H * 0.8) * h)));

        /// <summary>
        /// The brightness of the skin in a histogram (256 tones) of the middle of the faces: the mean of its brighter
        /// half, since the darker one is eyes, mouth, beard and shadow. -1 for an empty histogram.
        /// </summary>
        internal static double SkinLevel(int[] tones)
        {
            long total = 0;
            foreach (int c in tones) total += c;
            if (total == 0) return -1;
            long half = (total + 1) / 2, taken = 0;
            double sum = 0;
            for (int i = 255; i >= 0 && taken < half; i--)
            {
                long take = Math.Min(tones[i], half - taken);
                sum += take * (double)i;
                taken += take;
            }
            return sum / half;
        }

        /// <summary>
        /// Renders the image with the given settings and measures the result (0..255 scale). Only what ends up in
        /// the finished photo counts: the pixels inside the crop.
        /// </summary>
        static ToneStats Measure(RawImage img, RawSettings s, IReadOnlyList<FaceBox> faces = null)
        {
            var t = s.Clone();
            t.Clarity = 0;
            t.Sharpening = 0;
            t.NoiseLuma = t.NoiseColor = 0;   // slow and irrelevant to the measure
            t.GrainAmount = 0;
            var px = Render(img, t);
            int w = img.Width, h = img.Height;
            var frame = s.HasGeometry ? new Frame(w, h, s) : null;
            var hl = new long[256];
            var hm = new long[256];
            long n = 0;
            double sum = 0, sum2 = 0, sat = 0;
            long bright = 0, dark = 0;
            int block = Math.Max(1, Math.Min(w, h) / 24), blocksWide = (w + block - 1) / block;
            var blockSum = new int[blocksWide * ((h + block - 1) / block)];
            var blockCount = new int[blockSum.Length];
            for (int y = 0, i = 0; y < h; y++)
                for (int x = 0; x < w; x++, i += 4)
                {
                    if (px[i + 3] < 128 || (frame != null && !frame.InCrop(x, y))) continue;
                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    int l = (int)(0.299 * r + 0.587 * g + 0.114 * b + 0.5);
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    hl[l]++;
                    hm[mx]++;
                    sum += l; sum2 += l * (double)l;
                    if (mx > 8) sat += (mx - mn) / (double)mx;
                    if (l >= 190) bright++;
                    if (l <= 60) dark++;
                    int cell = y / block * blocksWide + x / block;
                    blockSum[cell] += l;
                    blockCount[cell]++;
                    n++;
                }
            if (n == 0) return new ToneStats { Median = 128, P999Max = 255 };
            int blocks = 0, brightBlocks = 0;
            for (int c = 0; c < blockSum.Length; c++)
            {
                if (blockCount[c] * 2 < block * block) continue;
                blocks++;
                if (blockSum[c] >= 170 * blockCount[c]) brightBlocks++;
            }

            double faceLum = -1;
            if (faces != null && faces.Count > 0)
            {
                var tones = new int[256];
                foreach (var f in faces)
                {
                    var (x0, y0, x1, y1) = FaceCore(f, w, h);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = (y * w + x) * 4;
                            if (px[i + 3] < 128 || (frame != null && !frame.InCrop(x, y))) continue;
                            tones[(int)(0.299 * px[i + 2] + 0.587 * px[i + 1] + 0.114 * px[i] + 0.5)]++;
                        }
                }
                faceLum = SkinLevel(tones);
            }
            int Pct(long[] hist, double q)
            {
                long target = (long)(q * n), acc = 0;
                for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > target) return i; }
                return 255;
            }
            double mean = sum / n;
            return new ToneStats
            {
                Median = Pct(hl, 0.5),
                Std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean)) / 255,
                MeanSat = sat / n,
                FracBright = bright / (double)n,
                FracDark = dark / (double)n,
                BrightArea = blocks > 0 ? brightBlocks / (double)blocks : 0,
                P999Max = Pct(hm, 0.999),
                P001Lum = Pct(hl, 0.001),
                P99Lum = Pct(hl, 0.99),
                P999Lum = Pct(hl, 0.999),
                P998Max = Pct(hm, 0.998),
                FaceLum = faceLum,
            };
        }

        /// <summary>Finds the value of one setting (monotonic effect) that brings a measured quantity to a target.</summary>
        static double Solve(RawImage img, RawSettings s, Action<RawSettings, double> set, double lo, double hi,
                            Func<ToneStats, double> metric, double target, int iterations = 12, IReadOnlyList<FaceBox> faces = null)
        {
            for (int i = 0; i < iterations; i++)
            {
                double mid = (lo + hi) / 2;
                set(s, mid);
                if (metric(Measure(img, s, faces)) < target) lo = mid; else hi = mid;
            }
            double v = (lo + hi) / 2;
            set(s, v);
            return v;
        }

        /// <summary>What the automatic tone understood of a photo.</summary>
        public sealed class AutoToneInfo
        {
            /// <summary>0..1: how much the photo is dark on purpose (a night street, a stage, a low-key portrait).</summary>
            public double LowKey;
            /// <summary>The faces the exposure was weighed on.</summary>
            public int Faces;
        }

        /// <summary>
        /// Camera Raw style "Automatico": sets Exposure, Contrast, Highlights, Shadows, Whites, Blacks,
        /// Vibrance and Saturation by measuring the rendered result, like Adobe's Auto.
        /// White balance, clarity and sharpening are left unchanged (as in Camera Raw); the noise reduction is
        /// raised when the ISO or the added light call for it, never lowered.
        /// </summary>
        public static RawSettings AutoTone(RawImage img, RawSettings current) => AutoTone(img, current, out _);

        public static RawSettings AutoTone(RawImage img, RawSettings current, out AutoToneInfo info)
        {
            bool scene = img.SceneReferred;
            var faces = SceneAnalysis.Of(img).Faces;   // asked of the image as received: the detector wants more pixels than the measures
            var small = img.Downscale(320);
            var s = current.Clone();
            s.Exposure = s.Contrast = s.Highlights = s.Shadows = s.Whites = s.Blacks = s.Vibrance = s.Saturation = 0;
            double evLo = scene ? -4 : -2.5, evHi = scene ? 5 : 2.5;

            // Median brightness to aim for: dark photos are brought up to ~114/255 (where Adobe's Auto tends to land),
            // bright (high-key) photos keep most of their brightness instead of being darkened.
            double startMedian = Measure(small, s).Median, reference = startMedian, lowKey = 0;
            if (startMedian < 114)
            {
                // A dark photo is either underexposed or dark on purpose: a night street, a stage, a low-key portrait.
                // Exposed until its lights reach white, the first turns into a normal photo; the second stays dark, and
                // its lights are points, not areas (that would be a subject against the light, to be brightened).
                // The second kind keeps most of its darkness instead of being turned into daylight.
                Solve(small, s, (x, v) => x.Exposure = v, evLo, evHi, m => m.P99Lum, 235, 10);
                var lit = Measure(small, s);
                lowKey = (1 - SmoothStep(40, 85, lit.Median)) * (1 - SmoothStep(0.08, 0.22, lit.BrightArea));
                reference = startMedian + (Math.Min(lit.Median, 114) - startMedian) * lowKey;
                s.Exposure = 0;
            }
            double MidTarget = startMedian < 114
                ? 114 - (114 - reference) * (0.15 + 0.8 * lowKey)
                : 114 + (startMedian - 114) * 0.7;
            void SolveExposure() => Solve(small, s, (x, v) => x.Exposure = v, evLo, evHi, m => m.Median, MidTarget, 13);

            // 1) Exposure for the midtones.
            SolveExposure();
            info = new AutoToneInfo { LowKey = lowKey, Faces = faces.Count };

            // 2) Highlights down and shadows up, in proportion to how much of the image is bright or dark.
            //    In a photo that is dark on purpose the shadows are the photo: they are opened much less.
            var m1 = Measure(small, s);
            double hl = 20 + 180 * m1.FracBright + (m1.P999Max >= 254 ? 15 : 0);
            double sh = (10 + 160 * m1.FracDark) * (1 - 0.8 * lowKey);
            if (!scene) { hl *= 0.8; sh *= 0.8; }
            s.Highlights = -Math.Round(Math.Clamp(hl, 0, 100));
            s.Shadows = Math.Round(Math.Clamp(sh, 0, 80));

            //    Then the exposure again, for the faces: people are what gets looked at, and a background that fills
            //    the histogram must not leave them dark (or burnt). The larger the faces, the more they count.
            double faceWeight = 0.75 * SmoothStep(0.002, 0.03, faces.Sum(f => f.Area));
            if (faceWeight > 0.01)
            {
                double lum = Measure(small, s, faces).FaceLum, want = Math.Clamp(lum, 110, 195);   // wide: skin comes in every tone
                if (lum >= 0 && Math.Abs(want - lum) > 4)
                {
                    double before = s.Exposure;
                    double forFaces = Solve(small, s, (x, v) => x.Exposure = v, evLo, evHi, m => m.FaceLum, want, 10, faces);
                    s.Exposure = before + Math.Clamp(forFaces - before, -0.7, 1) * faceWeight;
                }
            }

            // 3) Contrast from the tonal spread. Exposure is not re-solved: like in Camera Raw,
            //    opening the shadows is meant to brighten the photo.
            var m2 = Measure(small, s);
            s.Contrast = Math.Round(Math.Clamp((0.21 - m2.Std) * 150, -15, 25));

            // 4) Whites and blacks: stretch the histogram until it just touches the edges.
            for (int round = 0; round < 2; round++)
            {
                // Whites: raise until the brightest tones reach white OR colours start clipping, whichever comes first.
                double wLo = scene ? -60 : -15;
                s.Whites = wLo;
                var atLo = Measure(small, s);
                double w;
                if (atLo.P999Lum >= 250)
                {
                    w = scene ? -10 : 0;   // truly blown areas (lamps, sun): the white point can't fix them
                }
                else
                {
                    double wLum = Solve(small, s, (x, v) => x.Whites = v, wLo, 60, m => m.P999Lum, 250);
                    double wClip = atLo.P998Max >= 254 ? wLum : Solve(small, s, (x, v) => x.Whites = v, wLo, 60, m => m.P998Max, 254);
                    w = Math.Min(wLum, wClip);
                }
                s.Whites = Math.Round(Math.Clamp(w, -40, 45));

                // Blacks: pull down until the darkest tones just touch black; never lift a photo that already has black.
                s.Blacks = 0;
                s.Blacks = Measure(small, s).P001Lum <= 6
                    ? 0
                    : Math.Round(Math.Clamp(Solve(small, s, (x, v) => x.Blacks = v, -80, 0, m => m.P001Lum, 6), -50, 0));
            }
            s.Exposure = Math.Round(s.Exposure * 20) / 20;
            if (Math.Abs(s.Exposure) < 0.05) s.Exposure = 0;

            // 5) Presence: vibrance for muted photos, a touch of saturation.
            var m3 = Measure(small, s);
            if (m3.MeanSat >= 0.04)
            {
                s.Vibrance = Math.Round(Math.Clamp(10 + (0.35 - m3.MeanSat) * 60, 0, 30));
                s.Saturation = Math.Round(Math.Clamp((0.30 - m3.MeanSat) * 15, -5, 5));
            }

            // 6) Noise: the light added here shows the grain that the exposure had hidden.
            var (luma, color) = AutoNoise(img, s);
            s.NoiseLuma = Math.Max(current.NoiseLuma, luma);
            s.NoiseColor = Math.Max(current.NoiseColor, color);
            return s;
        }

        /// <summary>
        /// Noise reduction (Luminanza, Colore) for the ISO of the photo and for the light that the settings add:
        /// a photo shot dark and brightened in development has the grain of a much higher ISO.
        /// </summary>
        public static (double Luma, double Color) AutoNoise(RawImage img, RawSettings s)
        {
            // What is not a RAW was already cleaned by the camera: only a strong push brings its noise back.
            double iso = !img.SceneReferred ? 50 : img.Iso > 0 ? img.Iso : 400;
            double masks = s.Masks == null ? 0 : s.Masks.Where(m => m != null && m.Enabled)
                .Select(m => m.Exposure * m.Amount / 100).DefaultIfEmpty(0).Max();
            double push = Math.Max(0, s.Exposure) + Math.Max(0, s.Shadows) / 100 + Math.Max(0, masks) / 2;
            double effective = iso * Math.Pow(2, push);
            double luma = Math.Clamp(11 * Math.Log2(effective / 640), 0, 55);
            double color = Math.Clamp(10 + 7 * Math.Log2(effective / 200), 0, 50);
            return (luma < 4 ? 0 : Math.Round(luma), color < 5 ? 0 : Math.Round(color));
        }

        /// <summary>
        /// "Miglioramento automatico": the automatic tone and, for a photo that is not a RAW, a cautious correction
        /// of the colour cast. A RAW keeps the white balance chosen by the camera, which measured the light of the scene.
        /// </summary>
        public static RawSettings AutoEnhance(RawImage img, RawSettings current)
        {
            var s = current.Clone();
            if (!img.SceneReferred && s.Temperature == 0 && s.Tint == 0)
                (s.Temperature, s.Tint) = AutoWhiteBalance(img, cautious: true);
            return AutoTone(img, s);
        }

        /// <summary>
        /// The values set by the automatic tools, taken only in part or pushed further:
        /// 0 = the settings before, 100 = the automatic ones, up to 150 = beyond them.
        /// </summary>
        public static RawSettings Blend(RawSettings before, RawSettings auto, double intensity)
        {
            double k = Math.Clamp(intensity, 0, 150) / 100;
            var r = before.Clone();
            void Mix(Func<RawSettings, double> get, Action<RawSettings, double> set, int decimals, double min, double max) =>
                set(r, Math.Clamp(Math.Round(get(before) + (get(auto) - get(before)) * k, decimals), min, max));
            Mix(x => x.Temperature, (x, v) => x.Temperature = v, 0, -100, 100);
            Mix(x => x.Tint, (x, v) => x.Tint = v, 0, -100, 100);
            Mix(x => x.Exposure, (x, v) => x.Exposure = v, 2, -5, 5);
            Mix(x => x.Contrast, (x, v) => x.Contrast = v, 0, -100, 100);
            Mix(x => x.Highlights, (x, v) => x.Highlights = v, 0, -100, 100);
            Mix(x => x.Shadows, (x, v) => x.Shadows = v, 0, -100, 100);
            Mix(x => x.Whites, (x, v) => x.Whites = v, 0, -100, 100);
            Mix(x => x.Blacks, (x, v) => x.Blacks = v, 0, -100, 100);
            Mix(x => x.Vibrance, (x, v) => x.Vibrance = v, 0, -100, 100);
            Mix(x => x.Saturation, (x, v) => x.Saturation = v, 0, -100, 100);
            Mix(x => x.NoiseLuma, (x, v) => x.NoiseLuma = v, 0, 0, 100);
            Mix(x => x.NoiseColor, (x, v) => x.NoiseColor = v, 0, 0, 100);
            return r;
        }

        /// <summary>
        /// The brightness of the developed photo, pixel by pixel (0..1, as displayed); -1 outside the crop and on
        /// transparent pixels. For the tools that decide from what the finished photo looks like.
        /// </summary>
        internal static float[] Brightness(RawImage img, RawSettings s)
        {
            var t = s.Clone();
            t.Clarity = 0;
            t.Sharpening = 0;
            t.NoiseLuma = t.NoiseColor = 0;
            t.GrainAmount = 0;
            var px = Render(img, t);
            int w = img.Width, h = img.Height;
            var frame = s.HasGeometry ? new Frame(w, h, s) : null;
            var lum = new float[w * h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0, p = y * w; x < w; x++, p++)
                {
                    int i = p * 4;
                    lum[p] = px[i + 3] < 128 || (frame != null && !frame.InCrop(x, y))
                        ? -1
                        : (0.2126f * px[i + 2] + 0.7152f * px[i + 1] + 0.0722f * px[i]) / 255f;
                }
            });
            return lum;
        }
    }
}
