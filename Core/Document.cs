using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    /// <summary>A layered image. Layers[0] is the top-most layer (same order as the Layers panel).</summary>
    public sealed class Document
    {
        public Document(int width, int height)
        {
            Width = width;
            Height = height;
        }

        byte[] _composite;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public ObservableCollection<Layer> Layers { get; } = new ObservableCollection<Layer>();
        /// <summary>The layers blended for the screen; made again (empty, see <see cref="CompositeRegion"/>) after <see cref="Park"/>.</summary>
        public byte[] Composite => _composite ??= new byte[Width * Height * 4];

        /// <summary>
        /// Frees what a tab that is not shown does not need: the screen image and the pixels of untouched layers.
        /// With dozens of photos open they would otherwise fill the memory (see <see cref="Layer.Park"/>).
        /// </summary>
        public void Park()
        {
            _composite = null;
            foreach (var l in Layers) l.Park();
        }
        public Int32Rect Bounds => new Int32Rect(0, 0, Width, Height);

        public static Document CreateBlank(int w, int h, Color? background)
        {
            var doc = new Document(w, h);
            var layer = new Layer(T("Sfondo"), w, h);
            if (background is Color c) layer.Fill(c);
            layer.UpdateThumbnail();
            doc.Layers.Add(layer);
            return doc;
        }

        public void CompositeRegion(Int32Rect r)
        {
            r = ImageOps.Intersect(r, Bounds);
            if (r.Width <= 0 || r.Height <= 0) return;

            var list = new List<Layer>();
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                var l = Layers[i];
                if (l.Visible && l.Opacity > 0) list.Add(l);
            }
            var comp = Composite;
            int w = Width;
            Parallel.For(r.Y, r.Y + r.Height, y =>
            {
                int start = (y * w + r.X) * 4, end = start + r.Width * 4;
                Array.Clear(comp, start, end - start);
                foreach (var l in list) BlendSpan(comp, l.Pixels, start, end, l.Opacity / 100.0, l.Blend);
            });
        }

        /// <summary>Renders all visible layers into a new buffer, optionally over a solid background.</summary>
        public byte[] Render(Color? background = null)
        {
            var buf = new byte[Width * Height * 4];
            if (background is Color c)
                for (int i = 0; i < buf.Length; i += 4) { buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; buf[i + 3] = 255; }
            var list = new List<Layer>();
            for (int i = Layers.Count - 1; i >= 0; i--)
                if (Layers[i].Visible && Layers[i].Opacity > 0) list.Add(Layers[i]);
            int rowBytes = Width * 4;
            Parallel.For(0, Height, y =>
            {
                int start = y * rowBytes;
                foreach (var l in list) BlendSpan(buf, l.Pixels, start, start + rowBytes, l.Opacity / 100.0, l.Blend);
            });
            return buf;
        }

        public static void BlendSpan(byte[] dst, byte[] src, int start, int end, double opacity, BlendMode mode)
        {
            float op = (float)opacity;
            for (int i = start; i < end; i += 4)
            {
                int sa = src[i + 3];
                if (sa == 0) continue;
                float a = sa * op / 255f;
                int dav = dst[i + 3];
                if (dav == 0 || (mode == BlendMode.Normal && a >= 1f))
                {
                    dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
                    dst[i + 3] = (byte)(a * 255f + 0.5f);
                    continue;
                }
                float ab = dav / 255f;
                float ao = a + ab * (1 - a);
                float k1 = a / ao, k2 = ab * (1 - a) / ao;
                for (int c = 0; c < 3; c++)
                {
                    float cs = src[i + c] / 255f, cb = dst[i + c] / 255f;
                    if (mode != BlendMode.Normal) cs = (1 - ab) * cs + ab * Blend(mode, cb, cs);
                    float co = cs * k1 + cb * k2;
                    dst[i + c] = (byte)(co * 255f + 0.5f);
                }
                dst[i + 3] = (byte)(ao * 255f + 0.5f);
            }
        }

        static float Blend(BlendMode m, float b, float s)
        {
            switch (m)
            {
                case BlendMode.Multiply: return b * s;
                case BlendMode.Screen: return b + s - b * s;
                case BlendMode.Overlay: return b <= 0.5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s);
                case BlendMode.HardLight: return s <= 0.5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s);
                case BlendMode.SoftLight:
                    if (s <= 0.5f) return b - (1 - 2 * s) * b * (1 - b);
                    float d = b <= 0.25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b);
                    return b + (2 * s - 1) * (d - b);
                case BlendMode.Darken: return Math.Min(b, s);
                case BlendMode.Lighten: return Math.Max(b, s);
                case BlendMode.ColorBurn: return b >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - b) / s);
                case BlendMode.ColorDodge: return b <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s));
                case BlendMode.LinearDodge: return Math.Min(1, b + s);
                case BlendMode.Difference: return Math.Abs(b - s);
                case BlendMode.Exclusion: return b + s - 2 * b * s;
                default: return s;
            }
        }

        public DocState Snapshot(int activeIndex, Selection selection)
        {
            var st = new DocState { Width = Width, Height = Height, ActiveIndex = activeIndex, Selection = selection };
            foreach (var l in Layers)
            {
                if (l.Dirty || l.SnapshotPixels == null)
                {
                    l.SnapshotPixels = (byte[])l.Pixels.Clone();
                    l.Dirty = false;
                }
                st.Layers.Add(new LayerState { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity, Blend = l.Blend, Pixels = l.SnapshotPixels });
            }
            return st;
        }

        public void Restore(DocState s)
        {
            if (s.Width != Width || s.Height != Height) _composite = null;
            Width = s.Width;
            Height = s.Height;
            Layers.Clear();
            foreach (var ls in s.Layers)
            {
                var l = new Layer(ls.Name, s.Width, s.Height, (byte[])ls.Pixels.Clone())
                {
                    Visible = ls.Visible,
                    Opacity = ls.Opacity,
                    Blend = ls.Blend,
                };
                l.SnapshotPixels = ls.Pixels;
                l.Dirty = false;
                l.UpdateThumbnail();
                Layers.Add(l);
            }
        }

        /// <summary>Applies a geometric transform to every layer; the new size is nw x nh.</summary>
        public void Transform(Func<byte[], byte[]> f, int nw, int nh)
        {
            foreach (var l in Layers)
            {
                l.SetContent(nw, nh, f(l.Pixels));
                l.UpdateThumbnail();
            }
            Width = nw;
            Height = nh;
            _composite = null;
        }

        /// <summary>Merges the layer at index into the one below it. Returns the index of the merged layer.</summary>
        public int MergeDown(int index)
        {
            if (index < 0 || index >= Layers.Count - 1) return index;
            var top = Layers[index];
            var bottom = Layers[index + 1];
            if (top.Visible)
            {
                var px = (byte[])bottom.Pixels.Clone();
                int rowBytes = Width * 4;
                Parallel.For(0, Height, y =>
                    BlendSpan(px, top.Pixels, y * rowBytes, (y + 1) * rowBytes, top.Opacity / 100.0, top.Blend));
                bottom.Pixels = px;
            }
            Layers.RemoveAt(index);
            bottom.UpdateThumbnail();
            return index;
        }

        public void Flatten(Color background)
        {
            var px = Render(background);
            Layers.Clear();
            var l = new Layer(T("Sfondo"), Width, Height, px);
            l.UpdateThumbnail();
            Layers.Add(l);
        }
    }
}
