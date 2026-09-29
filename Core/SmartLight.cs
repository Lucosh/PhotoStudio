using System;
using System.Linq;

namespace PhotoStudio.Core
{
    /// <summary>
    /// "Luce intelligente": one click that fixes the light of a photo. Global exposure and tones as the
    /// Automatico of Camera Raw, then local light masks where the photo needs them (dark areas, bright areas,
    /// sky, subject). Everything stays editable: the masks appear in the Maschere tab.
    /// </summary>
    public static class SmartLight
    {
        public const string Prefix = "Luce auto";

        /// <param name="intensity">0..150 %: how strongly to apply the correction (100 = normal).</param>
        public static RawSettings Apply(RawImage img, RawSettings current, double intensity = 100)
        {
            double k = Math.Clamp(intensity, 0, 150) / 100;
            var small = img.Downscale(480);
            var start = current.Clone();
            start.Masks.RemoveAll(m => m.Name != null && m.Name.StartsWith(Prefix, StringComparison.Ordinal));   // redo, don't stack
            var auto = RawDevelop.AutoTone(small, start);

            var r = start.Clone();
            void Mix(Func<RawSettings, double> get, Action<RawSettings, double> set, int decimals) =>
                set(r, Math.Round(get(start) + (get(auto) - get(start)) * k, decimals));
            Mix(x => x.Exposure, (x, v) => x.Exposure = v, 2);
            Mix(x => x.Contrast, (x, v) => x.Contrast = v, 0);
            Mix(x => x.Highlights, (x, v) => x.Highlights = v, 0);
            Mix(x => x.Shadows, (x, v) => x.Shadows = v, 0);
            Mix(x => x.Whites, (x, v) => x.Whites = v, 0);
            Mix(x => x.Blacks, (x, v) => x.Blacks = v, 0);
            Mix(x => x.Vibrance, (x, v) => x.Vibrance = v, 0);
            Mix(x => x.Saturation, (x, v) => x.Saturation = v, 0);
            if (k <= 0) return r;

            // Measure the photo after the global correction.
            var px = RawDevelop.Render(small, r);
            int w = small.Width, h = small.Height, n = w * h;
            var lum = new float[n];
            double sum = 0, sum2 = 0, darkSum = 0;
            int dark = 0, bright = 0;
            var rowMean = new double[h];
            double topBlue = 0;
            int topCount = 0;
            double centerSum = 0, borderSum = 0;
            int centerCount = 0, borderCount = 0;
            for (int y = 0; y < h; y++)
            {
                double row = 0;
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x, i = p * 4;
                    float L = (0.2126f * px[i + 2] + 0.7152f * px[i + 1] + 0.0722f * px[i]) / 255f;
                    lum[p] = L;
                    sum += L; sum2 += L * L; row += L;
                    if (L < 0.2f) { dark++; darkSum += L; }
                    if (L > 0.85f) bright++;
                    if (y < h / 4) { topBlue += (px[i] - px[i + 2]) / 255.0; topCount++; }
                    double u = (x + 0.5) / w - 0.5, v = (y + 0.5) / h - 0.5;
                    if (Math.Abs(u) < 0.2 && Math.Abs(v) < 0.2) { centerSum += L; centerCount++; }
                    else if (Math.Abs(u) > 0.35 || Math.Abs(v) > 0.35) { borderSum += L; borderCount++; }
                }
                rowMean[y] = row / w;
            }
            double mean = sum / n, std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
            double fracDark = dark / (double)n, fracBright = bright / (double)n;
            double meanDark = dark > 0 ? darkSum / dark : 0;
            double cx = (r.CropL + r.CropR) / 2, cy = (r.CropT + r.CropB) / 2;

            // 1) Dark areas: open them up with a zone-of-light mask (the rest of the photo is untouched).
            if (fracDark > 0.06)
            {
                double ev = Math.Clamp(0.25 + (0.12 - meanDark) * 3, 0.15, 0.7) * k;
                r.Masks.Add(new LocalMask
                {
                    Name = Prefix + ": ombre", Kind = MaskKind.Luminance, Low = 0, High = 0.28, Feather = 0.15,
                    Exposure = Math.Round(ev, 2), Clarity = Math.Round(6 * k),
                });
            }

            // 2) Bright areas: bring back detail and colour.
            if (fracBright > 0.04)
            {
                double ev = -Math.Clamp(0.2 + fracBright * 1.5, 0.2, 0.6) * k;
                r.Masks.Add(new LocalMask
                {
                    Name = Prefix + ": luci", Kind = MaskKind.Luminance, Low = 0.75, High = 1, Feather = 0.12,
                    Exposure = Math.Round(ev, 2), Saturation = Math.Round(8 * k), Contrast = Math.Round(6 * k),
                });
            }

            // 3) Sky: a bright (often bluish) band at the top, darker below. A gradient down to the horizon.
            double top = rowMean.Take(Math.Max(1, h / 4)).Average();
            double rest = rowMean.Skip((int)(h * 0.4)).DefaultIfEmpty(mean).Average();
            double blue = topCount > 0 ? topBlue / topCount : 0;
            if (top > rest + 0.12 && (blue > 0.02 || top > 0.75))
            {
                int best = h / 3;
                double drop = double.MinValue;
                int span = Math.Max(2, h / 40);
                for (int y = (int)(h * 0.15); y < (int)(h * 0.7); y++)
                {
                    double above = 0, below = 0;
                    for (int j = 1; j <= span; j++) { above += rowMean[Math.Max(0, y - j)]; below += rowMean[Math.Min(h - 1, y + j)]; }
                    if (above - below > drop) { drop = above - below; best = y; }
                }
                double horizon = Math.Clamp(best / (double)h + 0.05, 0.2, 0.8);
                r.Masks.Add(new LocalMask
                {
                    Name = Prefix + ": cielo", Kind = MaskKind.Linear, X1 = cx, Y1 = r.CropT, X2 = cx, Y2 = horizon,
                    Exposure = Math.Round(-0.3 * k, 2), Saturation = Math.Round(12 * k), Contrast = Math.Round(8 * k),
                });
            }

            // 4) Subject: a centre darker than the edges gets a soft pool of light.
            if (centerCount > 0 && borderCount > 0 && centerSum / centerCount < borderSum / borderCount - 0.06)
            {
                r.Masks.Add(new LocalMask
                {
                    Name = Prefix + ": soggetto", Kind = MaskKind.Radial, X1 = cx, Y1 = cy, RX = 0.3, RY = 0.35, Feather = 0.7,
                    Exposure = Math.Round(0.25 * k, 2),
                });
            }

            // Flat, hazy photos: a touch of dehaze.
            if (std < 0.14 && mean > 0.45) r.Dehaze = Math.Round(start.Dehaze + 12 * k);
            return r;
        }
    }
}
