using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoStudio.Core
{
    public enum StrokeMode { Paint, Erase, Clone, Dodge, Burn }

    /// <summary>
    /// Brush stroke engine. Coverage of the whole stroke is kept in a mask so overlapping
    /// dabs never exceed the stroke opacity (like Photoshop).
    /// </summary>
    public sealed class StrokeEngine
    {
        readonly Layer _layer;
        readonly byte[] _base;
        readonly byte[] _mask;
        readonly Selection _sel;
        readonly int _w, _h;
        Point _last;

        public double Size = 20, Hardness = 0.8, Opacity = 1;
        public Color Color = Colors.Black;
        public StrokeMode Mode = StrokeMode.Paint;
        public int CloneDx, CloneDy;

        public StrokeEngine(Layer layer, Selection sel)
        {
            _layer = layer;
            _w = layer.Width;
            _h = layer.Height;
            _sel = sel;
            _base = (byte[])layer.Pixels.Clone();
            _mask = new byte[_w * _h];
            layer.MarkDirty();
        }

        public Int32Rect Begin(Point p)
        {
            _last = p;
            return Stamp(p);
        }

        public Int32Rect LineTo(Point p)
        {
            Vector d = p - _last;
            double len = d.Length;
            double step = Math.Max(1, Size * 0.1);
            if (len < step) return new Int32Rect(0, 0, 0, 0);
            int n = (int)(len / step);
            var dirty = new Int32Rect(0, 0, 0, 0);
            for (int i = 1; i <= n; i++) dirty = ImageOps.Union(dirty, Stamp(_last + d * (i * step / len)));
            _last += d * (n * step / len);
            return dirty;
        }

        Int32Rect Stamp(Point c)
        {
            double r = Math.Max(0.5, Size / 2);
            int x0 = Math.Max(0, (int)Math.Floor(c.X - r - 1)), y0 = Math.Max(0, (int)Math.Floor(c.Y - r - 1));
            int x1 = Math.Min(_w - 1, (int)Math.Ceiling(c.X + r + 1)), y1 = Math.Min(_h - 1, (int)Math.Ceiling(c.Y + r + 1));
            if (x1 < x0 || y1 < y0) return new Int32Rect(0, 0, 0, 0);
            double edge = Math.Max(1.0, r * (1 - Hardness));
            var px = _layer.Pixels;
            for (int y = y0; y <= y1; y++)
            {
                double dy = y + 0.5 - c.Y;
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x + 0.5 - c.X;
                    double cov = (r - Math.Sqrt(dx * dx + dy * dy)) / edge;
                    if (cov <= 0) continue;
                    cov = cov >= 1 ? 1 : cov * cov * (3 - 2 * cov);
                    int mi = y * _w + x;
                    if (_sel != null) cov *= _sel.Mask[mi] / 255.0;
                    byte m = (byte)(cov * 255 + 0.5);
                    if (m <= _mask[mi]) continue;
                    _mask[mi] = m;
                    ApplyPixel(px, mi, x, y, m / 255.0 * Opacity);
                }
            }
            return new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        void ApplyPixel(byte[] px, int mi, int x, int y, double a)
        {
            int i = mi * 4;
            px[i] = _base[i]; px[i + 1] = _base[i + 1]; px[i + 2] = _base[i + 2]; px[i + 3] = _base[i + 3];
            switch (Mode)
            {
                case StrokeMode.Paint:
                    ImageOps.Over(px, i, Color.B, Color.G, Color.R, a * Color.A / 255.0);
                    break;
                case StrokeMode.Erase:
                    px[i + 3] = (byte)(_base[i + 3] * (1 - a) + 0.5);
                    break;
                case StrokeMode.Clone:
                    int sx = x + CloneDx, sy = y + CloneDy;
                    if (sx < 0 || sy < 0 || sx >= _w || sy >= _h) break;
                    int si = (sy * _w + sx) * 4;
                    ImageOps.Over(px, i, _base[si], _base[si + 1], _base[si + 2], a * _base[si + 3] / 255.0);
                    break;
                case StrokeMode.Dodge:
                case StrokeMode.Burn:
                    double g = Mode == StrokeMode.Dodge ? 1 / (1 + a) : 1 + a;
                    for (int c = 0; c < 3; c++) px[i + c] = ImageOps.ClampByte(Math.Pow(_base[i + c] / 255.0, g) * 255);
                    break;
            }
        }
    }

    public static class Painting
    {
        /// <summary>Returns a 0/255 mask of pixels similar to the one at (sx,sy).</summary>
        public static byte[] Region(byte[] px, int w, int h, int sx, int sy, int tol, bool contiguous)
        {
            var reg = new byte[w * h];
            int si = (sy * w + sx) * 4;
            byte tb = px[si], tg = px[si + 1], tr = px[si + 2], ta = px[si + 3];

            bool Match(int p)
            {
                int i = p * 4;
                if (ta == 0 && px[i + 3] == 0) return true;
                return Math.Abs(px[i] - tb) <= tol && Math.Abs(px[i + 1] - tg) <= tol
                    && Math.Abs(px[i + 2] - tr) <= tol && Math.Abs(px[i + 3] - ta) <= tol;
            }

            if (!contiguous)
            {
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++) if (Match(y * w + x)) reg[y * w + x] = 255;
                });
                return reg;
            }

            var stack = new Stack<(int x, int y)>();
            stack.Push((sx, sy));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                int row = y * w;
                if (reg[row + x] != 0 || !Match(row + x)) continue;
                int l = x, r = x;
                while (l > 0 && reg[row + l - 1] == 0 && Match(row + l - 1)) l--;
                while (r < w - 1 && reg[row + r + 1] == 0 && Match(row + r + 1)) r++;
                for (int i = l; i <= r; i++) reg[row + i] = 255;
                for (int ny = y - 1; ny <= y + 1; ny += 2)
                {
                    if (ny < 0 || ny >= h) continue;
                    int nrow = ny * w;
                    bool inSpan = false;
                    for (int i = l; i <= r; i++)
                    {
                        bool ok = reg[nrow + i] == 0 && Match(nrow + i);
                        if (ok && !inSpan) { stack.Push((i, ny)); inSpan = true; }
                        else if (!ok) inSpan = false;
                    }
                }
            }
            return reg;
        }

        public static Int32Rect FloodFill(Layer layer, int x, int y, Color color, int tolerance, bool contiguous, double opacity, Selection sel)
        {
            int w = layer.Width, h = layer.Height;
            if (x < 0 || y < 0 || x >= w || y >= h) return new Int32Rect(0, 0, 0, 0);
            var reg = Region(layer.Pixels, w, h, x, y, tolerance, contiguous);
            var px = layer.Pixels;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int p = yy * w + xx;
                    if (reg[p] == 0) continue;
                    double a = opacity * color.A / 255.0;
                    if (sel != null) a *= sel.Mask[p] / 255.0;
                    if (a <= 0) continue;
                    ImageOps.Over(px, p * 4, color.B, color.G, color.R, a);
                    if (xx < minX) minX = xx; if (xx > maxX) maxX = xx;
                    if (yy < minY) minY = yy; if (yy > maxY) maxY = yy;
                }
            layer.MarkDirty();
            return maxX < 0 ? new Int32Rect(0, 0, 0, 0) : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public static void FillArea(Layer layer, Color color, Selection sel)
        {
            int w = layer.Width, h = layer.Height;
            var px = layer.Pixels;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    double a = color.A / 255.0 * (sel == null ? 1 : sel.Mask[p] / 255.0);
                    ImageOps.Over(px, p * 4, color.B, color.G, color.R, a);
                }
            });
            layer.MarkDirty();
        }

        public static void ClearArea(Layer layer, Selection sel)
        {
            int n = layer.Width * layer.Height;
            var px = layer.Pixels;
            for (int p = 0; p < n; p++)
            {
                int m = sel.Mask[p];
                if (m == 0) continue;
                px[p * 4 + 3] = (byte)(px[p * 4 + 3] * (255 - m) / 255);
            }
            layer.MarkDirty();
        }

        public static void Gradient(Layer layer, Point p0, Point p1, Color c0, Color c1, bool radial, double opacity, Selection sel)
        {
            int w = layer.Width, h = layer.Height;
            var px = layer.Pixels;
            Vector d = p1 - p0;
            double len2 = d.LengthSquared;
            if (len2 < 1) return;
            double len = Math.Sqrt(len2);
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    double m = sel == null ? 1 : sel.Mask[p] / 255.0;
                    if (m <= 0) continue;
                    double fx = x + 0.5 - p0.X, fy = y + 0.5 - p0.Y;
                    double t = radial ? Math.Sqrt(fx * fx + fy * fy) / len : (fx * d.X + fy * d.Y) / len2;
                    t = Math.Clamp(t, 0, 1);
                    double a = (c0.A + (c1.A - c0.A) * t) / 255.0;
                    byte b = (byte)(c0.B + (c1.B - c0.B) * t + 0.5);
                    byte g = (byte)(c0.G + (c1.G - c0.G) * t + 0.5);
                    byte r = (byte)(c0.R + (c1.R - c0.R) * t + 0.5);
                    ImageOps.Over(px, p * 4, b, g, r, a * opacity * m);
                }
            });
            layer.MarkDirty();
        }

        /// <summary>Rasterizes WPF drawing commands (in document coordinates) onto a layer.</summary>
        public static Int32Rect RenderDrawing(Layer layer, Rect bounds, Action<DrawingContext> draw, Selection sel)
        {
            int w = layer.Width, h = layer.Height;
            int bx = (int)Math.Floor(bounds.X) - 2, by = (int)Math.Floor(bounds.Y) - 2;
            var r = ImageOps.Intersect(
                new Int32Rect(bx, by, (int)Math.Ceiling(bounds.Right) - bx + 3, (int)Math.Ceiling(bounds.Bottom) - by + 3),
                new Int32Rect(0, 0, w, h));
            if (ImageOps.IsEmpty(r)) return r;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-r.X, -r.Y));
                draw(dc);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var buf = new byte[r.Width * r.Height * 4];
            rtb.CopyPixels(buf, r.Width * 4, 0);

            var px = layer.Pixels;
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                {
                    int bi = (y * r.Width + x) * 4;
                    int sa = buf[bi + 3];
                    if (sa == 0) continue;
                    int p = (r.Y + y) * w + r.X + x;
                    double m = sel == null ? 1 : sel.Mask[p] / 255.0;
                    if (m <= 0) continue;
                    byte b = (byte)Math.Min(255, buf[bi] * 255 / sa);
                    byte g = (byte)Math.Min(255, buf[bi + 1] * 255 / sa);
                    byte rr = (byte)Math.Min(255, buf[bi + 2] * 255 / sa);
                    ImageOps.Over(px, p * 4, b, g, rr, sa / 255.0 * m);
                }
            layer.MarkDirty();
            return r;
        }
    }
}
