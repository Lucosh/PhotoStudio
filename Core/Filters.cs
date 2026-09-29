using System;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>Spatial filters. All functions return a new buffer and keep the source untouched.</summary>
    public static class Filters
    {
        public static byte[] GaussianBlur(byte[] s, int w, int h, double radius) => ImageOps.BlurBgra(s, w, h, radius);

        public static byte[] UnsharpMask(byte[] s, int w, int h, double amount, double radius, double threshold)
        {
            var bl = ImageOps.BlurBgra(s, w, h, radius);
            var d = new byte[s.Length];
            double a = amount / 100;
            Parallel.For(0, h, y =>
            {
                int start = y * w * 4, end = start + w * 4;
                for (int i = start; i < end; i += 4)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int diff = s[i + c] - bl[i + c];
                        d[i + c] = Math.Abs(diff) >= threshold ? ImageOps.ClampByte(s[i + c] + diff * a) : s[i + c];
                    }
                    d[i + 3] = s[i + 3];
                }
            });
            return d;
        }

        /// <summary>Local contrast boost on midtones (large-radius unsharp mask).</summary>
        public static byte[] Clarity(byte[] s, int w, int h, double amount)
        {
            double radius = Math.Max(4, Math.Min(w, h) / 60.0);
            var bl = ImageOps.BlurBgra(s, w, h, radius);
            var d = new byte[s.Length];
            double a = amount / 100 * 0.8;
            Parallel.For(0, h, y =>
            {
                int start = y * w * 4, end = start + w * 4;
                for (int i = start; i < end; i += 4)
                {
                    double l = ImageOps.Luma(s[i + 2], s[i + 1], s[i]) / 255;
                    double mid = 4 * l * (1 - l);
                    for (int c = 0; c < 3; c++)
                        d[i + c] = ImageOps.ClampByte(s[i + c] + (s[i + c] - bl[i + c]) * a * mid);
                    d[i + 3] = s[i + 3];
                }
            });
            return d;
        }

        static byte[] Convolve3(byte[] s, int w, int h, double[] k, double bias, bool gray)
        {
            var d = new byte[s.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double sb = 0, sg = 0, sr = 0;
                    for (int j = -1; j <= 1; j++)
                    {
                        int yy = Math.Clamp(y + j, 0, h - 1);
                        for (int i = -1; i <= 1; i++)
                        {
                            int xx = Math.Clamp(x + i, 0, w - 1);
                            int si = (yy * w + xx) * 4;
                            double kv = k[(j + 1) * 3 + (i + 1)];
                            sb += s[si] * kv; sg += s[si + 1] * kv; sr += s[si + 2] * kv;
                        }
                    }
                    int o = (y * w + x) * 4;
                    if (gray)
                    {
                        byte v = ImageOps.ClampByte(ImageOps.Luma(sr, sg, sb) + bias);
                        d[o] = d[o + 1] = d[o + 2] = v;
                    }
                    else
                    {
                        d[o] = ImageOps.ClampByte(sb + bias);
                        d[o + 1] = ImageOps.ClampByte(sg + bias);
                        d[o + 2] = ImageOps.ClampByte(sr + bias);
                    }
                    d[o + 3] = s[o + 3];
                }
            });
            return d;
        }

        public static byte[] Sharpen(byte[] s, int w, int h)
        {
            const double a = 0.5;
            return Convolve3(s, w, h, new[] { 0, -a, 0, -a, 1 + 4 * a, -a, 0, -a, 0 }, 0, false);
        }

        public static byte[] Emboss(byte[] s, int w, int h, double amount)
        {
            double a = amount / 100;
            return Convolve3(s, w, h, new[] { -a, -a, 0, -a, 0, a, 0, a, a }, 128, true);
        }

        public static byte[] FindEdges(byte[] s, int w, int h)
        {
            var d = new byte[s.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        double gx = 0, gy = 0;
                        for (int j = -1; j <= 1; j++)
                        {
                            int yy = Math.Clamp(y + j, 0, h - 1);
                            for (int i = -1; i <= 1; i++)
                            {
                                int xx = Math.Clamp(x + i, 0, w - 1);
                                int v = s[(yy * w + xx) * 4 + c];
                                int wx = i * (j == 0 ? 2 : 1), wy = j * (i == 0 ? 2 : 1);
                                gx += v * wx; gy += v * wy;
                            }
                        }
                        d[o + c] = ImageOps.ClampByte(255 - Math.Sqrt(gx * gx + gy * gy));
                    }
                    d[o + 3] = s[o + 3];
                }
            });
            return d;
        }

        public static byte[] AddNoise(byte[] s, int w, int h, double amount, bool mono)
        {
            var d = new byte[s.Length];
            double a = amount * 2.55;
            Parallel.For(0, h, y =>
            {
                var rnd = new Random(y * 7919 + 13);
                int start = y * w * 4, end = start + w * 4;
                for (int i = start; i < end; i += 4)
                {
                    double n = (rnd.NextDouble() + rnd.NextDouble() - 1) * a;
                    for (int c = 0; c < 3; c++)
                    {
                        if (!mono && c > 0) n = (rnd.NextDouble() + rnd.NextDouble() - 1) * a;
                        d[i + c] = ImageOps.ClampByte(s[i + c] + n);
                    }
                    d[i + 3] = s[i + 3];
                }
            });
            return d;
        }

        public static byte[] Pixelate(byte[] s, int w, int h, int cell)
        {
            cell = Math.Max(1, cell);
            var d = new byte[s.Length];
            int rows = (h + cell - 1) / cell;
            Parallel.For(0, rows, by =>
            {
                int y0 = by * cell, y1 = Math.Min(h, y0 + cell);
                for (int x0 = 0; x0 < w; x0 += cell)
                {
                    int x1 = Math.Min(w, x0 + cell);
                    long sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int i = (y * w + x) * 4;
                            int a = s[i + 3];
                            sb += s[i] * a; sg += s[i + 1] * a; sr += s[i + 2] * a; sa += a; n++;
                        }
                    byte b = 0, g = 0, r = 0, al = (byte)(sa / n);
                    if (sa > 0) { b = (byte)(sb / sa); g = (byte)(sg / sa); r = (byte)(sr / sa); }
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int i = (y * w + x) * 4;
                            d[i] = b; d[i + 1] = g; d[i + 2] = r; d[i + 3] = al;
                        }
                }
            });
            return d;
        }

        public static byte[] Vignette(byte[] s, int w, int h, double amount, double size, double feather)
        {
            var d = new byte[s.Length];
            double a = amount / 100;
            double inner = size / 100 * 1.1;
            double outer = inner + Math.Max(0.05, feather / 100 * 1.2);
            Parallel.For(0, h, y =>
            {
                double ny = (y + 0.5) / h * 2 - 1;
                for (int x = 0; x < w; x++)
                {
                    double nx = (x + 0.5) / w * 2 - 1;
                    double r = Math.Sqrt(nx * nx + ny * ny);
                    double t = Math.Clamp((r - inner) / (outer - inner), 0, 1);
                    t = t * t * (3 - 2 * t);
                    int i = (y * w + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        double v = s[i + c];
                        v = a < 0 ? v * (1 + a * t) : v + (255 - v) * a * t;
                        d[i + c] = ImageOps.ClampByte(v);
                    }
                    d[i + 3] = s[i + 3];
                }
            });
            return d;
        }

        public static byte[] MotionBlur(byte[] s, int w, int h, double angleDeg, double distance)
        {
            var p = (byte[])s.Clone();
            ImageOps.Premultiply(p);
            var d = new byte[s.Length];
            double ang = angleDeg * Math.PI / 180;
            double dx = Math.Cos(ang), dy = -Math.Sin(ang);
            int samples = Math.Clamp((int)Math.Ceiling(distance), 1, 96);
            double step = distance / samples;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double sb = 0, sg = 0, sr = 0, sa = 0;
                    for (int k = 0; k <= samples; k++)
                    {
                        double t = -distance / 2 + k * step;
                        int xx = Math.Clamp((int)Math.Round(x + dx * t), 0, w - 1);
                        int yy = Math.Clamp((int)Math.Round(y + dy * t), 0, h - 1);
                        int i = (yy * w + xx) * 4;
                        sb += p[i]; sg += p[i + 1]; sr += p[i + 2]; sa += p[i + 3];
                    }
                    int n = samples + 1, o = (y * w + x) * 4;
                    d[o] = (byte)(sb / n); d[o + 1] = (byte)(sg / n); d[o + 2] = (byte)(sr / n); d[o + 3] = (byte)(sa / n);
                }
            });
            ImageOps.Unpremultiply(d);
            return d;
        }
    }
}
