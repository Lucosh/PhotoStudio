using System;
using System.Collections.Generic;
using System.Linq;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    /// <summary>
    /// "Luce intelligente": one click that fixes the light of a photo. Global exposure and tones as the
    /// Automatico of Camera Raw, then local light masks where the photo needs them (dark areas, bright areas,
    /// sky, faces or subject), then a check on the finished photo that takes back what the sum of them overdid.
    /// Everything stays editable: the masks appear in the Maschere tab.
    /// </summary>
    public static class SmartLight
    {
        /// <summary>More faces than this get one pool of light for the whole group instead of one each.</summary>
        const int MaxFaceMasks = 4;

        /// <summary>True for the masks added by Luce intelligente (also those saved before the Auto flag existed).</summary>
        public static bool IsAuto(LocalMask m) => m.Auto || (m.Name != null && m.Name.StartsWith("Luce auto:", StringComparison.Ordinal));

        /// <summary>The zone of an automatic mask ("ombre", "cielo"...), as shown in its name.</summary>
        public static string ZoneOf(LocalMask m)
        {
            int i = m.Name?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
            return i >= 0 ? m.Name.Substring(i + 2) : m.Name ?? "";
        }

        static string MaskName(string zone) => T("Luce auto: {0}", zone);

        static bool IsShadows(LocalMask m) => IsAuto(m) && m.Kind == MaskKind.Luminance && m.Low <= 0;
        static bool IsHighlights(LocalMask m) => IsAuto(m) && m.Kind == MaskKind.Luminance && m.High >= 1;
        static bool IsSky(LocalMask m) => IsAuto(m) && m.Kind == MaskKind.Sky;

        static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <param name="intensity">0..150 %: how strongly to apply the correction (100 = normal).</param>
        public static RawSettings Apply(RawImage img, RawSettings current, double intensity = 100)
        {
            double k = Math.Clamp(intensity, 0, 150) / 100;
            var scene = SceneAnalysis.Of(img);   // asked of the image as received: the detectors want more pixels than the measures
            var small = img.Downscale(480);
            var start = current.Clone();
            start.Masks.RemoveAll(IsAuto);   // redo, don't stack
            var auto = RawDevelop.AutoTone(img, start, out var tone);   // the very values of Automatico
            var r = RawDevelop.Blend(start, auto, intensity);
            if (k <= 0) return r;

            // Measure the photo after the global correction: only what is inside the crop counts.
            int w = small.Width, h = small.Height, n = w * h;
            var lum = RawDevelop.Brightness(small, r);
            double cropL = r.CropL, cropT = r.CropT, cropW = Math.Max(0.02, r.CropR - r.CropL), cropH = Math.Max(0.02, r.CropB - r.CropT);
            var sky = scene.Sky;
            var skyAt = sky != null ? new float[n] : null;
            double sum = 0, sum2 = 0, darkSum = 0, skySum = 0, skyArea = 0;
            int count = 0, dark = 0, bright = 0;
            double centerSum = 0, borderSum = 0;
            int centerCount = 0, borderCount = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    float L = lum[p];
                    if (L < 0) continue;
                    float inSky = skyAt != null ? (skyAt[p] = sky.Weight(small.Data, x, y, w, h)) : 0;
                    count++;
                    sum += L; sum2 += L * L;
                    skySum += inSky * L; skyArea += inSky;
                    if (L < 0.2f) { dark++; darkSum += L; }
                    if (L > 0.85f && inSky < 0.5f) bright++;   // the sky has a mask of its own
                    double u = ((x + 0.5) / w - cropL) / cropW - 0.5, v = ((y + 0.5) / h - cropT) / cropH - 0.5;
                    if (Math.Abs(u) < 0.2 && Math.Abs(v) < 0.2) { centerSum += L; centerCount++; }
                    else if (Math.Abs(u) > 0.35 || Math.Abs(v) > 0.35) { borderSum += L; borderCount++; }
                }
            if (count == 0) return r;
            double mean = sum / count, std = Math.Sqrt(Math.Max(0, sum2 / count - mean * mean));
            double fracDark = dark / (double)count, fracBright = bright / (double)count;
            double meanDark = dark > 0 ? darkSum / dark : 0;
            double cx = (r.CropL + r.CropR) / 2, cy = (r.CropT + r.CropB) / 2;

            // 1) Dark areas: open them up with a zone-of-light mask (the rest of the photo is untouched).
            //    Not in a photo that is dark on purpose, where the shadows are the photo.
            if (fracDark > 0.06)
            {
                double ev = Math.Clamp(0.25 + (0.12 - meanDark) * 3, 0.15, 0.7) * (1 - 0.7 * tone.LowKey) * k;
                if (ev >= 0.08)
                    r.Masks.Add(new LocalMask
                    {
                        Name = MaskName(T("ombre")), Auto = true, Kind = MaskKind.Luminance, Low = 0, High = 0.28, Feather = 0.15,
                        Exposure = Math.Round(ev, 2), Clarity = Math.Round(6 * k),
                    });
            }

            // 2) Bright areas: bring back detail and colour.
            double lightsEv = 0;
            if (fracBright > 0.04)
            {
                lightsEv = -Math.Clamp(0.2 + fracBright * 1.5, 0.2, 0.6) * k;
                r.Masks.Add(new LocalMask
                {
                    Name = MaskName(T("luci")), Auto = true, Kind = MaskKind.Luminance, Low = 0.75, High = 1, Feather = 0.12,
                    Exposure = Math.Round(lightsEv, 2), Saturation = Math.Round(8 * k), Contrast = Math.Round(6 * k),
                });
            }

            // 3) Sky: the sky itself, found in the photo, not a band at the top. The whiter it is the more it is
            //    held back; a sky that is already deep only gets colour and contrast. Where the mask of the bright
            //    areas already works on it, the sky mask does only the rest.
            if (sky != null && skyArea / count > 0.03)
            {
                double skyMean = skySum / skyArea;
                double pull = (0.15 * SmoothStep(0.35, 0.55, skyMean) + 0.45 * SmoothStep(0.62, 0.92, skyMean)) * sky.Confidence * k;
                pull = Math.Max(0, pull + lightsEv * SmoothStep(0.7, 0.85, skyMean));
                r.Masks.Add(new LocalMask
                {
                    Name = MaskName(T("cielo")), Auto = true, Kind = MaskKind.Sky,
                    Exposure = 0 - Math.Round(pull, 2), Highlights = 0 - Math.Round(25 * SmoothStep(0.7, 0.9, skyMean) * k),
                    Saturation = Math.Round((6 + 8 * sky.Blue) * k), Contrast = Math.Round(8 * k),
                });
            }

            // 4) People: a pool of light on the faces that are still dark (or a veil on those that burn).
            //    Without faces, a centre darker than the edges is taken for the subject.
            var faces = FacesInCrop(scene, r, lum, w, h);
            if (faces.Count > 0)
            {
                // Mask exposure that brings the skin of a face (brightness L) into the comfortable range, done only in part.
                double EvFor(double L) =>
                    L < 0.48 ? Math.Clamp(2.2 * Math.Log2(0.55 / Math.Max(L, 0.05)), 0.1, 0.7) * 0.7 * k
                    : L > 0.84 ? -Math.Clamp(2.2 * Math.Log2(L / 0.78), 0.1, 0.5) * 0.7 * k
                    : 0;
                if (faces.Count <= MaxFaceMasks)
                {
                    int number = 0;
                    foreach (var f in faces)
                    {
                        number++;
                        double ev = EvFor(f.Lum);
                        if (ev == 0) continue;
                        r.Masks.Add(new LocalMask
                        {
                            Name = MaskName(faces.Count == 1 ? T("viso") : T("viso {0}", number)), Auto = true, Kind = MaskKind.Radial,
                            X1 = f.X, Y1 = f.Y, RX = f.W * 1.1, RY = f.H * 1.2, Feather = 0.75, Exposure = Math.Round(ev, 2),
                        });
                    }
                }
                else
                {
                    double ev = EvFor(faces.Sum(f => f.Lum * f.W * f.H) / faces.Sum(f => f.W * f.H));
                    double left = faces.Min(f => f.X - f.W / 2), right = faces.Max(f => f.X + f.W / 2);
                    double top = faces.Min(f => f.Y - f.H / 2), bottom = faces.Max(f => f.Y + f.H / 2);
                    if (ev != 0)
                        r.Masks.Add(new LocalMask
                        {
                            Name = MaskName(T("visi")), Auto = true, Kind = MaskKind.Radial,
                            X1 = (left + right) / 2, Y1 = (top + bottom) / 2, RX = (right - left) * 0.75, RY = (bottom - top) * 0.9,
                            Feather = 0.7, Exposure = Math.Round(ev, 2),
                        });
                }
            }
            else if (centerCount > 0 && borderCount > 0 && centerSum / centerCount < borderSum / borderCount - 0.06)
            {
                r.Masks.Add(new LocalMask
                {
                    Name = MaskName(T("soggetto")), Auto = true, Kind = MaskKind.Radial, X1 = cx, Y1 = cy, RX = 0.3 * cropW, RY = 0.35 * cropH, Feather = 0.7,
                    Exposure = Math.Round(0.25 * k, 2),
                });
            }

            // Flat, hazy photos: a touch of dehaze.
            if (std < 0.14 && mean > 0.45) r.Dehaze = Math.Round(start.Dehaze + 12 * k);

            if (r.Masks.Any(IsAuto)) Verify(small, r, lum, skyAt, mean, std, tone.LowKey, k);

            // Noise reduction for all the light that was added, masks included.
            var (luma, color) = RawDevelop.AutoNoise(img, r);
            r.NoiseLuma = Math.Max(start.NoiseLuma, Math.Round(luma * Math.Min(1, k)));
            r.NoiseColor = Math.Max(start.NoiseColor, Math.Round(color * Math.Min(1, k)));
            return r;
        }

        /// <summary>
        /// The faces inside the crop, largest first: centre and size in the straightened photo (0..1, as the masks),
        /// and brightness of the skin after the global correction.
        /// </summary>
        static List<(double X, double Y, double W, double H, double Lum)> FacesInCrop(SceneAnalysis scene, RawSettings s, float[] lum, int w, int h)
        {
            var found = new List<(double X, double Y, double W, double H, double Lum)>();
            double a = s.Angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            foreach (var f in scene.Faces)
            {
                var (x0, y0, x1, y1) = RawDevelop.FaceCore(f, w, h);
                var tones = new int[256];
                int count = 0, outside = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float L = lum[y * w + x];
                        if (L < 0) outside++; else { tones[(int)(L * 255 + 0.5f)]++; count++; }
                    }
                if (count == 0 || outside > count) continue;   // cropped away
                // Same rotation about the centre as the straighten tool.
                double qx = (f.Cx - 0.5) * w, qy = (f.Cy - 0.5) * h;
                found.Add(((cos * qx - sin * qy) / w + 0.5, (sin * qx + cos * qy) / h + 0.5, f.W, f.H, RawDevelop.SkinLevel(tones) / 255));
            }
            return found;
        }

        /// <summary>
        /// The check on the finished photo. The masks add their light to sliders that had already opened the
        /// shadows and held the highlights: where the sum goes too far the global slider gives way first (the mask
        /// does the same job only where it is needed), then the masks themselves.
        /// </summary>
        /// <param name="before">Brightness after the global correction alone, as measured by <see cref="Apply"/>.</param>
        static void Verify(RawImage small, RawSettings r, float[] before, float[] skyAt, double meanBefore, double stdBefore, double lowKey, double k)
        {
            double extra = Math.Max(0, k - 1);
            (double Mean, double Std, double Lights) Measure()
            {
                var lum = RawDevelop.Brightness(small, r);
                double sum = 0, sum2 = 0, lightSum = 0;
                int count = 0, lights = 0;
                for (int p = 0; p < lum.Length; p++)
                {
                    float L = lum[p];
                    if (L < 0 || before[p] < 0) continue;
                    count++;
                    sum += L; sum2 += L * L;
                    if (before[p] > 0.85f && (skyAt == null || skyAt[p] < 0.5f)) { lights++; lightSum += L; }
                }
                double mean = sum / Math.Max(1, count);
                return (mean, Math.Sqrt(Math.Max(0, sum2 / Math.Max(1, count) - mean * mean)), lights > 0 ? lightSum / lights : 1);
            }

            // Too bright: the masks were meant to move the light, not to raise the whole photo again.
            double meanLimit = meanBefore + 0.015 * (1 + 2 * extra) * (1 - 0.5 * lowKey);
            bool TooBright() => Measure().Mean > meanLimit;
            double shadows = r.Shadows;
            var lifting = r.Masks.Where(m => IsAuto(m) && m.Exposure > 0).Select(m => (Mask: m, Exposure: m.Exposure)).ToList();
            if (lifting.Count > 0 && Most(t => r.Shadows = Math.Round(shadows * (0.4 + 0.6 * t)), TooBright, shadows > 0) == 0)
                Most(t => { foreach (var l in lifting) l.Mask.Exposure = Math.Round(l.Exposure * (0.35 + 0.65 * t), 2); }, TooBright);

            // Too dull: the lights of the photo (the sky apart, which has its own measure) turn grey.
            double lightLimit = 0.76 - 0.05 * extra;
            bool TooDull() => Measure().Lights < lightLimit;
            double highlights = r.Highlights;
            var holding = r.Masks.Where(m => IsAuto(m) && m.Exposure < 0 && m.Kind == MaskKind.Luminance).Select(m => (Mask: m, Exposure: m.Exposure)).ToList();
            if (holding.Count > 0 && Most(t => r.Highlights = Math.Round(highlights * (0.5 + 0.5 * t)), TooDull, highlights < 0) == 0)
                Most(t => { foreach (var l in holding) l.Mask.Exposure = Math.Round(l.Exposure * (0.35 + 0.65 * t), 2); }, TooDull);

            // Shadows up and lights down flatten the photo: give back the contrast that was lost.
            double lost = stdBefore - Measure().Std;
            if (lost > 0.012) r.Contrast = Math.Min(40, r.Contrast + Math.Round(Math.Min(10, lost * 250)));
        }

        /// <summary>
        /// The largest share (0..1) of a correction that does not go too far, found by bisection; the correction
        /// is left applied with that share. 0 also when even the smallest share is too much.
        /// </summary>
        static double Most(Action<double> apply, Func<bool> tooMuch, bool applicable = true)
        {
            if (!applicable) return tooMuch() ? 0 : 1;
            apply(1);
            if (!tooMuch()) return 1;
            apply(0);
            if (tooMuch()) return 0;
            double lo = 0, hi = 1;
            for (int i = 0; i < 4; i++)
            {
                double mid = (lo + hi) / 2;
                apply(mid);
                if (tooMuch()) hi = mid; else lo = mid;
            }
            apply(lo);
            return lo;
        }

        /// <summary>
        /// Photos of the same scene must look alike: gives the photos of a series the same base correction (the
        /// median of what each would get on its own) and the same zone masks. Sky and faces stay where each photo
        /// has them. The exposure allows for the different camera settings of each shot.
        /// </summary>
        /// <param name="series">The settings found for each photo, with its camera exposure (EV at ISO 100; NaN = unknown).</param>
        public static void Harmonize(IReadOnlyList<(RawSettings Settings, double Ev)> series)
        {
            if (series.Count < 2) return;
            var all = series.Select(p => p.Settings).ToList();
            double Median(IEnumerable<double> values)
            {
                var v = values.OrderBy(x => x).ToArray();
                return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
            }
            void Same(Func<RawSettings, double> get, Action<RawSettings, double> set)
            {
                double m = Math.Round(Median(all.Select(get)));
                foreach (var s in all) set(s, m);
            }

            // The same light shot with a faster time needs that much more exposure in development:
            // what the photos share is exposure minus camera exposure.
            bool known = series.All(p => !double.IsNaN(p.Ev));
            double common = Median(series.Select(p => p.Settings.Exposure - (known ? p.Ev : 0)));
            foreach (var p in series)
                p.Settings.Exposure = Math.Clamp(Math.Round((common + (known ? p.Ev : 0)) * 20) / 20, -5, 5);
            Same(s => s.Contrast, (s, v) => s.Contrast = v);
            Same(s => s.Highlights, (s, v) => s.Highlights = v);
            Same(s => s.Shadows, (s, v) => s.Shadows = v);
            Same(s => s.Whites, (s, v) => s.Whites = v);
            Same(s => s.Blacks, (s, v) => s.Blacks = v);
            Same(s => s.Vibrance, (s, v) => s.Vibrance = v);
            Same(s => s.Saturation, (s, v) => s.Saturation = v);
            Same(s => s.Dehaze, (s, v) => s.Dehaze = v);
            Same(s => s.NoiseLuma, (s, v) => s.NoiseLuma = v);
            Same(s => s.NoiseColor, (s, v) => s.NoiseColor = v);

            // Zone masks: the one of the majority for everybody, or for nobody (shadows end up first, as Apply adds them).
            foreach (var zone in new Predicate<LocalMask>[] { IsHighlights, IsShadows })
            {
                var with = all.Select(s => s.Masks.Find(zone)).Where(m => m != null).OrderBy(m => m.Exposure).ToList();
                var model = with.Count * 2 >= all.Count ? with[with.Count / 2] : null;
                foreach (var s in all)
                {
                    s.Masks.RemoveAll(zone);
                    if (model == null) continue;
                    int firstAuto = s.Masks.FindIndex(IsAuto);
                    s.Masks.Insert(firstAuto < 0 ? s.Masks.Count : firstAuto, model.Clone());
                }
            }

            // The sky is where each photo has it, but it is treated in the same way.
            var skies = all.SelectMany(s => s.Masks.Where(IsSky)).ToList();
            if (skies.Count > 1)
            {
                double exposure = Math.Round(Median(skies.Select(m => m.Exposure)), 2), highlights = Math.Round(Median(skies.Select(m => m.Highlights)));
                double saturation = Math.Round(Median(skies.Select(m => m.Saturation))), contrast = Math.Round(Median(skies.Select(m => m.Contrast)));
                foreach (var m in skies) { m.Exposure = exposure; m.Highlights = highlights; m.Saturation = saturation; m.Contrast = contrast; }
            }
        }
    }
}
