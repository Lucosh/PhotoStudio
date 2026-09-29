using System;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>Point (per-pixel) color adjustments. All functions return a new buffer.</summary>
    public static class Adjustments
    {
        public delegate void PixelOp(ref double r, ref double g, ref double b);

        const int Chunk = 16384;

        public static byte[] Map(byte[] src, PixelOp op)
        {
            var d = new byte[src.Length];
            Parallel.For(0, (src.Length / 4 + Chunk - 1) / Chunk, c =>
            {
                int s = c * Chunk * 4, e = Math.Min(src.Length, s + Chunk * 4);
                for (int i = s; i < e; i += 4)
                {
                    double b = src[i] / 255.0, g = src[i + 1] / 255.0, r = src[i + 2] / 255.0;
                    op(ref r, ref g, ref b);
                    d[i] = ImageOps.ClampByte(b * 255);
                    d[i + 1] = ImageOps.ClampByte(g * 255);
                    d[i + 2] = ImageOps.ClampByte(r * 255);
                    d[i + 3] = src[i + 3];
                }
            });
            return d;
        }

        public static byte[] ApplyLut(byte[] src, byte[] lr, byte[] lg, byte[] lb)
        {
            var d = new byte[src.Length];
            Parallel.For(0, (src.Length / 4 + Chunk - 1) / Chunk, c =>
            {
                int s = c * Chunk * 4, e = Math.Min(src.Length, s + Chunk * 4);
                for (int i = s; i < e; i += 4)
                {
                    d[i] = lb[src[i]]; d[i + 1] = lg[src[i + 1]]; d[i + 2] = lr[src[i + 2]]; d[i + 3] = src[i + 3];
                }
            });
            return d;
        }

        public static byte[] Lut(Func<double, double> f)
        {
            var l = new byte[256];
            for (int i = 0; i < 256; i++) l[i] = ImageOps.ClampByte(f(i / 255.0) * 255);
            return l;
        }

        static byte[] ApplyLut(byte[] src, byte[] lut) => ApplyLut(src, lut, lut, lut);

        public static byte[] BrightnessContrast(byte[] s, double brightness, double contrast)
        {
            double cf = contrast >= 0 ? 1 + contrast / 100.0 * 2 : 1 + contrast / 100.0;
            double gamma = Math.Pow(2, -brightness / 100.0 * 1.3);
            return ApplyLut(s, Lut(v => (Math.Pow(v, gamma) - 0.5) * cf + 0.5));
        }

        public static byte[] Levels(byte[] s, double inBlack, double inWhite, double gamma, double outBlack, double outWhite)
        {
            return ApplyLut(s, Lut(v =>
            {
                double t = Math.Clamp((v * 255 - inBlack) / Math.Max(1, inWhite - inBlack), 0, 1);
                t = Math.Pow(t, 1 / Math.Max(0.01, gamma));
                return (outBlack + t * (outWhite - outBlack)) / 255;
            }));
        }

        public static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        public static double LinearToSrgb(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

        public static byte[] Exposure(byte[] s, double ev, double offset, double gamma)
        {
            double k = Math.Pow(2, ev);
            return ApplyLut(s, Lut(v =>
            {
                double lin = SrgbToLinear(v) * k + offset;
                double o = LinearToSrgb(Math.Max(0, lin));
                return Math.Pow(Math.Clamp(o, 0, 1), 1 / Math.Max(0.01, gamma));
            }));
        }

        public static byte[] HueSaturation(byte[] s, double hue, double sat, double light)
        {
            double hs = hue / 360.0, sf = 1 + sat / 100.0, lf = light / 100.0;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                RgbToHsl(r, g, b, out double h, out double sa, out double l);
                h += hs; if (h < 0) h += 1; if (h >= 1) h -= 1;
                sa = Math.Clamp(sa * sf, 0, 1);
                l = lf >= 0 ? l + (1 - l) * lf : l * (1 + lf);
                HslToRgb(h, sa, l, out r, out g, out b);
            });
        }

        public static byte[] Vibrance(byte[] s, double vibrance, double saturation)
        {
            double vf = vibrance / 100.0, sf = saturation / 100.0;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                double cur = mx - mn;
                double factor = 1 + sf + vf * (1 - cur) * (1 - cur) * 1.5;
                if (factor < 0) factor = 0;
                double y = ImageOps.Luma(r, g, b);
                r = y + (r - y) * factor; g = y + (g - y) * factor; b = y + (b - y) * factor;
            });
        }

        public static byte[] ColorBalance(byte[] s, double cyanRed, double magentaGreen, double yellowBlue, bool preserveLum)
        {
            double cr = cyanRed / 100 * 0.35, mg = magentaGreen / 100 * 0.35, yb = yellowBlue / 100 * 0.35;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                double l0 = ImageOps.Luma(r, g, b);
                r += cr * 4 * r * (1 - r) + cr * 0.15;
                g += mg * 4 * g * (1 - g) + mg * 0.15;
                b += yb * 4 * b * (1 - b) + yb * 0.15;
                if (preserveLum)
                {
                    double d = l0 - ImageOps.Luma(r, g, b);
                    r += d; g += d; b += d;
                }
            });
        }

        public static byte[] Temperature(byte[] s, double temperature, double tint)
        {
            double t = temperature / 100.0, n = tint / 100.0;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                r *= 1 + t * 0.25;
                b *= 1 - t * 0.25;
                g *= 1 - n * 0.2;
            });
        }

        public static byte[] BlackWhite(byte[] s, double rw, double gw, double bw)
        {
            double kr = rw / 100, kg = gw / 100, kb = bw / 100;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                double v = r * kr + g * kg + b * kb;
                r = g = b = v;
            });
        }

        public static byte[] Threshold(byte[] s, double level)
        {
            double t = level / 255.0;
            return Map(s, (ref double r, ref double g, ref double b) =>
            {
                double v = ImageOps.Luma(r, g, b) >= t ? 1 : 0;
                r = g = b = v;
            });
        }

        public static byte[] Posterize(byte[] s, double levels)
        {
            int n = Math.Max(2, (int)levels);
            return ApplyLut(s, Lut(v => Math.Round(v * (n - 1)) / (n - 1)));
        }

        public static byte[] Invert(byte[] s) => ApplyLut(s, Lut(v => 1 - v));

        public static byte[] Desaturate(byte[] s) => Map(s, (ref double r, ref double g, ref double b) =>
        {
            double v = ImageOps.Luma(r, g, b);
            r = g = b = v;
        });

        public static byte[] Sepia(byte[] s) => Map(s, (ref double r, ref double g, ref double b) =>
        {
            double nr = 0.393 * r + 0.769 * g + 0.189 * b;
            double ng = 0.349 * r + 0.686 * g + 0.168 * b;
            double nb = 0.272 * r + 0.534 * g + 0.131 * b;
            r = nr; g = ng; b = nb;
        });

        /// <summary>Stretches each channel so that 0.1% of pixels clip at each end.</summary>
        public static byte[] AutoLevels(byte[] s, bool perChannel)
        {
            var hist = new int[4, 256];
            long count = 0;
            for (int i = 0; i < s.Length; i += 4)
            {
                if (s[i + 3] == 0) continue;
                hist[0, s[i + 2]]++; hist[1, s[i + 1]]++; hist[2, s[i]]++;
                hist[3, (s[i + 2] * 77 + s[i + 1] * 150 + s[i] * 29) >> 8]++;
                count++;
            }
            if (count == 0) return (byte[])s.Clone();
            long clip = Math.Max(1, count / 1000);
            var luts = new byte[3][];
            for (int c = 0; c < 3; c++)
            {
                int src = perChannel ? c : 3;
                int lo = 0, hi = 255;
                long acc = 0;
                while (lo < 255 && (acc += hist[src, lo]) <= clip) lo++;
                acc = 0;
                while (hi > 0 && (acc += hist[src, hi]) <= clip) hi--;
                if (hi <= lo) { lo = 0; hi = 255; }
                int l0 = lo, h0 = hi;
                luts[c] = Lut(v => (v * 255 - l0) / (h0 - l0));
            }
            return ApplyLut(s, luts[0], luts[1], luts[2]);
        }

        /// <summary>
        /// Photoshop "Colore automatico": Find Dark &amp; Light Colors (the average of the darkest / lightest 0.1%
        /// become neutral black / white) followed by Snap Neutral Midtones (near-grey midtones become neutral).
        /// </summary>
        public static byte[] AutoColor(byte[] s)
        {
            int n = s.Length / 4;
            int step = Math.Max(1, n / 400000);
            var hist = new long[256];
            long count = 0;
            for (int p = 0; p < n; p += step)
            {
                int i = p * 4;
                if (s[i + 3] < 128) continue;
                hist[(s[i + 2] * 77 + s[i + 1] * 150 + s[i] * 29) >> 8]++;
                count++;
            }
            if (count == 0) return (byte[])s.Clone();
            long clip = Math.Max(1, count / 1000);
            int loThr = 0, hiThr = 255;
            for (long acc = 0; loThr < 255 && (acc += hist[loThr]) < clip; loThr++) { }
            for (long acc = 0; hiThr > 0 && (acc += hist[hiThr]) < clip; hiThr--) { }

            double[] dark = new double[3], light = new double[3];
            long nd = 0, nl = 0;
            for (int p = 0; p < n; p += step)
            {
                int i = p * 4;
                if (s[i + 3] < 128) continue;
                int l = (s[i + 2] * 77 + s[i + 1] * 150 + s[i] * 29) >> 8;
                if (l <= loThr) { dark[0] += s[i + 2]; dark[1] += s[i + 1]; dark[2] += s[i]; nd++; }
                if (l >= hiThr) { light[0] += s[i + 2]; light[1] += s[i + 1]; light[2] += s[i]; nl++; }
            }
            var luts = new byte[3][];
            for (int c = 0; c < 3; c++)
            {
                double lo = nd > 0 ? dark[c] / nd : 0, hi = nl > 0 ? light[c] / nl : 255;
                if (hi - lo < 16) { lo = 0; hi = 255; }
                double l0 = lo, h0 = hi;
                luts[c] = Lut(v => (v * 255 - l0) / (h0 - l0));
            }

            // Snap neutral midtones: average the near-grey midtones after the levels step, then neutralise them with gamma.
            double[] mid = new double[3];
            long nm = 0;
            for (int p = 0; p < n; p += step)
            {
                int i = p * 4;
                if (s[i + 3] < 128) continue;
                int r = luts[0][s[i + 2]], g = luts[1][s[i + 1]], b = luts[2][s[i]];
                int l = (r * 77 + g * 150 + b * 29) >> 8;
                int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                if (l < 50 || l > 205 || mx - mn > 30) continue;
                mid[0] += r; mid[1] += g; mid[2] += b; nm++;
            }
            if (nm > count / 200)
            {
                double target = (mid[0] + mid[1] + mid[2]) / (3.0 * nm) / 255;
                for (int c = 0; c < 3; c++)
                {
                    double m = mid[c] / nm / 255;
                    if (m <= 0.02 || m >= 0.98) continue;
                    double gamma = Math.Clamp(Math.Log(target) / Math.Log(m), 0.7, 1.4);
                    var baseLut = luts[c];
                    luts[c] = new byte[256];
                    for (int v = 0; v < 256; v++) luts[c][v] = ImageOps.ClampByte(Math.Pow(baseLut[v] / 255.0, gamma) * 255);
                }
            }
            return ApplyLut(s, luts[0], luts[1], luts[2]);
        }

        public static byte[] ShadowsHighlights(byte[] s, int w, int h, double shadows, double highlights, double radius)
        {
            var lum = new byte[w * h];
            for (int i = 0, p = 0; p < lum.Length; i += 4, p++)
                lum[p] = (byte)((s[i + 2] * 77 + s[i + 1] * 150 + s[i] * 29) >> 8);
            ImageOps.GaussianBlur(lum, w, h, 1, radius);
            double sa = shadows / 100.0, ha = highlights / 100.0;
            var d = new byte[s.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x, i = p * 4;
                    double lb = lum[p] / 255.0;
                    double sw = (1 - lb) * (1 - lb), hw = lb * lb;
                    double exp = Math.Pow(2, -(sa * sw - ha * hw) * 1.2);
                    for (int c = 0; c < 3; c++) d[i + c] = ImageOps.ClampByte(Math.Pow(s[i + c] / 255.0, exp) * 255);
                    d[i + 3] = s[i + 3];
                }
            });
            return d;
        }

        public static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) { h = s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        public static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s == 0) { r = g = b = l; return; }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
