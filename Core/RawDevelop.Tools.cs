using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    // Lightroom-style tools of the develop engine: noise reduction, texture, masks, dehaze, colour mixer,
    // colour grading, vignette, grain and the final straighten/crop.
    public static partial class RawDevelop
    {
        static float SmoothStepF(float e0, float e1, float x)
        {
            if (e1 - e0 < 1e-5f) return x < e0 ? 0 : 1;
            float t = (x - e0) / (e1 - e0);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }

        // ================= Develop = render + straighten/crop =================

        /// <summary>The finished photo: every tool, then straighten and crop.</summary>
        public static (int Width, int Height, byte[] Pixels) Develop(RawImage img, RawSettings s, int maskPreview = -1)
        {
            var px = Render(img, s, maskPreview);
            if (!s.HasGeometry) return (img.Width, img.Height, px);
            return ApplyGeometry(px, img.Width, img.Height, s, true);
        }

        /// <summary>Size of the developed photo.</summary>
        /// <summary>
        /// Largest centred rectangle with the photo's proportions that fits inside the photo rotated by the angle,
        /// as a fraction of the photo (1 = no rotation).
        /// </summary>
        public static double InscribedScale(int w, int h, double angleDeg)
        {
            double t = Math.Abs(angleDeg) * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t);
            return Math.Min(w / (w * c + h * s), h / (w * s + h * c));
        }

        /// <summary>Keeps the crop inside the straightened photo (no empty corners), as Lightroom does.</summary>
        public static void ConstrainCrop(RawSettings s, int w, int h)
        {
            double k = InscribedScale(w, h, s.Angle), lo = 0.5 - k / 2, hi = 0.5 + k / 2;
            s.CropL = Math.Clamp(s.CropL, lo, hi - 0.02);
            s.CropT = Math.Clamp(s.CropT, lo, hi - 0.02);
            s.CropR = Math.Clamp(s.CropR, s.CropL + 0.02, hi);
            s.CropB = Math.Clamp(s.CropB, s.CropT + 0.02, hi);
        }

        /// <summary>
        /// Straightens (rotation about the centre) and crops. With crop = false the whole straightened frame is
        /// returned and the areas outside the photo are dark grey (used while choosing the crop).
        /// </summary>
        public static (int Width, int Height, byte[] Pixels) ApplyGeometry(byte[] src, int w, int h, RawSettings s, bool crop)
        {
            double L = crop ? s.CropL : 0, T = crop ? s.CropT : 0, R = crop ? s.CropR : 1, B = crop ? s.CropB : 1;
            int ow = Math.Max(1, (int)Math.Round((R - L) * w)), oh = Math.Max(1, (int)Math.Round((B - T) * h));
            double a = s.Angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            double cx = w / 2.0, cy = h / 2.0;
            int x0 = Math.Clamp((int)Math.Round(L * w), 0, w - 1), y0 = Math.Clamp((int)Math.Round(T * h), 0, h - 1);
            ow = Math.Min(ow, w - x0);   // rounding must never step outside the photo
            oh = Math.Min(oh, h - y0);
            var dst = new byte[ow * oh * 4];
            if (Math.Abs(s.Angle) < 0.01)
            {
                Parallel.For(0, oh, y => Buffer.BlockCopy(src, ((y0 + y) * w + x0) * 4, dst, y * ow * 4, ow * 4));
                return (ow, oh, dst);
            }
            Parallel.For(0, oh, y =>
            {
                double py = y0 + y + 0.5 - cy;
                for (int x = 0; x < ow; x++)
                {
                    double px = x0 + x + 0.5 - cx;
                    // Inverse rotation: where this output pixel comes from in the original.
                    double qx = cos * px + sin * py + cx - 0.5, qy = -sin * px + cos * py + cy - 0.5;
                    int o = (y * ow + x) * 4;
                    if (!crop && (qx < -0.5 || qy < -0.5 || qx > w - 0.5 || qy > h - 0.5))
                    {
                        dst[o] = dst[o + 1] = dst[o + 2] = 38; dst[o + 3] = 255;
                        continue;
                    }
                    qx = Math.Clamp(qx, 0, w - 1.001);
                    qy = Math.Clamp(qy, 0, h - 1.001);
                    int ix = (int)qx, iy = (int)qy;
                    float fx = (float)(qx - ix), fy = (float)(qy - iy);
                    int i00 = (iy * w + ix) * 4, i01 = i00 + 4, i10 = i00 + w * 4, i11 = i10 + 4;
                    if (ix + 1 >= w) { i01 = i00; i11 = i10; }
                    if (iy + 1 >= h) { i10 = i00; i11 = i01; }
                    for (int c = 0; c < 4; c++)
                    {
                        float v = (src[i00 + c] * (1 - fx) + src[i01 + c] * fx) * (1 - fy) + (src[i10 + c] * (1 - fx) + src[i11 + c] * fx) * fy;
                        dst[o + c] = (byte)(v + 0.5f);
                    }
                }
            });
            return (ow, oh, dst);
        }

        /// <summary>Maps pixels of the original to the straightened photo.</summary>
        sealed class Frame
        {
            public readonly int W, H;
            readonly double _cos, _sin, _cx, _cy;
            readonly double _left, _top, _right, _bottom;
            readonly bool _rotated;

            public Frame(int w, int h, RawSettings s)
            {
                W = w; H = h;
                double a = s.Angle * Math.PI / 180;
                _cos = Math.Cos(a); _sin = Math.Sin(a);
                _cx = w / 2.0; _cy = h / 2.0;
                _rotated = Math.Abs(s.Angle) >= 0.01;
                _left = s.CropL * w; _top = s.CropT * h; _right = s.CropR * w; _bottom = s.CropB * h;
            }

            /// <summary>True when the original pixel (x, y) is part of the finished (straightened and cropped) photo.</summary>
            public bool InCrop(int x, int y)
            {
                ToStraight(x, y, out double sx, out double sy);
                return sx >= _left && sx <= _right && sy >= _top && sy <= _bottom;
            }

            /// <summary>Straightened pixel coordinates of the centre of original pixel (x, y).</summary>
            public void ToStraight(int x, int y, out double sx, out double sy)
            {
                if (!_rotated) { sx = x + 0.5; sy = y + 0.5; return; }
                double qx = x + 0.5 - _cx, qy = y + 0.5 - _cy;
                sx = _cos * qx - _sin * qy + _cx;
                sy = _sin * qx + _cos * qy + _cy;
            }
        }

        // ================= Blur helpers =================

        static float[] BoxBlur2(float[] src, int w, int h, int r) => BoxMean(BoxMean(src, w, h, r), w, h, r);

        // ================= Noise reduction =================

        /// <summary>Edge-preserving (bilateral) smoothing of the perceptual luminance.</summary>
        static void DenoiseLuminance(float[] xt, int w, int h, double amount, double scale)
        {
            int r = scale >= 0.6 ? 2 : 1;
            float sr = 0.004f + (float)(amount / 100) * 0.05f;
            double ss = r * 0.7 + 0.3;
            int side = 2 * r + 1;
            var gs = new float[side * side];
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    gs[(dy + r) * side + dx + r] = (float)Math.Exp(-(dx * dx + dy * dy) / (2 * ss * ss));
            const int bins = 256;
            float range = 3 * sr, toBin = (bins - 1) / range;
            var wr = new float[bins];
            for (int i = 0; i < bins; i++) { double v = i / toBin; wr[i] = (float)Math.Exp(-v * v / (2 * sr * sr)); }
            var src = (float[])xt.Clone();
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    float c = src[p], sum = 0, sw = 0;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int yy = Math.Clamp(y + dy, 0, h - 1) * w;
                        for (int dx = -r; dx <= r; dx++)
                        {
                            float v = src[yy + Math.Clamp(x + dx, 0, w - 1)];
                            float diff = Math.Abs(v - c);
                            if (diff >= range) continue;
                            float wt = gs[(dy + r) * side + dx + r] * wr[(int)(diff * toBin)];
                            sum += v * wt; sw += wt;
                        }
                    }
                    xt[p] = sw > 0 ? sum / sw : c;
                }
            });
        }

        /// <summary>Blurs the colour (chroma) only: removes coloured blotches without softening detail.</summary>
        static void ReduceColorNoise(byte[] px, int w, int h, double amount, double scale)
        {
            int n = w * h;
            double sigma = Math.Max(0.6, amount / 100 * 6 * scale);
            var ch = new byte[n * 2];
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    int i = p * 4;
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    ch[p * 2] = ImageOps.ClampByte(128 - 0.168736 * R - 0.331264 * G + 0.5 * B);
                    ch[p * 2 + 1] = ImageOps.ClampByte(128 + 0.5 * R - 0.418688 * G - 0.081312 * B);
                }
            });
            ImageOps.GaussianBlur(ch, w, h, 2, sigma);
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    int i = p * 4;
                    double Y = 0.299 * px[i + 2] + 0.587 * px[i + 1] + 0.114 * px[i];
                    double cb = ch[p * 2] - 128, cr = ch[p * 2 + 1] - 128;
                    px[i + 2] = ImageOps.ClampByte(Y + 1.402 * cr);
                    px[i + 1] = ImageOps.ClampByte(Y - 0.344136 * cb - 0.714136 * cr);
                    px[i] = ImageOps.ClampByte(Y + 1.772 * cb);
                }
            });
        }

        // ================= Texture =================

        static void ApplyTexture(float[] xt, int w, int h, double amount, double scale)
        {
            int r = Math.Max(1, (int)Math.Round(3 * scale));
            var blur = BoxBlur2(xt, w, h, r);
            float a = (float)(amount / 100 * (amount > 0 ? 1.2 : 0.9));
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    float x = xt[p];
                    float v = x + a * (x - blur[p]) * (4 * x * (1 - x) + 0.25f);
                    xt[p] = v < 0 ? 0 : v > 1 ? 1 : v;
                }
            });
        }

        // ================= Masks (local adjustments) =================

        sealed class MaskSet
        {
            sealed class Eval
            {
                public LocalMask M;
                public float Amount;
                public MaskKind Kind;
                public bool Invert;
                public float Lo, Hi, Fe;
                public double Ax, Ay, Dx, Dy, InvLen2;
                public double Cx, Cy, IRx, IRy, Inner;

                public Eval(LocalMask m, int w, int h)
                {
                    M = m;
                    Kind = m.Kind;
                    Invert = m.Invert;
                    Amount = (float)Math.Clamp(m.Amount / 100, 0, 2);
                    Lo = (float)m.Low; Hi = (float)m.High; Fe = (float)Math.Max(0.01, m.Feather);
                    Ax = m.X1 * w; Ay = m.Y1 * h;
                    Dx = (m.X2 - m.X1) * w; Dy = (m.Y2 - m.Y1) * h;
                    InvLen2 = 1 / Math.Max(1, Dx * Dx + Dy * Dy);
                    Cx = m.X1 * w; Cy = m.Y1 * h;
                    IRx = 1 / Math.Max(1, m.RX * w); IRy = 1 / Math.Max(1, m.RY * h);
                    Inner = 1 - Math.Clamp(m.Feather, 0, 1);
                }

                /// <param name="sky">How much the pixel belongs to the sky (0 when no mask needs it).</param>
                public float Weight(float lum, float sky, double sx, double sy)
                {
                    float wgt;
                    switch (Kind)
                    {
                        case MaskKind.Luminance:
                            float wl = Lo <= 0 ? 1 : SmoothStepF(Lo - Fe, Lo, lum);
                            float wh = Hi >= 1 ? 1 : 1 - SmoothStepF(Hi, Hi + Fe, lum);
                            wgt = wl * wh;
                            break;
                        case MaskKind.Sky:
                            wgt = sky;
                            break;
                        case MaskKind.Linear:
                            double t = ((sx - Ax) * Dx + (sy - Ay) * Dy) * InvLen2;
                            wgt = 1 - SmoothStepF(0, 1, (float)t);
                            break;
                        default:
                            double ex = (sx - Cx) * IRx, ey = (sy - Cy) * IRy;
                            float dist = (float)Math.Sqrt(ex * ex + ey * ey);
                            wgt = 1 - SmoothStepF((float)Inner, 1, dist);
                            break;
                    }
                    if (Invert) wgt = 1 - wgt;
                    return wgt * Amount;
                }
            }

            readonly Frame _f;
            readonly float[] _lum, _blur;
            readonly Eval[] _tone, _color;
            readonly Eval _preview;
            readonly SkyMap _sky;       // null when no mask is a sky mask, or the photo has no sky
            readonly ushort[] _data;
            public int PreviewIndex { get; }
            public bool HasColor => _color.Length > 0;

            MaskSet(Frame f, float[] lum, float[] blur, Eval[] tone, Eval[] color, Eval preview, int previewIndex, SkyMap sky, ushort[] data)
            {
                _f = f; _lum = lum; _blur = blur; _tone = tone; _color = color; _preview = preview; PreviewIndex = previewIndex;
                _sky = sky; _data = data;
            }

            float Sky(int x, int y) => _sky != null ? _sky.Weight(_data, x, y, _f.W, _f.H) : 0;

            public static MaskSet Create(RawImage img, RawSettings s, float[] xt, Frame f, int preview)
            {
                int w = f.W, h = f.H;
                var all = s.Masks ?? new List<LocalMask>();
                var active = all.Where(m => m != null && m.Enabled && m.Amount > 0.5).Select(m => new Eval(m, w, h)).ToArray();
                Eval pv = preview >= 0 && preview < all.Count && all[preview] != null ? new Eval(all[preview], w, h) { Amount = 1 } : null;
                if (active.Length == 0 && pv == null) return null;
                var sky = active.Any(e => e.Kind == MaskKind.Sky) || pv?.Kind == MaskKind.Sky ? SceneAnalysis.Of(img).Sky : null;

                float[] lum = null;
                if (active.Any(e => e.Kind == MaskKind.Luminance) || pv?.Kind == MaskKind.Luminance)
                {
                    // Selection on a smoothed, edge-aware luminance: clean masks that follow the shapes.
                    var baseLayer = LocalBase(xt, w, h);
                    lum = new float[xt.Length];
                    Parallel.For(0, h, y =>
                    {
                        for (int p = y * w, end = p + w; p < end; p++)
                            lum[p] = Math.Clamp(0.4f * xt[p] + 0.6f * baseLayer[p], 0f, 1f);
                    });
                }
                var tone = active.Where(e => e.M.HasTone).ToArray();
                float[] blur = tone.Any(e => Math.Abs(e.M.Clarity) >= 0.5)
                    ? BoxBlur2(xt, w, h, Math.Max(2, Math.Min(w, h) / 60))
                    : null;
                var color = active.Where(e => e.M.HasColor).ToArray();
                return new MaskSet(f, lum, blur, tone, color, pv, pv != null ? preview : -1, sky, img.Data);
            }

            public void ApplyTone(float[] xt)
            {
                if (_tone.Length == 0) return;
                int w = _f.W, h = _f.H;
                var p2 = _tone.Select(e => (
                    E: e,
                    Exp: (float)Math.Pow(2, e.M.Exposure / 2.2),
                    C: (float)(e.M.Contrast / 100 * 0.8),
                    Hl: (float)(e.M.Highlights / 100 * 0.3),
                    Sh: (float)(e.M.Shadows / 100 * 0.3),
                    Cl: (float)(e.M.Clarity / 100 * 0.9))).ToArray();
                Parallel.For(0, h, y =>
                {
                    for (int x = 0, p = y * w; x < w; x++, p++)
                    {
                        _f.ToStraight(x, y, out double sx, out double sy);
                        float lum = _lum != null ? _lum[p] : 0, sky = Sky(x, y);
                        float orig = xt[p];
                        float cur = orig;
                        foreach (var t in p2)
                        {
                            float wgt = t.E.Weight(lum, sky, sx, sy);
                            if (wgt < 0.002f) continue;
                            float v = cur * t.Exp;
                            if (t.C != 0)
                            {
                                float c = v < 0 ? 0 : v > 1 ? 1 : v;
                                v += t.C * (c * c * (3 - 2 * c) - c);
                            }
                            if (t.Hl != 0) v += t.Hl * SmoothStepF(0.45f, 1f, v);
                            if (t.Sh != 0) v += t.Sh * (1 - SmoothStepF(0f, 0.5f, v)) * SmoothStepF(0f, 0.06f, v);
                            if (t.Cl != 0 && _blur != null) v += t.Cl * (orig - _blur[p]) * (4 * orig * (1 - orig) + 0.15f);
                            cur += wgt * (v - cur);
                        }
                        xt[p] = cur < 0 ? 0 : cur > 1 ? 1 : cur;
                    }
                });
            }

            public void Color(int p, int x, int y, out float kT, out float kn, out float sat)
            {
                kT = kn = sat = 0;
                _f.ToStraight(x, y, out double sx, out double sy);
                float lum = _lum != null ? _lum[p] : 0, sky = Sky(x, y);
                foreach (var e in _color)
                {
                    float wgt = e.Weight(lum, sky, sx, sy);
                    if (wgt < 0.002f) continue;
                    kT += wgt * (float)(e.M.Temperature / 100);
                    kn += wgt * (float)(e.M.Tint / 100);
                    sat += wgt * (float)(e.M.Saturation / 100);
                }
            }

            public float PreviewWeight(int p, int x, int y)
            {
                if (_preview == null) return 0;
                _f.ToStraight(x, y, out double sx, out double sy);
                return Math.Clamp(_preview.Weight(_lum != null ? _lum[p] : 0, Sky(x, y), sx, sy), 0f, 1f);
            }
        }

        // ================= Dehaze =================

        /// <summary>Simplified dark-channel dehaze: estimates the haze veil at low resolution and removes (or adds) it.</summary>
        sealed class HazeMap
        {
            int _f, _lw, _lh;
            float[] _t;
            float _a, _k;
            bool _add;

            public static HazeMap Build(ushort[] d, float[] xt, float fr, float fg, float fb, int w, int h, double amount)
            {
                var m = new HazeMap();
                if (amount < 0)
                {
                    m._add = true;
                    m._k = (float)(-amount / 100 * 0.55);
                    return m;
                }
                int f = Math.Max(1, (int)Math.Ceiling(Math.Max(w, h) / 160.0));
                int lw = (w + f - 1) / f, lh = (h + f - 1) / f;
                var dark = new float[lw * lh];
                int step = Math.Max(1, f / 3);
                Parallel.For(0, lh, cy =>
                {
                    for (int cx = 0; cx < lw; cx++)
                    {
                        float mn = 1;
                        for (int y = cy * f; y < Math.Min(h, cy * f + f); y += step)
                            for (int x = cx * f; x < Math.Min(w, cx * f + f); x += step)
                            {
                                int p = y * w + x;
                                float r = d[p * 3] * fr, g = d[p * 3 + 1] * fg, b = d[p * 3 + 2] * fb;
                                float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                                float Yt = Pow22Lut[(int)(Math.Clamp(xt[p], 0f, 1f) * GammaSize + 0.5f)];
                                float k = Y > 1e-7f ? Yt / Y : 0;
                                float v = Encode(Math.Min(r, Math.Min(g, b)) * k);
                                if (v < mn) mn = v;
                            }
                        dark[cy * lw + cx] = mn;
                    }
                });
                // Minimum over the neighbours (the dark channel), then smooth.
                var eroded = new float[dark.Length];
                for (int y = 0; y < lh; y++)
                    for (int x = 0; x < lw; x++)
                    {
                        float mn = 1;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                mn = Math.Min(mn, dark[Math.Clamp(y + dy, 0, lh - 1) * lw + Math.Clamp(x + dx, 0, lw - 1)]);
                        eroded[y * lw + x] = mn;
                    }
                var smooth = BoxBlur2(eroded, lw, lh, 2);
                var sorted = (float[])smooth.Clone();
                Array.Sort(sorted);
                float a = Math.Clamp(sorted[(int)(sorted.Length * 0.995)] + 0.08f, 0.55f, 1f);
                float omega = (float)(0.95 * Math.Min(1, amount / 100));
                var t = new float[smooth.Length];
                for (int i = 0; i < t.Length; i++) t[i] = Math.Clamp(1 - omega * smooth[i] / a, 0.2f, 1f);
                m._f = f; m._lw = lw; m._lh = lh; m._t = t; m._a = a;
                return m;
            }

            public void Apply(int x, int y, ref float R, ref float G, ref float B)
            {
                if (_add)
                {
                    const float veil = 0.82f;
                    R += (veil - R) * _k; G += (veil - G) * _k; B += (veil - B) * _k;
                    return;
                }
                float fx = Math.Clamp((x + 0.5f) / _f - 0.5f, 0, _lw - 1), fy = Math.Clamp((y + 0.5f) / _f - 0.5f, 0, _lh - 1);
                int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(_lw - 1, x0 + 1), y1 = Math.Min(_lh - 1, y0 + 1);
                float ax = fx - x0, ay = fy - y0;
                float tt = (_t[y0 * _lw + x0] * (1 - ax) + _t[y0 * _lw + x1] * ax) * (1 - ay) + (_t[y1 * _lw + x0] * (1 - ax) + _t[y1 * _lw + x1] * ax) * ay;
                R = Math.Clamp((R - _a) / tt + _a, 0f, 1f);
                G = Math.Clamp((G - _a) / tt + _a, 0f, 1f);
                B = Math.Clamp((B - _a) / tt + _a, 0f, 1f);
            }
        }

        // ================= Colour mixer and colour grading =================

        sealed class ColorStage
        {
            static readonly float[] Centers = { 0, 30, 60, 120, 180, 240, 270, 300, 360 };
            float[] _hue, _sat, _lum;
            bool _mixer, _grading;
            float[] _tS, _tM, _tH;
            float _lS, _lM, _lH, _k, _bal;

            public static ColorStage Create(RawSettings s)
            {
                bool mixer = s.HasMixer, grading = s.HasGrading;
                if (!mixer && !grading) return null;
                var c = new ColorStage { _mixer = mixer, _grading = grading };
                if (mixer)
                {
                    var src = s.Clone();   // fixes arrays of the wrong length
                    c._hue = src.MixHue.Select(v => (float)(v / 100)).ToArray();
                    c._sat = src.MixSat.Select(v => (float)(v / 100)).ToArray();
                    c._lum = src.MixLum.Select(v => (float)(v / 100)).ToArray();
                }
                if (grading)
                {
                    c._tS = Tint(s.ShadowHue, s.ShadowSat);
                    c._tM = Tint(s.MidHue, s.MidSat);
                    c._tH = Tint(s.HighHue, s.HighSat);
                    c._lS = (float)(s.ShadowLum / 100 * 0.25);
                    c._lM = (float)(s.MidLum / 100 * 0.25);
                    c._lH = (float)(s.HighLum / 100 * 0.25);
                    c._k = (float)(2 + (100 - Math.Clamp(s.GradeBlending, 0, 100)) / 100 * 4);
                    c._bal = (float)Math.Pow(2, -Math.Clamp(s.GradeBalance, -100, 100) / 100);
                }
                return c;
            }

            /// <summary>Zero-luminance colour offset for a wheel position (hue in degrees, saturation 0..100).</summary>
            static float[] Tint(double hue, double sat)
            {
                HueToRgb((float)hue, out float r, out float g, out float b);
                float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                float k = (float)(Math.Clamp(sat, 0, 100) / 100 * 0.35);
                return new[] { (r - y) * k, (g - y) * k, (b - y) * k };
            }

            static void HueToRgb(float h, out float r, out float g, out float b)
            {
                h = ((h % 360) + 360) % 360 / 60f;
                float x = 1 - Math.Abs(h % 2 - 1);
                (r, g, b) = (int)h switch
                {
                    0 => (1f, x, 0f),
                    1 => (x, 1f, 0f),
                    2 => (0f, 1f, x),
                    3 => (0f, x, 1f),
                    4 => (x, 0f, 1f),
                    _ => (1f, 0f, x),
                };
            }

            public void Apply(ref float R, ref float G, ref float B)
            {
                if (_mixer) Mix(ref R, ref G, ref B);
                if (_grading)
                {
                    float L = Math.Clamp(0.2126f * R + 0.7152f * G + 0.0722f * B, 0f, 1f);
                    float ls = (float)Math.Pow(L, _bal);
                    float ws = (float)Math.Pow(1 - ls, _k), wh = (float)Math.Pow(ls, _k), wm = Math.Max(0, 1 - ws - wh);
                    float lum = ws * _lS + wm * _lM + wh * _lH;
                    R += ws * _tS[0] + wm * _tM[0] + wh * _tH[0] + lum;
                    G += ws * _tS[1] + wm * _tM[1] + wh * _tH[1] + lum;
                    B += ws * _tS[2] + wm * _tM[2] + wh * _tH[2] + lum;
                }
            }

            void Mix(ref float R, ref float G, ref float B)
            {
                float mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B)), c = mx - mn;
                if (c < 0.004f) return;   // neutral greys are not touched
                float h;
                if (mx == R) h = 60 * (((G - B) / c) % 6);
                else if (mx == G) h = 60 * ((B - R) / c + 2);
                else h = 60 * ((R - G) / c + 4);
                if (h < 0) h += 360;
                int i = 7;
                for (int k = 0; k < 8; k++) if (h >= Centers[k] && h < Centers[k + 1]) { i = k; break; }
                int j = (i + 1) % 8;
                float t = (h - Centers[i]) / (Centers[i + 1] - Centers[i]);
                t = t * t * (3 - 2 * t);
                float wi = 1 - t, wj = t;
                float dh = (wi * _hue[i] + wj * _hue[j]) * 30;
                float ds = wi * _sat[i] + wj * _sat[j];
                float dl = wi * _lum[i] + wj * _lum[j];
                if (dh == 0 && ds == 0 && dl == 0) return;

                float l = (mx + mn) / 2;
                float sHsl = c / Math.Max(1e-4f, 1 - Math.Abs(2 * l - 1));
                float satW = Math.Min(1, sHsl * 2);
                h += dh;
                sHsl = Math.Clamp(sHsl * (1 + ds), 0, 1);
                if (dl < 0) l *= 1 + dl * 0.6f * satW;
                else l += (1 - l) * dl * 0.5f * satW;
                l = Math.Clamp(l, 0, 1);
                // HSL -> RGB
                float cc = (1 - Math.Abs(2 * l - 1)) * sHsl;
                HueToRgb(h, out float r1, out float g1, out float b1);
                float m = l - cc / 2;
                R = r1 * cc + m; G = g1 * cc + m; B = b1 * cc + m;
            }
        }

        // ================= Vignette and grain =================

        sealed class Effects
        {
            Frame _f;
            bool _vig;
            float _amount, _m0, _m1, _grain;
            double _ccx, _ccy, _chw, _chh, _cell, _scale;

            public static Effects Create(RawSettings s, Frame f, double scale)
            {
                bool vig = Math.Abs(s.VignetteAmount) >= 0.5, grain = s.GrainAmount >= 0.5;
                if (!vig && !grain) return null;
                double mid = 0.25 + Math.Clamp(s.VignetteMidpoint, 0, 100) / 100 * 0.65;
                double fe = 0.05 + Math.Clamp(s.VignetteFeather, 0, 100) / 100 * 0.9;
                return new Effects
                {
                    _f = f, _vig = vig, _amount = (float)(s.VignetteAmount / 100),
                    _m0 = (float)Math.Max(0, mid - fe / 2), _m1 = (float)(mid + fe / 2),
                    _ccx = (s.CropL + s.CropR) / 2, _ccy = (s.CropT + s.CropB) / 2,
                    _chw = Math.Max(0.01, (s.CropR - s.CropL) / 2), _chh = Math.Max(0.01, (s.CropB - s.CropT) / 2),
                    _grain = grain ? (float)(s.GrainAmount / 100) : 0,
                    _cell = 1 + Math.Clamp(s.GrainSize, 0, 100) / 100 * 3, _scale = scale,
                };
            }

            static float Hash(int x, int y)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / 16777216f;
                }
            }

            static float ValueNoise(double x, double y)
            {
                int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
                float fx = (float)(x - ix), fy = (float)(y - iy);
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
                return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
            }

            public void Apply(int x, int y, ref float R, ref float G, ref float B)
            {
                if (_vig)
                {
                    _f.ToStraight(x, y, out double sx, out double sy);
                    double u = (sx / _f.W - _ccx) / _chw, v = (sy / _f.H - _ccy) / _chh;
                    float dist = (float)(Math.Sqrt(u * u + v * v) / 1.41421356);
                    float wv = SmoothStepF(_m0, _m1, dist);
                    if (_amount < 0)
                    {
                        float k = 1 + _amount * 0.9f * wv;
                        R *= k; G *= k; B *= k;
                    }
                    else
                    {
                        float k = _amount * 0.9f * wv;
                        R += (1 - R) * k; G += (1 - G) * k; B += (1 - B) * k;
                    }
                }
                if (_grain > 0)
                {
                    double gx = x / _scale / _cell, gy = y / _scale / _cell;
                    float nz = ValueNoise(gx, gy) * 0.7f + ValueNoise(gx * 2.3 + 17, gy * 2.3 + 5) * 0.3f - 0.5f;
                    float L = Math.Clamp(0.299f * R + 0.587f * G + 0.114f * B, 0f, 1f);
                    float a = _grain * 0.22f * nz * (4 * L * (1 - L) + 0.3f);
                    R += a; G += a; B += a;
                }
            }
        }
    }
}
