using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace PhotoStudio.Core
{
    /// <summary>Low level operations on non-premultiplied BGRA buffers.</summary>
    public static class ImageOps
    {
        public static byte ClampByte(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5);

        public static double Luma(double r, double g, double b) => 0.299 * r + 0.587 * g + 0.114 * b;

        public static bool IsEmpty(Int32Rect r) => r.Width <= 0 || r.Height <= 0;

        public static Int32Rect Intersect(Int32Rect a, Int32Rect b)
        {
            int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
            int x1 = Math.Min(a.X + a.Width, b.X + b.Width), y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            return x1 > x0 && y1 > y0 ? new Int32Rect(x0, y0, x1 - x0, y1 - y0) : new Int32Rect(0, 0, 0, 0);
        }

        public static Int32Rect Union(Int32Rect a, Int32Rect b)
        {
            if (IsEmpty(a)) return b;
            if (IsEmpty(b)) return a;
            int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
            int x1 = Math.Max(a.X + a.Width, b.X + b.Width), y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new Int32Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Composites a color with alpha a (0..1) over the pixel at byte index i.</summary>
        public static void Over(byte[] p, int i, byte b, byte g, byte r, double a)
        {
            if (a <= 0) return;
            if (a > 1) a = 1;
            double ab = p[i + 3] / 255.0;
            double ao = a + ab * (1 - a);
            if (ao <= 0) { p[i] = p[i + 1] = p[i + 2] = p[i + 3] = 0; return; }
            double k = ab * (1 - a);
            p[i] = (byte)((b * a + p[i] * k) / ao + 0.5);
            p[i + 1] = (byte)((g * a + p[i + 1] * k) / ao + 0.5);
            p[i + 2] = (byte)((r * a + p[i + 2] * k) / ao + 0.5);
            p[i + 3] = (byte)(ao * 255 + 0.5);
        }

        /// <summary>Blends 'processed' back towards 'original' outside the selection. Modifies and returns processed.</summary>
        public static byte[] ApplyMask(byte[] original, byte[] processed, Selection sel)
        {
            if (sel == null) return processed;
            int w = sel.Width, h = sel.Height;
            var mask = sel.Mask;
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int m = mask[row + x];
                    if (m == 255) continue;
                    int i = (row + x) * 4;
                    if (m == 0)
                    {
                        processed[i] = original[i]; processed[i + 1] = original[i + 1];
                        processed[i + 2] = original[i + 2]; processed[i + 3] = original[i + 3];
                        continue;
                    }
                    for (int c = 0; c < 4; c++)
                        processed[i + c] = (byte)((original[i + c] * (255 - m) + processed[i + c] * m + 127) / 255);
                }
            });
            return processed;
        }

        /// <summary>Source-over composite of src onto dst (both w*h BGRA).</summary>
        public static void BlendOver(byte[] dst, byte[] src, int w, int h)
        {
            int rowBytes = w * 4;
            Parallel.For(0, h, y => Document.BlendSpan(dst, src, y * rowBytes, (y + 1) * rowBytes, 1.0, BlendMode.Normal));
        }

        public static bool HasAlpha(byte[] p)
        {
            for (int i = 3; i < p.Length; i += 4) if (p[i] != 255) return true;
            return false;
        }

        public static void Premultiply(byte[] p)
        {
            Parallel.For(0, (p.Length + 65535) / 65536, c =>
            {
                int s = c * 65536, e = Math.Min(p.Length, s + 65536);
                for (int i = s; i < e; i += 4)
                {
                    int a = p[i + 3];
                    if (a == 255) continue;
                    p[i] = (byte)((p[i] * a + 127) / 255);
                    p[i + 1] = (byte)((p[i + 1] * a + 127) / 255);
                    p[i + 2] = (byte)((p[i + 2] * a + 127) / 255);
                }
            });
        }

        public static void Unpremultiply(byte[] p)
        {
            Parallel.For(0, (p.Length + 65535) / 65536, c =>
            {
                int s = c * 65536, e = Math.Min(p.Length, s + 65536);
                for (int i = s; i < e; i += 4)
                {
                    int a = p[i + 3];
                    if (a == 255) continue;
                    if (a == 0) { p[i] = p[i + 1] = p[i + 2] = 0; continue; }
                    p[i] = (byte)Math.Min(255, (p[i] * 255 + a / 2) / a);
                    p[i + 1] = (byte)Math.Min(255, (p[i + 1] * 255 + a / 2) / a);
                    p[i + 2] = (byte)Math.Min(255, (p[i + 2] * 255 + a / 2) / a);
                }
            });
        }

        // ---------- Geometry ----------

        public static byte[] FlipH(byte[] src, int w, int h)
        {
            var d = new byte[src.Length];
            Parallel.For(0, h, y =>
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++) Buffer.BlockCopy(src, row + x * 4, d, row + (w - 1 - x) * 4, 4);
            });
            return d;
        }

        public static byte[] FlipV(byte[] src, int w, int h)
        {
            var d = new byte[src.Length];
            int rb = w * 4;
            Parallel.For(0, h, y => Buffer.BlockCopy(src, y * rb, d, (h - 1 - y) * rb, rb));
            return d;
        }

        public static byte[] Rotate180(byte[] src, int w, int h) => FlipV(FlipH(src, w, h), w, h);

        /// <summary>Rotates by 90 degrees; the result is h x w.</summary>
        public static byte[] Rotate90(byte[] src, int w, int h, bool clockwise)
        {
            var d = new byte[src.Length];
            int nw = h;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int di = clockwise ? (x * nw + (h - 1 - y)) : ((w - 1 - x) * nw + y);
                    Buffer.BlockCopy(src, (y * w + x) * 4, d, di * 4, 4);
                }
            });
            return d;
        }

        /// <summary>Extracts rectangle r (may extend outside the image: that area becomes transparent).</summary>
        public static byte[] Crop(byte[] src, int w, int h, Int32Rect r)
        {
            var d = new byte[r.Width * r.Height * 4];
            var inter = Intersect(r, new Int32Rect(0, 0, w, h));
            if (IsEmpty(inter)) return d;
            int len = inter.Width * 4;
            Parallel.For(inter.Y, inter.Y + inter.Height, y =>
                Buffer.BlockCopy(src, (y * w + inter.X) * 4, d, ((y - r.Y) * r.Width + (inter.X - r.X)) * 4, len));
            return d;
        }

        /// <summary>Writes src shifted by (dx,dy) into dst (cleared first).</summary>
        public static void Offset(byte[] src, byte[] dst, int w, int h, int dx, int dy)
        {
            Array.Clear(dst, 0, dst.Length);
            int x0 = Math.Max(0, dx), x1 = Math.Min(w, w + dx);
            if (x1 <= x0) return;
            int len = (x1 - x0) * 4;
            Parallel.For(Math.Max(0, dy), Math.Min(h, h + dy), y =>
                Buffer.BlockCopy(src, ((y - dy) * w + (x0 - dx)) * 4, dst, (y * w + x0) * 4, len));
        }

        /// <summary>Scales/rotates/translates a layer around the canvas centre (bilinear), keeping canvas size.</summary>
        public static byte[] TransformLayer(byte[] src, int w, int h, double scale, double angleDeg, double tx, double ty)
        {
            var s = (byte[])src.Clone();
            Premultiply(s);
            var d = new byte[src.Length];
            double a = -angleDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            double cx = w / 2.0, cy = h / 2.0, inv = 1 / Math.Max(0.001, scale);
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double px = x + 0.5 - cx - tx, py = y + 0.5 - cy - ty;
                    double sx = (px * cos - py * sin) * inv + cx - 0.5;
                    double sy = (px * sin + py * cos) * inv + cy - 0.5;
                    SampleBilinear(s, w, h, sx, sy, d, (y * w + x) * 4);
                }
            });
            Unpremultiply(d);
            return d;
        }

        static void SampleBilinear(byte[] s, int w, int h, double sx, double sy, byte[] d, int di)
        {
            if (sx < -1 || sy < -1 || sx > w || sy > h) return;
            int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
            double fx = sx - x0, fy = sy - y0;
            double b = 0, g = 0, r = 0, al = 0;
            for (int j = 0; j < 2; j++)
            {
                int yy = y0 + j;
                if (yy < 0 || yy >= h) continue;
                double wy = j == 0 ? 1 - fy : fy;
                for (int i = 0; i < 2; i++)
                {
                    int xx = x0 + i;
                    if (xx < 0 || xx >= w) continue;
                    double wt = wy * (i == 0 ? 1 - fx : fx);
                    int si = (yy * w + xx) * 4;
                    b += s[si] * wt; g += s[si + 1] * wt; r += s[si + 2] * wt; al += s[si + 3] * wt;
                }
            }
            d[di] = ClampByte(b); d[di + 1] = ClampByte(g); d[di + 2] = ClampByte(r); d[di + 3] = ClampByte(al);
        }

        /// <summary>High quality resample (triangle filter, area-correct when downscaling).</summary>
        public static byte[] Resize(byte[] src, int w, int h, int nw, int nh)
        {
            var s = (byte[])src.Clone();
            bool alpha = HasAlpha(s);
            if (alpha) Premultiply(s);
            var (hi, hw) = Weights(w, nw);
            var (vi, vw) = Weights(h, nh);

            var tmp = new byte[nw * h * 4];
            Parallel.For(0, h, y =>
            {
                int srow = y * w * 4, drow = y * nw * 4;
                for (int x = 0; x < nw; x++)
                {
                    var idx = hi[x]; var wt = hw[x];
                    double b = 0, g = 0, r = 0, a = 0;
                    for (int k = 0; k < idx.Length; k++)
                    {
                        int i = srow + idx[k] * 4; double f = wt[k];
                        b += s[i] * f; g += s[i + 1] * f; r += s[i + 2] * f; a += s[i + 3] * f;
                    }
                    int o = drow + x * 4;
                    tmp[o] = ClampByte(b); tmp[o + 1] = ClampByte(g); tmp[o + 2] = ClampByte(r); tmp[o + 3] = ClampByte(a);
                }
            });

            var dst = new byte[nw * nh * 4];
            Parallel.For(0, nh, y =>
            {
                var idx = vi[y]; var wt = vw[y];
                int drow = y * nw * 4;
                for (int x = 0; x < nw; x++)
                {
                    double b = 0, g = 0, r = 0, a = 0;
                    for (int k = 0; k < idx.Length; k++)
                    {
                        int i = (idx[k] * nw + x) * 4; double f = wt[k];
                        b += tmp[i] * f; g += tmp[i + 1] * f; r += tmp[i + 2] * f; a += tmp[i + 3] * f;
                    }
                    int o = drow + x * 4;
                    dst[o] = ClampByte(b); dst[o + 1] = ClampByte(g); dst[o + 2] = ClampByte(r); dst[o + 3] = ClampByte(a);
                }
            });
            if (alpha) Unpremultiply(dst);
            return dst;
        }

        static (int[][], double[][]) Weights(int srcSize, int dstSize)
        {
            double scale = (double)srcSize / dstSize;
            double support = Math.Max(1.0, scale);
            var idx = new int[dstSize][];
            var wts = new double[dstSize][];
            for (int o = 0; o < dstSize; o++)
            {
                double center = (o + 0.5) * scale - 0.5;
                int lo = (int)Math.Ceiling(center - support), hi = (int)Math.Floor(center + support);
                var li = new List<int>();
                var lw = new List<double>();
                double sum = 0;
                for (int i = lo; i <= hi; i++)
                {
                    double wgt = 1 - Math.Abs(i - center) / support;
                    if (wgt <= 0) continue;
                    li.Add(Math.Clamp(i, 0, srcSize - 1));
                    lw.Add(wgt);
                    sum += wgt;
                }
                if (li.Count == 0)
                {
                    li.Add(Math.Clamp((int)Math.Round(center), 0, srcSize - 1));
                    lw.Add(1);
                    sum = 1;
                }
                idx[o] = li.ToArray();
                var arr = lw.ToArray();
                for (int k = 0; k < arr.Length; k++) arr[k] /= sum;
                wts[o] = arr;
            }
            return (idx, wts);
        }

        // ---------- Blur ----------

        public static byte[] BlurBgra(byte[] src, int w, int h, double sigma)
        {
            var d = (byte[])src.Clone();
            bool alpha = HasAlpha(d);
            if (alpha) Premultiply(d);
            GaussianBlur(d, w, h, 4, sigma);
            if (alpha) Unpremultiply(d);
            return d;
        }

        /// <summary>In-place gaussian blur approximation (3 box passes) on an interleaved buffer.</summary>
        public static void GaussianBlur(byte[] data, int w, int h, int ch, double sigma)
        {
            if (sigma < 0.3) return;
            var tmp = new byte[data.Length];
            foreach (int size in BoxesForGauss(sigma, 3))
            {
                int r = (size - 1) / 2;
                if (r < 1) continue;
                BoxH(data, tmp, w, h, ch, r);
                BoxV(tmp, data, w, h, ch, r);
            }
        }

        static int[] BoxesForGauss(double sigma, int n)
        {
            double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(wIdeal);
            if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            var sizes = new int[n];
            for (int i = 0; i < n; i++) sizes[i] = i < m ? wl : wu;
            return sizes;
        }

        static void BoxH(byte[] src, byte[] dst, int w, int h, int ch, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            Parallel.For(0, h, y =>
            {
                int row = y * w * ch;
                for (int c = 0; c < ch; c++)
                {
                    int s = 0;
                    for (int k = -r; k <= r; k++) s += src[row + Math.Clamp(k, 0, w - 1) * ch + c];
                    for (int x = 0; x < w; x++)
                    {
                        dst[row + x * ch + c] = (byte)(s * inv + 0.5);
                        int add = Math.Min(x + r + 1, w - 1), rem = Math.Max(x - r, 0);
                        s += src[row + add * ch + c] - src[row + rem * ch + c];
                    }
                }
            });
        }

        static void BoxV(byte[] src, byte[] dst, int w, int h, int ch, int r)
        {
            double inv = 1.0 / (2 * r + 1);
            int stride = w * ch;
            const int block = 64;
            int blocks = (w + block - 1) / block;
            Parallel.For(0, blocks, b =>
            {
                int x0 = b * block * ch, x1 = Math.Min(w, (b + 1) * block) * ch;
                int n = x1 - x0;
                var sums = new int[n];
                for (int k = -r; k <= r; k++)
                {
                    int row = Math.Clamp(k, 0, h - 1) * stride;
                    for (int j = 0; j < n; j++) sums[j] += src[row + x0 + j];
                }
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int addRow = Math.Min(y + r + 1, h - 1) * stride, remRow = Math.Max(y - r, 0) * stride;
                    for (int j = 0; j < n; j++)
                    {
                        dst[row + x0 + j] = (byte)(sums[j] * inv + 0.5);
                        sums[j] += src[addRow + x0 + j] - src[remRow + x0 + j];
                    }
                }
            });
        }
    }
}
