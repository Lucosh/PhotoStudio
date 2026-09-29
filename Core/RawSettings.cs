using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace PhotoStudio.Core
{
    [Flags]
    public enum SettingsGroups
    {
        None = 0, WhiteBalance = 1, Exposure = 2, Tone = 4, Presence = 8, Detail = 16,
        Curve = 32, Color = 64, Effects = 128, Geometry = 256, Masks = 512,
        All = WhiteBalance | Exposure | Tone | Presence | Detail | Curve | Color | Effects | Geometry | Masks,
    }

    public enum MaskKind { Luminance, Linear, Radial }

    /// <summary>
    /// A local adjustment, as the masks of Lightroom: a zone of light (luminance range), a linear gradient or a
    /// radial gradient. Positions are normalised (0..1) in the straightened photo, before the crop.
    /// </summary>
    public sealed class LocalMask
    {
        public string Name = "Maschera";
        public MaskKind Kind;
        public bool Enabled = true;
        /// <summary>Radial: adjust outside the ellipse instead of inside. Luminance: every tone except the range.</summary>
        public bool Invert;

        // Luminance range (perceptual 0..1): full effect between Low and High, fading out over Feather.
        public double Low, High = 1, Feather = 0.15;
        // Linear: full effect at (X1,Y1) fading to none at (X2,Y2). Radial: centre (X1,Y1), radii RX, RY (Feather 0..1).
        public double X1 = 0.5, Y1 = 0.5, X2 = 0.5, Y2 = 1, RX = 0.25, RY = 0.3;

        /// <summary>Strength of the whole mask, 0..100 %.</summary>
        public double Amount = 100;
        public double Exposure;                         // EV, -4..4
        public double Contrast, Highlights, Shadows;    // -100..100
        public double Temperature, Tint;                // -100..100
        public double Saturation, Clarity;              // -100..100

        public LocalMask Clone() => (LocalMask)MemberwiseClone();

        [JsonIgnore] public bool HasColor => Math.Abs(Temperature) >= 0.5 || Math.Abs(Tint) >= 0.5 || Math.Abs(Saturation) >= 0.5;
        [JsonIgnore] public bool HasTone => Math.Abs(Exposure) >= 0.005 || Math.Abs(Contrast) >= 0.5 || Math.Abs(Highlights) >= 0.5
                               || Math.Abs(Shadows) >= 0.5 || Math.Abs(Clarity) >= 0.5;
    }

    /// <summary>Camera Raw / Lightroom style development settings (slider units, 0 = neutral).</summary>
    public sealed class RawSettings
    {
        // ---- Base
        public double Temperature, Tint;              // -100..100
        public double Exposure;                       // EV, -5..5
        public double Contrast, Highlights, Shadows, Whites, Blacks;   // -100..100
        public double Texture, Clarity, Dehaze;       // -100..100
        public double Vibrance, Saturation;           // -100..100

        // ---- Tone curve: control points x0,y0,x1,y1... in 0..1 (null = straight line)
        public double[] CurveRgb, CurveR, CurveG, CurveB;

        // ---- Color mixer (red, orange, yellow, green, aqua, blue, purple, magenta), -100..100
        public double[] MixHue = new double[8], MixSat = new double[8], MixLum = new double[8];

        // ---- Color grading: hue 0..360, saturation 0..100, luminance -100..100
        public double ShadowHue, ShadowSat, ShadowLum;
        public double MidHue, MidSat, MidLum;
        public double HighHue, HighSat, HighLum;
        public double GradeBlending = 50, GradeBalance;   // 0..100, -100..100

        // ---- Detail
        public double Sharpening;                     // 0..150
        public double NoiseLuma, NoiseColor;          // 0..100

        // ---- Effects (vignette after the crop, as in Lightroom)
        public double VignetteAmount;                 // -100..100
        public double VignetteMidpoint = 50, VignetteFeather = 50;   // 0..100
        public double GrainAmount;                    // 0..100
        public double GrainSize = 25;                 // 0..100

        // ---- Geometry: straighten angle (degrees) and crop in the straightened photo (0..1)
        public double Angle;
        public double CropL, CropT, CropR = 1, CropB = 1;

        // ---- Local adjustments
        public List<LocalMask> Masks = new List<LocalMask>();

        public static readonly string[] MixNames = { "Rossi", "Arancioni", "Gialli", "Verdi", "Acqua", "Blu", "Viola", "Magenta" };

        public RawSettings Clone()
        {
            var c = (RawSettings)MemberwiseClone();
            c.CurveRgb = (double[])CurveRgb?.Clone();
            c.CurveR = (double[])CurveR?.Clone();
            c.CurveG = (double[])CurveG?.Clone();
            c.CurveB = (double[])CurveB?.Clone();
            c.MixHue = Fix8(MixHue);
            c.MixSat = Fix8(MixSat);
            c.MixLum = Fix8(MixLum);
            c.Masks = Masks?.Where(m => m != null).Select(m => m.Clone()).ToList() ?? new List<LocalMask>();
            return c;
        }

        static double[] Fix8(double[] a)
        {
            var r = new double[8];
            if (a != null) Array.Copy(a, r, Math.Min(8, a.Length));
            return r;
        }

        public static RawSettings Default(bool sceneReferred) => new RawSettings { Sharpening = sceneReferred ? 25 : 0 };

        // ---- quick checks used by the engine to skip work
        [JsonIgnore] public bool HasCurve => !CurveMath.IsIdentity(CurveRgb) || !CurveMath.IsIdentity(CurveR) || !CurveMath.IsIdentity(CurveG) || !CurveMath.IsIdentity(CurveB);
        [JsonIgnore] public bool HasMixer => (MixHue?.Any(v => Math.Abs(v) >= 0.5) ?? false) || (MixSat?.Any(v => Math.Abs(v) >= 0.5) ?? false) || (MixLum?.Any(v => Math.Abs(v) >= 0.5) ?? false);
        [JsonIgnore] public bool HasGrading => ShadowSat >= 0.5 || MidSat >= 0.5 || HighSat >= 0.5
                                  || Math.Abs(ShadowLum) >= 0.5 || Math.Abs(MidLum) >= 0.5 || Math.Abs(HighLum) >= 0.5;
        [JsonIgnore] public bool HasCrop => CropL > 0.0005 || CropT > 0.0005 || CropR < 0.9995 || CropB < 0.9995;
        [JsonIgnore] public bool HasGeometry => Math.Abs(Angle) >= 0.01 || HasCrop;

        /// <summary>A copy of these settings with the chosen groups taken from another set (as "Incolla impostazioni" in Lightroom).</summary>
        public RawSettings MergeFrom(RawSettings src, SettingsGroups groups)
        {
            var r = Clone();
            var s = src.Clone();
            if (groups.HasFlag(SettingsGroups.WhiteBalance)) { r.Temperature = s.Temperature; r.Tint = s.Tint; }
            if (groups.HasFlag(SettingsGroups.Exposure)) r.Exposure = s.Exposure;
            if (groups.HasFlag(SettingsGroups.Tone))
            {
                r.Contrast = s.Contrast; r.Highlights = s.Highlights; r.Shadows = s.Shadows;
                r.Whites = s.Whites; r.Blacks = s.Blacks;
            }
            if (groups.HasFlag(SettingsGroups.Presence))
            {
                r.Texture = s.Texture; r.Clarity = s.Clarity; r.Dehaze = s.Dehaze;
                r.Vibrance = s.Vibrance; r.Saturation = s.Saturation;
            }
            if (groups.HasFlag(SettingsGroups.Detail)) { r.Sharpening = s.Sharpening; r.NoiseLuma = s.NoiseLuma; r.NoiseColor = s.NoiseColor; }
            if (groups.HasFlag(SettingsGroups.Curve)) { r.CurveRgb = s.CurveRgb; r.CurveR = s.CurveR; r.CurveG = s.CurveG; r.CurveB = s.CurveB; }
            if (groups.HasFlag(SettingsGroups.Color))
            {
                r.MixHue = s.MixHue; r.MixSat = s.MixSat; r.MixLum = s.MixLum;
                r.ShadowHue = s.ShadowHue; r.ShadowSat = s.ShadowSat; r.ShadowLum = s.ShadowLum;
                r.MidHue = s.MidHue; r.MidSat = s.MidSat; r.MidLum = s.MidLum;
                r.HighHue = s.HighHue; r.HighSat = s.HighSat; r.HighLum = s.HighLum;
                r.GradeBlending = s.GradeBlending; r.GradeBalance = s.GradeBalance;
            }
            if (groups.HasFlag(SettingsGroups.Effects))
            {
                r.VignetteAmount = s.VignetteAmount; r.VignetteMidpoint = s.VignetteMidpoint; r.VignetteFeather = s.VignetteFeather;
                r.GrainAmount = s.GrainAmount; r.GrainSize = s.GrainSize;
            }
            if (groups.HasFlag(SettingsGroups.Geometry)) { r.Angle = s.Angle; r.CropL = s.CropL; r.CropT = s.CropT; r.CropR = s.CropR; r.CropB = s.CropB; }
            if (groups.HasFlag(SettingsGroups.Masks)) r.Masks = s.Masks;
            return r;
        }

        /// <summary>Human readable summary of the non-neutral values.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            void Add(string name, double v, string fmt = "+0;-0")
            {
                if (Math.Abs(v) >= 0.5) parts.Add(name + " " + v.ToString(fmt, CultureInfo.CurrentCulture));
            }
            Add("temperatura", Temperature);
            Add("tinta", Tint);
            if (Math.Abs(Exposure) >= 0.05) parts.Add("esposizione " + Exposure.ToString("+0.00;-0.00", CultureInfo.CurrentCulture) + " EV");
            Add("contrasto", Contrast);
            Add("luci", Highlights);
            Add("ombre", Shadows);
            Add("bianchi", Whites);
            Add("neri", Blacks);
            Add("texture", Texture);
            Add("chiarezza", Clarity);
            Add("foschia", Dehaze);
            Add("vividezza", Vibrance);
            Add("saturazione", Saturation);
            if (HasCurve) parts.Add("curva di tono");
            if (HasMixer) parts.Add("mix colori");
            if (HasGrading) parts.Add("color grading");
            if (NoiseLuma >= 0.5 || NoiseColor >= 0.5) parts.Add("riduzione rumore");
            Add("vignettatura", VignetteAmount);
            if (GrainAmount >= 0.5) parts.Add("grana");
            if (Math.Abs(Angle) >= 0.01) parts.Add("raddrizzata " + Angle.ToString("+0.0;-0.0", CultureInfo.CurrentCulture) + "°");
            if (HasCrop) parts.Add("ritagliata");
            int masks = Masks?.Count(m => m.Enabled) ?? 0;
            if (masks > 0) parts.Add(masks == 1 ? "1 maschera" : masks + " maschere");
            return parts.Count == 0 ? "l'immagine era già bilanciata" : string.Join(", ", parts);
        }
    }

    /// <summary>Tone curves: monotone cubic (Fritsch-Carlson) through control points in 0..1.</summary>
    public static class CurveMath
    {
        public const int LutSize = 1024;

        public static bool IsIdentity(double[] pts)
        {
            if (pts == null || pts.Length < 4) return true;
            for (int i = 0; i + 1 < pts.Length; i += 2)
                if (Math.Abs(pts[i] - pts[i + 1]) > 0.002) return false;
            return true;
        }

        /// <summary>LUT of LutSize+1 entries mapping 0..1 to 0..1 (null for a straight line).</summary>
        public static float[] BuildLut(double[] pts)
        {
            if (IsIdentity(pts)) return null;
            var p = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < pts.Length; i += 2) p.Add((Math.Clamp(pts[i], 0, 1), Math.Clamp(pts[i + 1], 0, 1)));
            p = p.OrderBy(q => q.X).ToList();
            int n = p.Count;
            var lut = new float[LutSize + 1];
            if (n == 1)
            {
                for (int i = 0; i <= LutSize; i++) lut[i] = (float)p[0].Y;
                return lut;
            }
            var dx = new double[n - 1];
            var m = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                dx[i] = p[i + 1].X - p[i].X;
                m[i] = dx[i] <= 1e-9 ? 0 : (p[i + 1].Y - p[i].Y) / dx[i];
            }
            var t = new double[n];
            t[0] = m[0];
            t[n - 1] = m[n - 2];
            for (int i = 1; i < n - 1; i++) t[i] = m[i - 1] * m[i] <= 0 ? 0 : (m[i - 1] + m[i]) / 2;
            for (int i = 0; i < n - 1; i++)
            {
                if (m[i] == 0) { t[i] = t[i + 1] = 0; continue; }
                double a = t[i] / m[i], b = t[i + 1] / m[i], s = a * a + b * b;
                if (s > 9)
                {
                    double tau = 3 / Math.Sqrt(s);
                    t[i] = tau * a * m[i];
                    t[i + 1] = tau * b * m[i];
                }
            }
            int seg = 0;
            for (int k = 0; k <= LutSize; k++)
            {
                double x = k / (double)LutSize, y;
                if (x <= p[0].X) y = p[0].Y;
                else if (x >= p[n - 1].X) y = p[n - 1].Y;
                else
                {
                    while (seg < n - 2 && x > p[seg + 1].X) seg++;
                    double hh = dx[seg], u = hh <= 1e-9 ? 0 : (x - p[seg].X) / hh;
                    double u2 = u * u, u3 = u2 * u;
                    y = (2 * u3 - 3 * u2 + 1) * p[seg].Y + (u3 - 2 * u2 + u) * hh * t[seg]
                      + (-2 * u3 + 3 * u2) * p[seg + 1].Y + (u3 - u2) * hh * t[seg + 1];
                }
                lut[k] = (float)Math.Clamp(y, 0, 1);
            }
            return lut;
        }

        public static float Apply(float[] lut, float v)
        {
            if (lut == null) return v;
            if (v <= 0) return lut[0];
            if (v >= 1) return lut[LutSize];
            float f = v * LutSize;
            int i = (int)f;
            return lut[i] + (lut[i + 1] - lut[i]) * (f - i);
        }
    }
}
