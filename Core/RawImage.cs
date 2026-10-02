using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sdcb.LibRaw;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Linear-light RGB image with 16 bits per channel (sRGB primaries).
    /// Produced by decoding camera RAW files, or by linearising an 8-bit layer.
    /// </summary>
    public sealed class RawImage
    {
        static readonly HashSet<string> RawExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".raf", ".orf", ".rw2", ".rwl",
            ".pef", ".ptx", ".dng", ".srw", ".x3f", ".3fr", ".fff", ".iiq", ".erf", ".mrw", ".mef", ".mos",
            ".kdc", ".dcr", ".raw",
        };

        public int Width { get; init; }
        public int Height { get; init; }
        /// <summary>Interleaved linear R, G, B (0..65535).</summary>
        public ushort[] Data { get; init; }
        /// <summary>Optional alpha (only for images coming from a layer).</summary>
        public byte[] Alpha { get; init; }
        /// <summary>True for real camera RAW data (values may need highlight roll-off and a base tone curve).</summary>
        public bool SceneReferred { get; init; }
        /// <summary>Size relative to the full-resolution image (1 = full size).</summary>
        public double Scale { get; init; } = 1;
        public string Camera { get; init; } = "";
        public string Info { get; init; } = "";
        /// <summary>ISO, shutter time (seconds) and aperture (f-number) of the shot; 0 when unknown.</summary>
        public double Iso { get; init; }
        public double Shutter { get; init; }
        public double Aperture { get; init; }

        /// <summary>
        /// How bright the scene was, as the exposure value at ISO 100 (about 15 in full sun, 12 under an overcast
        /// sky, 5-8 indoors). NaN when the shooting data are missing.
        /// </summary>
        public double SceneEv => Iso > 0 && Shutter > 0 && Aperture > 0
            ? Math.Log2(Aperture * Aperture / Shutter) - Math.Log2(Iso / 100)
            : double.NaN;

        /// <summary>What the automatic tools found in the photo (sky, faces): shared with its reduced copies.</summary>
        internal SceneCache Scene { get; init; } = new SceneCache();
        /// <summary>What the AI noise reduction and refocus made of the photo: shared with its reduced copies.</summary>
        internal RestoreCache Restore { get; init; } = new RestoreCache();

        /// <summary>The same photo with other pixel values (same size, shooting data and caches).</summary>
        internal RawImage WithData(ushort[] data) => new RawImage
        {
            Width = Width, Height = Height, Data = data, Alpha = Alpha, SceneReferred = SceneReferred,
            Scale = Scale, Camera = Camera, Info = Info, Iso = Iso, Shutter = Shutter, Aperture = Aperture,
            Scene = Scene, Restore = Restore,
        };

        public static bool IsRawFile(string path) => RawExtensions.Contains(Path.GetExtension(path) ?? "");

        public static string FilterPattern => string.Join(";", RawExtensions.Select(e => "*" + e));

        /// <summary>Decodes a camera RAW file (LibRaw, with the Windows RAW codec as fallback).</summary>
        /// <param name="halfSize">Half the width and height, several times faster: enough to analyse a photo.</param>
        public static RawImage Load(string path, bool halfSize = false)
        {
            try
            {
                return LoadWithLibRaw(path, halfSize);
            }
            catch (Exception libRawError)
            {
                try
                {
                    var (w, h, px) = ImageIO.LoadBitmap(path);
                    var img = FromBgra(px, w, h);
                    return new RawImage
                    {
                        Width = img.Width, Height = img.Height, Data = img.Data, SceneReferred = false,
                        Camera = T("Decodificato con il codec RAW di Windows"), Info = "",
                    };
                }
                catch
                {
                    throw new IOException(T("Impossibile decodificare il file RAW: {0}", libRawError.Message), libRawError);
                }
            }
        }

        static RawImage LoadWithLibRaw(string path, bool halfSize)
        {
            using var ctx = RawContext.OpenFile(path);
            ctx.Unpack();
            ctx.DcrawProcess(c =>
            {
                c.HalfSize = halfSize;     // one pixel per 2x2 sensor cell: no demosaicing
                c.OutputBps = 16;          // full precision
                c.NoAutoBright = true;     // we do our own exposure
                c.UseCameraWb = true;      // "as shot" white balance
                c.HighlightMode = 2;       // blend clipped channels instead of pink highlights
                c.Gamma[0] = 1;            // linear output
                c.Gamma[1] = 1;
            });
            using var processed = ctx.MakeDcrawMemoryImage();
            int w = processed.Width, h = processed.Height, ch = processed.Channels;
            var src = processed.AsSpan<ushort>();
            ushort[] data;
            if (ch == 3)
            {
                data = src.Slice(0, w * h * 3).ToArray();
            }
            else
            {
                data = new ushort[w * h * 3];
                for (int p = 0; p < w * h; p++)
                {
                    ushort v = src[p * ch];
                    data[p * 3] = data[p * 3 + 1] = data[p * 3 + 2] = v;
                }
            }

            var ip = ctx.ImageParams;
            var op = ctx.ImageOtherParams;
            var parts = new List<string>();
            if (op.IsoSpeed > 0) parts.Add("ISO " + op.IsoSpeed.ToString("0", CultureInfo.InvariantCulture));
            if (op.Shutter > 0)
                parts.Add(op.Shutter < 1
                    ? "1/" + Math.Round(1 / op.Shutter).ToString(CultureInfo.InvariantCulture) + " s"
                    : op.Shutter.ToString("0.#", CultureInfo.CurrentCulture) + " s");
            if (op.Aperture > 0) parts.Add("f/" + op.Aperture.ToString("0.#", CultureInfo.CurrentCulture));
            if (op.FocalLength > 0) parts.Add(op.FocalLength.ToString("0", CultureInfo.InvariantCulture) + " mm");

            return new RawImage
            {
                Width = w, Height = h, Data = data, SceneReferred = true, Scale = halfSize ? 0.5 : 1,
                Camera = $"{ip.Make} {ip.Model}".Trim(),
                Info = string.Join(" · ", parts),
                Iso = Math.Max(0, op.IsoSpeed), Shutter = Math.Max(0, op.Shutter), Aperture = Math.Max(0, op.Aperture),
            };
        }

        /// <summary>Converts an 8-bit sRGB BGRA buffer to linear 16-bit RGB.</summary>
        public static RawImage FromBgra(byte[] px, int w, int h)
        {
            var lut = new ushort[256];
            for (int i = 0; i < 256; i++) lut[i] = (ushort)Math.Round(Adjustments.SrgbToLinear(i / 255.0) * 65535);
            int n = w * h;
            var data = new ushort[n * 3];
            bool hasAlpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) { hasAlpha = true; break; }
            byte[] alpha = hasAlpha ? new byte[n] : null;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x, i = p * 4;
                    data[p * 3] = lut[px[i + 2]];
                    data[p * 3 + 1] = lut[px[i + 1]];
                    data[p * 3 + 2] = lut[px[i]];
                    if (alpha != null) alpha[p] = px[i + 3];
                }
            });
            return new RawImage { Width = w, Height = h, Data = data, Alpha = alpha, SceneReferred = false };
        }

        /// <summary>Box-averaged (in linear light) copy whose longest side is at most maxSide.</summary>
        public RawImage Downscale(int maxSide)
        {
            int f = (int)Math.Ceiling(Math.Max(Width, Height) / (double)maxSide);
            if (f <= 1) return this;
            int nw = Math.Max(1, Width / f), nh = Math.Max(1, Height / f);
            var d = new ushort[nw * nh * 3];
            var a = Alpha != null ? new byte[nw * nh] : null;
            int area = f * f;
            Parallel.For(0, nh, y =>
            {
                for (int x = 0; x < nw; x++)
                {
                    long r = 0, g = 0, b = 0, al = 0;
                    for (int yy = y * f; yy < y * f + f; yy++)
                    {
                        int row = yy * Width;
                        for (int xx = x * f; xx < x * f + f; xx++)
                        {
                            int p = row + xx;
                            r += Data[p * 3]; g += Data[p * 3 + 1]; b += Data[p * 3 + 2];
                            if (a != null) al += Alpha[p];
                        }
                    }
                    int o = y * nw + x;
                    d[o * 3] = (ushort)(r / area); d[o * 3 + 1] = (ushort)(g / area); d[o * 3 + 2] = (ushort)(b / area);
                    if (a != null) a[o] = (byte)(al / area);
                }
            });
            return new RawImage
            {
                Width = nw, Height = nh, Data = d, Alpha = a, SceneReferred = SceneReferred,
                Scale = Scale / f, Camera = Camera, Info = Info,
                Iso = Iso, Shutter = Shutter, Aperture = Aperture, Scene = Scene, Restore = Restore,
            };
        }

        /// <summary>
        /// The same image where only the selected pixels are opaque: the automatic tools measure the opaque
        /// pixels, so with this the selection alone decides the correction.
        /// </summary>
        /// <param name="selection">Coverage of each pixel, 0..255.</param>
        public RawImage Within(byte[] selection)
        {
            var a = new byte[Width * Height];
            for (int i = 0; i < a.Length; i++) a[i] = Alpha != null ? (byte)(Alpha[i] * selection[i] / 255) : selection[i];
            return new RawImage
            {
                Width = Width, Height = Height, Data = Data, Alpha = a, SceneReferred = SceneReferred,
                Scale = Scale, Camera = Camera, Info = Info, Iso = Iso, Shutter = Shutter, Aperture = Aperture,
            };
        }
    }
}
