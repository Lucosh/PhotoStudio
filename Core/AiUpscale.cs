using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>
    /// AI enlargement to twice the width and height with Real-ESRGAN (the small general model, embedded): the network
    /// enlarges 4×, and the result is averaged down to 2×, which keeps its detail and hides its artefacts.
    /// The photo goes through the network in tiles with a margin of context around each, and only the middle of
    /// each tile is kept, so there are no seams and no full-size intermediate buffers.
    /// </summary>
    public static class AiUpscale
    {
        const int Tile = 256, Margin = 16, Core = Tile - 2 * Margin;

        /// <summary>Non-premultiplied BGRA w × h in, BGRA 2w × 2h out. Progress goes from 0 to 1.</summary>
        public static byte[] Upscale2x(byte[] src, int w, int h, Action<double> progress = null, CancellationToken ct = default)
        {
            var net = AiModels.Get(AiModels.Upscaler) ?? throw new InvalidOperationException(Loc.T("La rete per l'ingrandimento AI non si è potuta avviare."));
            int ow = 2 * w, oh = 2 * h;
            var dst = new byte[ow * oh * 4];
            int cols = (w + Core - 1) / Core, rows = (h + Core - 1) / Core, total = cols * rows, done = 0;
            var input = new float[3 * Tile * Tile];
            const int plane = Tile * Tile;
            const int P4 = 4 * Tile * 4 * Tile;   // one output plane, 4× in both directions

            for (int ry = 0; ry < rows; ry++)
                for (int rx = 0; rx < cols; rx++)
                {
                    ct.ThrowIfCancellationRequested();
                    int cx = rx * Core, cy = ry * Core;
                    int x0 = cx - Margin, y0 = cy - Margin;
                    Parallel.For(0, Tile, yy =>
                    {
                        int sy = Mirror(y0 + yy, h);
                        for (int xx = 0; xx < Tile; xx++)
                        {
                            int i = (sy * w + Mirror(x0 + xx, w)) * 4, o = yy * Tile + xx;
                            input[o] = src[i + 2] / 255f;
                            input[plane + o] = src[i + 1] / 255f;
                            input[2 * plane + o] = src[i] / 255f;
                        }
                    });
                    var o4 = net.Run(input, new long[] { 1, 3, Tile, Tile }, "input", "output")[0];

                    // The middle of the tile, averaged from 4× down to 2×.
                    int coreW = Math.Min(Core, w - cx), coreH = Math.Min(Core, h - cy);
                    Parallel.For(0, 2 * coreH, v =>
                    {
                        int tv = 2 * Margin + v, Y = 2 * cy + v;           // row in the 2× tile and in the 2× photo
                        for (int u = 0; u < 2 * coreW; u++)
                        {
                            int tu = 2 * Margin + u, X = 2 * cx + u;
                            int a = 2 * tv * 4 * Tile + 2 * tu, b = a + 4 * Tile;   // the 2 × 2 block of the 4× output
                            int d = (Y * ow + X) * 4;
                            for (int c = 0; c < 3; c++)
                            {
                                int off = c * P4;
                                float val = (o4[off + a] + o4[off + a + 1] + o4[off + b] + o4[off + b + 1]) / 4;
                                dst[d + 2 - c] = val <= 0 ? (byte)0 : val >= 1 ? (byte)255 : (byte)(val * 255 + 0.5f);
                            }
                        }
                    });
                    progress?.Invoke(++done / (double)total);
                }

            // Transparency is enlarged smoothly (bilinear): the network only knows colours.
            Parallel.For(0, oh, Y =>
            {
                float sy = Math.Clamp((Y + 0.5f) / 2 - 0.5f, 0, h - 1);
                int y0 = (int)sy, y1 = Math.Min(h - 1, y0 + 1);
                float fy = sy - y0;
                for (int X = 0; X < ow; X++)
                {
                    float sx = Math.Clamp((X + 0.5f) / 2 - 0.5f, 0, w - 1);
                    int x0 = (int)sx, x1 = Math.Min(w - 1, x0 + 1);
                    float fx = sx - x0;
                    float a00 = src[(y0 * w + x0) * 4 + 3], a01 = src[(y0 * w + x1) * 4 + 3];
                    float a10 = src[(y1 * w + x0) * 4 + 3], a11 = src[(y1 * w + x1) * 4 + 3];
                    float alpha = (a00 + (a01 - a00) * fx) * (1 - fy) + (a10 + (a11 - a10) * fx) * fy;
                    dst[(Y * ow + X) * 4 + 3] = (byte)(alpha + 0.5f);
                }
            });
            return dst;
        }

        static int Mirror(int i, int size)
        {
            if (size == 1) return 0;
            int period = 2 * (size - 1);
            i %= period;
            if (i < 0) i += period;
            return i < size ? i : period - i;
        }
    }
}
