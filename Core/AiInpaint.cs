using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Removes what is selected and rebuilds the background behind it, with LaMa (Large Mask inpainting, Apache 2.0)
    /// on the PC. Each separate part of the selection is rebuilt on its own, from the photo around it (the context
    /// the network looks at) at its real resolution as far as possible, and blended back with soft edges.
    /// </summary>
    public static class AiInpaint
    {
        /// <summary>Largest crop the network works on: beyond it the crop is reduced (and the texture is added back).</summary>
        const int MaxSide = 1536, MaxPixels = 1024 * 1024;
        /// <summary>
        /// Above this size (the longer side, in pixels of the photo) a hole is rebuilt from a reduced copy: LaMa keeps
        /// the shapes of the scene together much better when it sees a large hole small, and the texture of the photo
        /// is added back afterwards.
        /// </summary>
        const int MaxHole = 900;
        /// <summary>How much each part of the selection grows, as a share of its size (on top of a few pixels).</summary>
        const double GrowShare = 0.045;

        /// <summary>
        /// The layer (non-premultiplied BGRA, w × h) with the selected area (0..255 per pixel) rebuilt.
        /// Progress goes from 0 to 1.
        /// </summary>
        /// <param name="shadow">Also the shadow the object casts next to and under it (see <see cref="AddShadow"/>).</param>
        public static byte[] Remove(byte[] src, int w, int h, byte[] selection, Action<double> progress = null, CancellationToken ct = default,
                                    bool shadow = false)
        {
            var net = AiModels.Get(AiModels.Inpaint) ?? throw new InvalidOperationException(Loc.T("La rete per la rimozione AI non si è potuta avviare."));
            // Even a partly selected pixel (a soft outline, hair) goes: what is left of an object is what the network
            // would continue into the hole.
            var raw = new bool[w * h];
            bool any = false;
            for (int i = 0; i < raw.Length; i++) if (selection[i] >= 32) { raw[i] = true; any = true; }
            if (!any) return (byte[])src.Clone();

            // A selection rarely covers the whole outline (and the blur around it) of the object: each part grows in
            // proportion to its size, and at least by a few pixels of the resolution the network will see it at.
            var label = new int[w * h];
            var first = Components(raw, w, h, label);
            var hole = new bool[w * h];
            for (int k = 0; k < first.Count; k++)
            {
                var part = first[k];
                int size = Math.Max(part.X1 - part.X0, part.Y1 - part.Y0) + 1;
                int grow = Math.Clamp((int)Math.Ceiling(Math.Max(size * GrowShare + 6, 6 * Reduction(size * 2.5, size * 2.5, size))), 4, 96);
                DilateInto(label, k + 1, part, grow, hole, w, h);
            }
            if (shadow)
                for (int k = 0; k < first.Count; k++)
                    if (first[k].Count >= 64) AddShadow(src, w, h, hole, first[k]);

            var dst = (byte[])src.Clone();
            var parts = Components(hole, w, h, null);
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

        /// <summary>
        /// How much a crop of cw × ch photo pixels around a hole of the given size (its longer side) is reduced for
        /// the network (1 = not at all): to fit its limits, and so that a large hole is seen small enough.
        /// </summary>
        static double Reduction(double cw, double ch, double hole) =>
            Math.Max(1, Math.Max(Math.Max(Math.Max(cw, ch) / MaxSide, Math.Sqrt(cw * ch / MaxPixels)), hole / MaxHole));

        static int Round8(double v) => Math.Max(64, (int)Math.Round(v / 8) * 8);

        /// <summary>
        /// Rebuilds one part of the hole from the photo around it: a margin of about three quarters of the part's size
        /// on every side, at the photo's own resolution when it fits the network's limits.
        /// </summary>
        static void Fill(AiModels.Runner net, byte[] img, int w, int h, bool[] hole, Part part)
        {
            int bw = part.X1 - part.X0 + 1, bh = part.Y1 - part.Y0 + 1;
            int margin = Math.Max(64, (int)(0.75 * Math.Max(bw, bh)));
            int cx0 = Math.Max(0, part.X0 - margin), cy0 = Math.Max(0, part.Y0 - margin);
            int cx1 = Math.Min(w - 1, part.X1 + margin), cy1 = Math.Min(h - 1, part.Y1 + margin);
            double k = Reduction(cx1 - cx0 + 1, cy1 - cy0 + 1, Math.Max(bw, bh));
            if (k <= 1)
            {
                // At full resolution the crop is made a multiple of 8 wide and high (the network needs it), so the
                // photo goes in pixel for pixel, without resampling.
                int cwant = (cx1 - cx0 + 1 + 7) / 8 * 8, hwant = (cy1 - cy0 + 1 + 7) / 8 * 8;
                if (cwant <= w) { cx0 = Math.Clamp(cx0 - (cwant - (cx1 - cx0 + 1)) / 2, 0, w - cwant); cx1 = cx0 + cwant - 1; }
                if (hwant <= h) { cy0 = Math.Clamp(cy0 - (hwant - (cy1 - cy0 + 1)) / 2, 0, h - hwant); cy1 = cy0 + hwant - 1; }
            }
            int cx = cx0, cy = cy0, cw = cx1 - cx0 + 1, ch = cy1 - cy0 + 1;
            int nw = Round8(cw / k), nh = Round8(ch / k);
            float fx = cw / (float)nw, fy = ch / (float)nh;   // photo pixels per network pixel

            var image = new float[3 * nw * nh];
            var mask = new float[nw * nh];
            int plane = nw * nh;
            Parallel.For(0, nh, y =>
            {
                float sy = (y + 0.5f) * fy - 0.5f;
                int py0 = cy + (int)(y * fy), py1 = cy + Math.Max((int)((y + 1) * fy) - 1, (int)(y * fy));
                for (int x = 0; x < nw; x++)
                {
                    float sx = (x + 0.5f) * fx - 0.5f;
                    Sample(img, w, h, cx + sx, cy + sy, out float b, out float g, out float r);
                    int o = y * nw + x;
                    image[o] = r / 255f; image[plane + o] = g / 255f; image[2 * plane + o] = b / 255f;
                    // A network pixel is part of the hole when any photo pixel under it is.
                    int px0 = cx + (int)(x * fx), px1 = cx + Math.Max((int)((x + 1) * fx) - 1, (int)(x * fx));
                    bool inside = false;
                    for (int yy = py0; yy <= py1 && !inside; yy++)
                        for (int xx = px0; xx <= px1; xx++)
                            if (hole[Math.Min(h - 1, yy) * w + Math.Min(w - 1, xx)]) { inside = true; break; }
                    mask[o] = inside ? 1 : 0;
                }
            });
            var output = net.Run(new[] { ("image", image, new long[] { 1, 3, nh, nw }), ("mask", mask, new long[] { 1, 1, nh, nw }) }, "output")[0];

            // Back to the size of the crop, blended in with a soft edge: inside the hole the network's pixels, at the
            // border a short transition, outside the photo as it was.
            const int feather = 2;
            int fx0 = Math.Max(cx, part.X0 - feather), fy0 = Math.Max(cy, part.Y0 - feather);
            int fx1 = Math.Min(cx + cw - 1, part.X1 + feather), fy1 = Math.Min(cy + ch - 1, part.Y1 + feather);
            var soft = Soften(hole, w, h, fx0, fy0, fx1, fy1, feather);
            int sw = fx1 - fx0 + 1;
            // A crop reduced to fit the network is rebuilt at a lower resolution than the photo, so the fill is
            // smoother than its surroundings: the fine texture of the photo next to the hole (gravel, grass, grain)
            // is carried into it, on top of the colours and shapes rebuilt by the network.
            float scale = Math.Max(fx, fy);
            var detail = scale > 1.2f
                ? FineTexture(img, w, h, hole, part, fx0, fy0, fx1, fy1, scale,
                              (x, y, c) => Bilinear(output, (2 - c) * plane, nw, nh, (x - cx + 0.5f) / fx - 0.5f, (y - cy + 0.5f) / fy - 0.5f))
                : null;
            Parallel.For(fy0, fy1 + 1, y =>
            {
                float ny = (y - cy + 0.5f) / fy - 0.5f;
                for (int x = fx0; x <= fx1; x++)
                {
                    float a = soft[(y - fy0) * sw + (x - fx0)];
                    if (a <= 0) continue;
                    float nx = (x - cx + 0.5f) / fx - 0.5f;
                    int i = (y * w + x) * 4;
                    int d = ((y - fy0) * sw + (x - fx0)) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = Bilinear(output, (2 - c) * plane, nw, nh, nx, ny);   // network planes are R, G, B
                        if (detail != null) v += detail[d + c];
                        float mixed = img[i + c] + a * (v - img[i + c]);
                        img[i + c] = mixed <= 0 ? (byte)0 : mixed >= 255 ? (byte)255 : (byte)(mixed + 0.5f);
                    }
                }
            });
        }

        /// <summary>
        /// The fine texture (the photo minus a blur a few pixels wide) to add to each pixel of a box around a hole.
        /// The hole is taken in blocks; each block copies, as one piece, the texture of the nearest real area beside
        /// it in the direction whose colours are closest to what the network rebuilt (gravel gets gravel, a wall gets
        /// wall). A whole block at one offset keeps the texture natural (no stripes), and each pixel takes it only as
        /// far as the colours of source and fill agree, so the letters of a sign do not come into a plain wall.
        /// </summary>
        static float[] FineTexture(byte[] img, int w, int h, bool[] hole, Part part, int x0, int y0, int x1, int y1,
                                   float scale, Func<int, int, int, float> fill)
        {
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            int r = Math.Clamp((int)Math.Round(scale * 0.75f), 1, 6);
            int span = Math.Max(part.X1 - part.X0, part.Y1 - part.Y0) + 64;
            int ax0 = Math.Max(0, x0 - span), ay0 = Math.Max(0, y0 - span), ax1 = Math.Min(w - 1, x1 + span), ay1 = Math.Min(h - 1, y1 + span);
            int aw = ax1 - ax0 + 1, ah = ay1 - ay0 + 1;
            var mean = BoxMeanRgb(img, w, ax0, ay0, aw, ah, r);
            float Mean(int x, int y, int c) => mean[((y - ay0) * aw + (x - ax0)) * 3 + c];
            bool Real(int x, int y) => x >= ax0 && y >= ay0 && x <= ax1 && y <= ay1 && !hole[y * w + x];

            // How strong the texture of the photo is around the hole: what is copied is clipped at 2.5 times it.
            double sum2 = 0;
            long count = 0;
            for (int y = ay0; y <= ay1; y += 3)
                for (int x = ax0; x <= ax1; x += 3)
                    if (!hole[y * w + x])
                        for (int c = 0; c < 3; c++) { float v = img[(y * w + x) * 4 + c] - Mean(x, y, c); sum2 += v * v; count++; }
            if (count < 100) return null;
            float cap = (float)(2.5 * Math.Sqrt(sum2 / count));
            float amount = Math.Clamp(0.5f + 0.5f * (1 - 1 / scale), 0, 1);

            const int B = 48, gap = 10;
            var dirs = new (int X, int Y)[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) };
            var detail = new float[bw * bh * 3];
            int blocksX = (bw + B - 1) / B, blocksY = (bh + B - 1) / B;
            Parallel.For(0, blocksX * blocksY, b =>
            {
                int bx0 = x0 + b % blocksX * B, by0 = y0 + b / blocksX * B;
                int bx1 = Math.Min(x1, bx0 + B - 1), by1 = Math.Min(y1, by0 + B - 1);
                bool any = false;
                for (int y = by0; y <= by1 && !any; y += 4)
                    for (int x = bx0; x <= bx1; x += 4)
                        if (hole[y * w + x]) { any = true; break; }
                if (!any) return;
                int cxb = (bx0 + bx1) / 2, cyb = (by0 + by1) / 2;

                // For each direction, the shift that takes the block just past the edge of the hole.
                (int Dx, int Dy) best = (0, 0);
                double bestCost = double.MaxValue;
                foreach (var d in dirs)
                {
                    int dist = 0;
                    while (dist < span)
                    {
                        int px = cxb + d.X * dist, py = cyb + d.Y * dist;
                        if (px < ax0 || py < ay0 || px > ax1 || py > ay1) { dist = -1; break; }
                        if (!hole[py * w + px]) break;
                        dist += 4;
                    }
                    if (dist < 0 || dist >= span) continue;
                    int dx = d.X * (dist + B / 2 + gap), dy = d.Y * (dist + B / 2 + gap);
                    double cost = 0;
                    int n = 0, bad = 0;
                    for (int y = by0; y <= by1; y += 6)
                        for (int x = bx0; x <= bx1; x += 6)
                        {
                            if (!hole[y * w + x]) continue;
                            n++;
                            if (!Real(x + dx, y + dy)) { bad++; continue; }
                            for (int c = 0; c < 3; c++) cost += Math.Abs(Mean(x + dx, y + dy, c) - fill(x, y, c));
                        }
                    if (n == 0 || bad > n * 0.3) continue;
                    double total = cost / Math.Max(1, (n - bad) * 3) + 30.0 * bad / n + 0.02 * dist;
                    if (total < bestCost) { bestCost = total; best = (dx, dy); }
                }
                if (bestCost == double.MaxValue || bestCost > 40) return;   // nothing alike nearby: no texture

                for (int y = by0; y <= by1; y++)
                    for (int x = bx0; x <= bx1; x++)
                    {
                        int sx = x + best.Dx, sy = y + best.Dy;
                        if (!hole[y * w + x] || !Real(sx, sy)) continue;
                        // As much texture as the colours agree: none where the source is another material.
                        float diff = 0;
                        for (int c = 0; c < 3; c++) diff += Math.Abs(Mean(sx, sy, c) - fill(x, y, c));
                        float k = Math.Clamp(1 - diff / 3 / 30, 0, 1) * amount;
                        if (k <= 0) continue;
                        int o = ((y - y0) * bw + (x - x0)) * 3, i = (sy * w + sx) * 4;
                        for (int c = 0; c < 3; c++) detail[o + c] = Math.Clamp(img[i + c] - Mean(sx, sy, c), -cap, cap) * k;
                    }
            });
            return detail;
        }

        /// <summary>Mean B, G, R over a (2r+1)² box around each pixel of a region (summed-area tables).</summary>
        static float[] BoxMeanRgb(byte[] img, int w, int x0, int y0, int bw, int bh, int r)
        {
            var sums = new double[3][];
            for (int c = 0; c < 3; c++) sums[c] = new double[(bw + 1) * (bh + 1)];
            for (int y = 0; y < bh; y++)
            {
                double s0 = 0, s1 = 0, s2 = 0;
                for (int x = 0; x < bw; x++)
                {
                    int i = ((y0 + y) * w + x0 + x) * 4;
                    s0 += img[i]; s1 += img[i + 1]; s2 += img[i + 2];
                    int o = (y + 1) * (bw + 1) + x + 1, up = y * (bw + 1) + x + 1;
                    sums[0][o] = sums[0][up] + s0; sums[1][o] = sums[1][up] + s1; sums[2][o] = sums[2][up] + s2;
                }
            }
            var mean = new float[bw * bh * 3];
            Parallel.For(0, bh, y =>
            {
                for (int x = 0; x < bw; x++)
                {
                    int qx0 = Math.Max(0, x - r), qx1 = Math.Min(bw, x + r + 1), qy0 = Math.Max(0, y - r), qy1 = Math.Min(bh, y + r + 1);
                    double n = (qx1 - qx0) * (double)(qy1 - qy0);
                    for (int c = 0; c < 3; c++)
                    {
                        var t = sums[c];
                        mean[(y * bw + x) * 3 + c] = (float)((t[qy1 * (bw + 1) + qx1] - t[qy0 * (bw + 1) + qx1] - t[qy1 * (bw + 1) + qx0] + t[qy0 * (bw + 1) + qx0]) / n);
                    }
                }
            });
            return mean;
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

        static float Bilinear(float[] p, int offset, int pw, int ph, float x, float y)
        {
            x = Math.Clamp(x, 0, pw - 1); y = Math.Clamp(y, 0, ph - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(pw - 1, x0 + 1), y1 = Math.Min(ph - 1, y0 + 1);
            float fx = x - x0, fy = y - y0;
            float a = p[offset + y0 * pw + x0], b = p[offset + y0 * pw + x1], c = p[offset + y1 * pw + x0], d = p[offset + y1 * pw + x1];
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }

        /// <summary>
        /// Adds to the hole the shadow of an object: starting from the object, the pixels next to and under it that are
        /// clearly darker than the ground around them but of the same colour (a shadow darkens, it does not change
        /// the colour). When what is found is larger than the object itself it is something else, and nothing is added.
        /// </summary>
        static void AddShadow(byte[] img, int w, int h, bool[] hole, Part p)
        {
            int bw = p.X1 - p.X0 + 1, bh = p.Y1 - p.Y0 + 1;
            int sx0 = Math.Max(0, (int)(p.X0 - 0.75 * bh)), sx1 = Math.Min(w - 1, (int)(p.X1 + 0.75 * bh));
            // Shadows lie on the ground: around the base of the object and beyond, not behind its upper part.
            int sy0 = Math.Max(0, (int)(p.Y1 - 0.2 * bh)), sy1 = Math.Min(h - 1, (int)(p.Y1 + 0.35 * bh));
            if (sx1 <= sx0 || sy1 <= sy0) return;
            int boxW = sx1 - sx0 + 1, boxH = sy1 - sy0 + 1;
            int r = Math.Clamp(Math.Max(bw, bh) / 60 + 2, 2, 12);
            var meanL = BoxMean(img, w, sx0, sy0, boxW, boxH, r, out var meanR, out var meanB);

            // The ground around: median brightness and colour of what is not the object in the search area.
            var lums = new List<float>();
            var reds = new List<float>();
            var blues = new List<float>();
            for (int y = sy0; y <= sy1; y += 2)
                for (int x = sx0; x <= sx1; x += 2)
                {
                    if (hole[y * w + x]) continue;
                    int j = (y - sy0) * boxW + (x - sx0);
                    lums.Add(meanL[j]); reds.Add(meanR[j]); blues.Add(meanB[j]);
                }
            if (lums.Count < 100) return;
            float refL = Median(lums), refR = Median(reds), refB = Median(blues);
            bool IsShadow(int i)
            {
                int j = (i / w - sy0) * boxW + (i % w - sx0);
                float l = meanL[j], rr = meanR[j], b = meanB[j];
                return l < 0.7f * refL && l > 0.06f * refL && Math.Abs(rr - refR) < 0.05f && b - refB > -0.04f && b - refB < 0.09f;
            }

            // Grown from the edge of the object.
            var found = new List<int>();
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            for (int y = sy0; y <= sy1; y++)
                for (int x = sx0; x <= sx1; x++)
                {
                    int i = y * w + x;
                    if (hole[i] || !IsShadow(i)) continue;
                    bool edge = (x > 0 && hole[i - 1]) || (x < w - 1 && hole[i + 1]) || (y > 0 && hole[i - w]) || (y < h - 1 && hole[i + w]);
                    if (edge && seen.Add(i)) stack.Push(i);
                }
            long limit = Math.Max(400, (long)(0.9 * p.Count));
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                found.Add(i);
                if (found.Count > limit) return;   // too large for a shadow: a dark area of the photo
                void Try(int j, int jx, int jy)
                {
                    if (jx < sx0 || jx > sx1 || jy < sy0 || jy > sy1 || hole[j] || !seen.Add(j) || !IsShadow(j)) return;
                    stack.Push(j);
                }
                if (x > 0) Try(i - 1, x - 1, y);
                if (x < w - 1) Try(i + 1, x + 1, y);
                if (y > 0) Try(i - w, x, y - 1);
                if (y < h - 1) Try(i + w, x, y + 1);
            }
            if (found.Count < 20) return;

            // The soft edge of a shadow (the penumbra) goes too.
            var shadowPart = new Part();
            var label = new int[w * h];
            foreach (int i in found)
            {
                label[i] = 1;
                int x = i % w, y = i / w;
                if (x < shadowPart.X0) shadowPart.X0 = x; if (x > shadowPart.X1) shadowPart.X1 = x;
                if (y < shadowPart.Y0) shadowPart.Y0 = y; if (y > shadowPart.Y1) shadowPart.Y1 = y;
            }
            shadowPart.Count = found.Count;
            DilateInto(label, 1, shadowPart, Math.Clamp(Math.Max(bw, bh) / 40 + 3, 3, 24), hole, w, h);
        }

        /// <summary>
        /// Mean luminance and colour (red and blue shares) over a (2r+1)² box around each pixel of a region of the
        /// image, with summed-area tables.
        /// </summary>
        static float[] BoxMean(byte[] img, int w, int x0, int y0, int bw, int bh, int r, out float[] red, out float[] blue)
        {
            int h = img.Length / 4 / w;
            int ax0 = Math.Max(0, x0 - r), ay0 = Math.Max(0, y0 - r), ax1 = Math.Min(w - 1, x0 + bw - 1 + r), ay1 = Math.Min(h - 1, y0 + bh - 1 + r);
            int aw = ax1 - ax0 + 1, ah = ay1 - ay0 + 1;
            var sl = new double[(aw + 1) * (ah + 1)];
            var sr = new double[sl.Length];
            var sb = new double[sl.Length];
            for (int y = 0; y < ah; y++)
            {
                double rl = 0, rrd = 0, rb = 0;
                for (int x = 0; x < aw; x++)
                {
                    int i = ((ay0 + y) * w + ax0 + x) * 4;
                    float b = img[i], g = img[i + 1], rd = img[i + 2], sum = rd + g + b + 1;
                    rl += 0.114 * b + 0.587 * g + 0.299 * rd; rrd += rd / sum; rb += b / sum;
                    int o = (y + 1) * (aw + 1) + x + 1, up = y * (aw + 1) + x + 1;
                    sl[o] = sl[up] + rl; sr[o] = sr[up] + rrd; sb[o] = sb[up] + rb;
                }
            }
            var lum = new float[bw * bh];
            red = new float[bw * bh];
            blue = new float[bw * bh];
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int qx0 = Math.Max(ax0, x0 + x - r) - ax0, qx1 = Math.Min(ax1, x0 + x + r) - ax0 + 1;
                    int qy0 = Math.Max(ay0, y0 + y - r) - ay0, qy1 = Math.Min(ay1, y0 + y + r) - ay0 + 1;
                    double n = (qx1 - qx0) * (double)(qy1 - qy0);
                    double S(double[] t) => t[qy1 * (aw + 1) + qx1] - t[qy0 * (aw + 1) + qx1] - t[qy1 * (aw + 1) + qx0] + t[qy0 * (aw + 1) + qx0];
                    int o = y * bw + x;
                    lum[o] = (float)(S(sl) / n); red[o] = (float)(S(sr) / n); blue[o] = (float)(S(sb) / n);
                }
            return lum;
        }

        static float Median(List<float> v)
        {
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>Grows the part with the given label by r pixels (square) and adds it to the hole.</summary>
        static void DilateInto(int[] label, int id, Part p, int r, bool[] hole, int w, int h)
        {
            int bx0 = Math.Max(0, p.X0 - r), by0 = Math.Max(0, p.Y0 - r), bx1 = Math.Min(w - 1, p.X1 + r), by1 = Math.Min(h - 1, p.Y1 + r);
            int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
            var tmp = new bool[bw * bh];
            // Along the rows: a pixel is reached when a pixel of the part lies within r of it.
            Parallel.For(0, bh, y =>
            {
                int row = (by0 + y) * w, last = int.MinValue / 2;
                for (int x = -r; x < bw; x++)
                {
                    int sx = bx0 + x + r;
                    if (sx < w && sx >= 0 && label[row + sx] == id) last = x + r;
                    if (x >= 0 && x - last <= r) tmp[y * bw + x] = true;
                }
            });
            // Then along the columns.
            Parallel.For(0, bw, x =>
            {
                int last = int.MinValue / 2;
                for (int y = -r; y < bh; y++)
                {
                    int sy = y + r;
                    if (sy < bh && tmp[sy * bw + x]) last = sy;
                    if (y >= 0 && y - last <= r) hole[(by0 + y) * w + bx0 + x] = true;
                }
            });
        }

        /// <summary>The separate parts of the hole (4-connected); label, when given, gets the part number (from 1) of each pixel.</summary>
        static List<Part> Components(bool[] m, int w, int h, int[] label)
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
                    if (label != null) label[i] = parts.Count + 1;
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
