using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Removes what is selected and rebuilds the background behind it, with LaMa (Large Mask inpainting, Apache 2.0)
    /// on the PC. Each separate part of the selection is rebuilt on its own, from a square of the photo around it
    /// (the context the network looks at), and blended back with soft edges.
    /// </summary>
    public static class AiInpaint
    {
        const int Side = 512;

        /// <summary>
        /// The layer (non-premultiplied BGRA, w × h) with the selected area (0..255 per pixel) rebuilt.
        /// Progress goes from 0 to 1.
        /// </summary>
        public static byte[] Remove(byte[] src, int w, int h, byte[] selection, Action<double> progress = null, CancellationToken ct = default)
        {
            var net = AiModels.Get(AiModels.Inpaint) ?? throw new InvalidOperationException(Loc.T("La rete per la rimozione AI non si è potuta avviare."));
            var hole = new bool[w * h];
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (selection[y * w + x] >= 64)
                    {
                        hole[y * w + x] = true;
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
            if (x1 < 0) return (byte[])src.Clone();

            // A selection drawn by hand rarely covers the outline and the shadow of the object: a few pixels more.
            int grow = Math.Clamp((int)Math.Round(Math.Max(x1 - x0, y1 - y0) * 0.015) + 3, 3, 24);
            hole = Dilate(hole, w, h, grow, Math.Max(0, x0 - grow), Math.Max(0, y0 - grow), Math.Min(w - 1, x1 + grow), Math.Min(h - 1, y1 + grow));

            var dst = (byte[])src.Clone();
            var parts = Components(hole, w, h);
            parts.Sort((a, b) => b.Count.CompareTo(a.Count));
            int done = 0;
            progress?.Invoke(0);
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                if (part.Count >= 4) Fill(net, dst, w, h, hole, part);
                progress?.Invoke(++done / (double)parts.Count);
            }
            return dst;
        }

        sealed class Part
        {
            public int X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1, Count;
        }

        /// <summary>Rebuilds one part of the hole from a square of the photo around it, three times its size.</summary>
        static void Fill(AiModels.Runner net, byte[] img, int w, int h, bool[] hole, Part part)
        {
            int bw = part.X1 - part.X0 + 1, bh = part.Y1 - part.Y0 + 1;
            int side = Math.Max(128, Math.Max(bw, bh) * 3);
            int cw = Math.Min(side, w), ch = Math.Min(side, h);
            int cx = Math.Clamp((part.X0 + part.X1) / 2 - cw / 2, 0, w - cw), cy = Math.Clamp((part.Y0 + part.Y1) / 2 - ch / 2, 0, h - ch);

            // The crop, stretched to the 512 × 512 of the network, and its mask (1 = to rebuild).
            var image = new float[3 * Side * Side];
            var mask = new float[Side * Side];
            const int plane = Side * Side;
            Parallel.For(0, Side, y =>
            {
                float sy = (y + 0.5f) * ch / Side - 0.5f;
                for (int x = 0; x < Side; x++)
                {
                    float sx = (x + 0.5f) * cw / Side - 0.5f;
                    Sample(img, w, h, cx + sx, cy + sy, out float b, out float g, out float r);
                    int o = y * Side + x;
                    image[o] = r / 255f; image[plane + o] = g / 255f; image[2 * plane + o] = b / 255f;
                    // A network pixel is part of the hole when any photo pixel under it is.
                    int px0 = cx + (int)(x * cw / (float)Side), px1 = cx + Math.Max((int)((x + 1) * cw / (float)Side) - 1, (int)(x * cw / (float)Side));
                    int py0 = cy + (int)(y * ch / (float)Side), py1 = cy + Math.Max((int)((y + 1) * ch / (float)Side) - 1, (int)(y * ch / (float)Side));
                    bool any = false;
                    for (int yy = py0; yy <= py1 && !any; yy++)
                        for (int xx = px0; xx <= px1; xx++)
                            if (hole[Math.Min(h - 1, yy) * w + Math.Min(w - 1, xx)]) { any = true; break; }
                    mask[o] = any ? 1 : 0;
                }
            });
            var output = net.Run(new[] { ("image", image, new long[] { 1, 3, Side, Side }), ("mask", mask, new long[] { 1, 1, Side, Side }) }, "output")[0];

            // Back to the size of the crop, blended in with a soft edge: inside the hole the network's pixels, at the
            // border a short transition, outside the photo as it was.
            const int feather = 2;
            int fx0 = Math.Max(cx, part.X0 - feather), fy0 = Math.Max(cy, part.Y0 - feather);
            int fx1 = Math.Min(cx + cw - 1, part.X1 + feather), fy1 = Math.Min(cy + ch - 1, part.Y1 + feather);
            var soft = Soften(hole, w, h, fx0, fy0, fx1, fy1, feather);
            int sw = fx1 - fx0 + 1;
            // A large crop is rebuilt at a lower resolution than the photo, so the fill is smoother than its
            // surroundings: grain like the photo's around the hole is added back, in proportion to the reduction.
            float scale = Math.Max(cw, ch) / (float)Side;
            float grain = scale > 1.3f ? Grain(img, w, h, hole, fx0, fy0, fx1, fy1) * (1 - 1 / scale) : 0;
            Parallel.For(fy0, fy1 + 1, y =>
            {
                float ny = (y - cy + 0.5f) * Side / ch - 0.5f;
                for (int x = fx0; x <= fx1; x++)
                {
                    float a = soft[(y - fy0) * sw + (x - fx0)];
                    if (a <= 0) continue;
                    float nx = (x - cx + 0.5f) * Side / cw - 0.5f;
                    int i = (y * w + x) * 4;
                    float n = grain > 0 ? grain * Noise(x, y) : 0;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = Bilinear(output, (2 - c) * plane, nx, ny) + n;   // network planes are R, G, B
                        float mixed = img[i + c] + a * (v - img[i + c]);
                        img[i + c] = mixed <= 0 ? (byte)0 : mixed >= 255 ? (byte)255 : (byte)(mixed + 0.5f);
                    }
                }
            });
        }

        /// <summary>
        /// The fine grain of the photo around the hole: the standard deviation of the luminance minus its 3 × 3 mean,
        /// on a band of pixels outside the hole.
        /// </summary>
        static float Grain(byte[] img, int w, int h, bool[] hole, int x0, int y0, int x1, int y1)
        {
            const int band = 24;
            x0 = Math.Max(1, x0 - band); y0 = Math.Max(1, y0 - band); x1 = Math.Min(w - 2, x1 + band); y1 = Math.Min(h - 2, y1 + band);
            double sum = 0, sum2 = 0;
            long n = 0;
            float L(int x, int y) { int i = (y * w + x) * 4; return 0.114f * img[i] + 0.587f * img[i + 1] + 0.299f * img[i + 2]; }
            for (int y = y0; y <= y1; y += 2)
                for (int x = x0; x <= x1; x += 2)
                {
                    if (hole[y * w + x]) continue;
                    float mean = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) mean += L(x + dx, y + dy);
                    float d = L(x, y) - mean / 9;
                    sum += d; sum2 += d * d; n++;
                }
            if (n < 50) return 0;
            double m = sum / n;
            // The 3 × 3 mean removes about a ninth of white noise, so the deviation is slightly larger than measured.
            return (float)Math.Min(12, Math.Sqrt(Math.Max(0, sum2 / n - m * m)) * 1.06);
        }

        /// <summary>Repeatable noise with zero mean and unit deviation (a hash of the pixel, approximately normal).</summary>
        static float Noise(int x, int y)
        {
            uint s = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            float total = 0;
            for (int k = 0; k < 4; k++)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                total += (s & 0xFFFF) / 65535f;
            }
            return (total - 2) * 1.732f;   // the sum of 4 uniforms has deviation 1/sqrt(3)
        }

        static void Sample(byte[] img, int w, int h, float x, float y, out float b, out float g, out float r)
        {
            x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
            float fx = x - x0, fy = y - y0;
            int a = (y0 * w + x0) * 4, bb = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, d = (y1 * w + x1) * 4;
            float L(int k) => (img[a + k] + (img[bb + k] - img[a + k]) * fx) * (1 - fy) + (img[c + k] + (img[d + k] - img[c + k]) * fx) * fy;
            b = L(0); g = L(1); r = L(2);
        }

        static float Bilinear(float[] p, int offset, float x, float y)
        {
            x = Math.Clamp(x, 0, Side - 1); y = Math.Clamp(y, 0, Side - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(Side - 1, x0 + 1), y1 = Math.Min(Side - 1, y0 + 1);
            float fx = x - x0, fy = y - y0;
            float a = p[offset + y0 * Side + x0], b = p[offset + y0 * Side + x1], c = p[offset + y1 * Side + x0], d = p[offset + y1 * Side + x1];
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }

        /// <summary>The hole grown by r pixels (square), inside the given box.</summary>
        static bool[] Dilate(bool[] m, int w, int h, int r, int bx0, int by0, int bx1, int by1)
        {
            var tmp = new bool[m.Length];
            var dst = new bool[m.Length];
            Parallel.For(by0, by1 + 1, y =>
            {
                int last = -1 << 20;
                for (int x = bx0 - r; x <= bx1 + r; x++)
                {
                    int sx = x + r;
                    if (sx >= 0 && sx < w && m[y * w + sx]) last = sx;
                    if (x >= bx0 && x <= bx1 && last >= 0 && x - last <= r) tmp[y * w + x] = true;   // a pixel of the hole within r
                }
            });
            Parallel.For(bx0, bx1 + 1, x =>
            {
                int last = -1 << 20;
                for (int y = by0 - r; y <= by1 + r; y++)
                {
                    int sy = y + r;
                    if (sy >= 0 && sy < h && tmp[sy * w + x]) last = sy;
                    if (y >= by0 && y <= by1 && last >= 0 && y - last <= r) dst[y * w + x] = true;
                }
            });
            return dst;
        }

        /// <summary>The separate parts of the hole (4-connected).</summary>
        static List<Part> Components(bool[] m, int w, int h)
        {
            var seen = new bool[m.Length];
            var parts = new List<Part>();
            var stack = new Stack<int>();
            for (int start = 0; start < m.Length; start++)
            {
                if (!m[start] || seen[start]) continue;
                var p = new Part();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    p.Count++;
                    if (x < p.X0) p.X0 = x; if (x > p.X1) p.X1 = x;
                    if (y < p.Y0) p.Y0 = y; if (y > p.Y1) p.Y1 = y;
                    if (x > 0 && m[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                    if (x < w - 1 && m[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                    if (y > 0 && m[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                    if (y < h - 1 && m[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
                }
                parts.Add(p);
            }
            return parts;
        }

        /// <summary>The hole inside a box as weights 0..1, blurred by r pixels so the edge of the fill does not show.</summary>
        static float[] Soften(bool[] m, int w, int h, int x0, int y0, int x1, int y1, int r)
        {
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            var a = new float[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int sum = 0, n = 0;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int sx = x0 + x + dx, sy = y0 + y + dy;
                            if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                            n++;
                            if (m[sy * w + sx]) sum++;
                        }
                    // Pixels of the hole itself always take the fill; the blur only reaches outward.
                    a[y * bw + x] = m[(y0 + y) * w + x0 + x] ? 1 : n == 0 ? 0 : sum / (float)n;
                }
            return a;
        }
    }
}
