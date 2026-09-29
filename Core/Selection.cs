using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoStudio.Core
{
    public enum SelectionOp { Replace, Add, Subtract, Intersect }

    /// <summary>Immutable selection: an 8-bit coverage mask the size of the document.</summary>
    public sealed class Selection
    {
        Geometry _outline;
        readonly Geometry _shape; // exact vector outline when known (rect, ellipse, lasso)

        Selection(int w, int h, byte[] mask, Geometry shape)
        {
            Width = w;
            Height = h;
            Mask = mask;
            _shape = shape;
            Bounds = ComputeBounds();
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Mask { get; }
        public Int32Rect Bounds { get; }
        public bool IsEmpty => Bounds.Width <= 0 || Bounds.Height <= 0;
        public Geometry Outline => _outline ??= _shape ?? TraceOutline();

        public static Selection FromRect(Int32Rect r, int w, int h)
        {
            r = ImageOps.Intersect(r, new Int32Rect(0, 0, w, h));
            var m = new byte[w * h];
            for (int y = r.Y; y < r.Y + r.Height; y++) Array.Fill(m, (byte)255, y * w + r.X, r.Width);
            var shape = new RectangleGeometry(new Rect(r.X, r.Y, r.Width, r.Height));
            shape.Freeze();
            return new Selection(w, h, m, shape);
        }

        public static Selection All(int w, int h) => FromRect(new Int32Rect(0, 0, w, h), w, h);

        public static Selection FromMask(byte[] mask, int w, int h) => new Selection(w, h, mask, null);

        public static Selection FromGeometry(Geometry g, int w, int h)
        {
            var m = new byte[w * h];
            var b = g.Bounds;
            if (!b.IsEmpty)
            {
                int bx = (int)Math.Floor(b.X), by = (int)Math.Floor(b.Y);
                var r = ImageOps.Intersect(
                    new Int32Rect(bx, by, (int)Math.Ceiling(b.Right) - bx + 1, (int)Math.Ceiling(b.Bottom) - by + 1),
                    new Int32Rect(0, 0, w, h));
                if (!ImageOps.IsEmpty(r))
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.PushTransform(new TranslateTransform(-r.X, -r.Y));
                        dc.DrawGeometry(Brushes.White, null, g);
                        dc.Pop();
                    }
                    var rtb = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    var buf = new byte[r.Width * r.Height * 4];
                    rtb.CopyPixels(buf, r.Width * 4, 0);
                    for (int y = 0; y < r.Height; y++)
                        for (int x = 0; x < r.Width; x++)
                            m[(r.Y + y) * w + r.X + x] = buf[(y * r.Width + x) * 4 + 3];
                }
            }
            var shape = g.Clone();
            shape.Freeze();
            return new Selection(w, h, m, shape);
        }

        public Selection Combine(Selection other, SelectionOp op)
        {
            if (op == SelectionOp.Replace) return other;
            var m = new byte[Width * Height];
            var a = Mask; var b = other.Mask;
            for (int i = 0; i < m.Length; i++)
            {
                m[i] = op switch
                {
                    SelectionOp.Add => Math.Max(a[i], b[i]),
                    SelectionOp.Subtract => (byte)(a[i] * (255 - b[i]) / 255),
                    _ => Math.Min(a[i], b[i]),
                };
            }
            Geometry shape = null;
            if (_shape != null && other._shape != null)
            {
                var mode = op == SelectionOp.Add ? GeometryCombineMode.Union
                         : op == SelectionOp.Subtract ? GeometryCombineMode.Exclude : GeometryCombineMode.Intersect;
                shape = new CombinedGeometry(mode, _shape, other._shape);
                shape.Freeze();
            }
            return new Selection(Width, Height, m, shape);
        }

        public Selection Inverted()
        {
            var m = new byte[Mask.Length];
            for (int i = 0; i < m.Length; i++) m[i] = (byte)(255 - Mask[i]);
            Geometry shape = null;
            if (_shape != null)
            {
                shape = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), _shape);
                shape.Freeze();
            }
            return new Selection(Width, Height, m, shape);
        }

        public Selection Feathered(double radius)
        {
            var m = (byte[])Mask.Clone();
            ImageOps.GaussianBlur(m, Width, Height, 1, radius);
            return new Selection(Width, Height, m, null);
        }

        public Selection Translated(int dx, int dy)
        {
            var m = new byte[Mask.Length];
            for (int y = 0; y < Height; y++)
            {
                int sy = y - dy;
                if (sy < 0 || sy >= Height) continue;
                for (int x = 0; x < Width; x++)
                {
                    int sx = x - dx;
                    if (sx >= 0 && sx < Width) m[y * Width + x] = Mask[sy * Width + sx];
                }
            }
            Geometry shape = null;
            if (_shape != null)
            {
                var g = new GeometryGroup { Transform = new TranslateTransform(dx, dy) };
                g.Children.Add(_shape);
                g.Freeze();
                shape = g;
            }
            return new Selection(Width, Height, m, shape);
        }

        Int32Rect ComputeBounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                int first = -1, last = -1;
                for (int x = 0; x < Width; x++)
                {
                    if (Mask[row + x] == 0) continue;
                    if (first < 0) first = x;
                    last = x;
                }
                if (first < 0) continue;
                if (first < minX) minX = first;
                if (last > maxX) maxX = last;
                if (y < minY) minY = y;
                maxY = y;
            }
            return maxX < 0 ? new Int32Rect(0, 0, 0, 0) : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>Builds the "marching ants" outline from the mask (pixel edges where coverage crosses 50%).</summary>
        Geometry TraceOutline()
        {
            var sg = new StreamGeometry();
            if (!IsEmpty)
            {
                var b = Bounds;
                int x0 = b.X, y0 = b.Y, x1 = b.X + b.Width, y1 = b.Y + b.Height;
                bool In(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Mask[y * Width + x] >= 128;
                using (var ctx = sg.Open())
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        int start = -1;
                        for (int x = x0; x <= x1; x++)
                        {
                            bool edge = x < x1 && In(x, y - 1) != In(x, y);
                            if (edge) { if (start < 0) start = x; }
                            else if (start >= 0)
                            {
                                ctx.BeginFigure(new Point(start, y), false, false);
                                ctx.LineTo(new Point(x, y), true, false);
                                start = -1;
                            }
                        }
                    }
                    var runStart = new int[x1 - x0 + 1];
                    Array.Fill(runStart, -1);
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int k = x - x0;
                            bool edge = y < y1 && In(x - 1, y) != In(x, y);
                            if (edge) { if (runStart[k] < 0) runStart[k] = y; }
                            else if (runStart[k] >= 0)
                            {
                                ctx.BeginFigure(new Point(x, runStart[k]), false, false);
                                ctx.LineTo(new Point(x, y), true, false);
                                runStart[k] = -1;
                            }
                        }
                    }
                }
            }
            sg.Freeze();
            return sg;
        }
    }
}
