using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sdcb.LibRaw;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    public enum PhotoState { Pending, Open, Saved }

    /// <summary>Color label, as in Lightroom (keys 6-9 in the Preselezione).</summary>
    public enum PhotoLabel { None, Red, Yellow, Green, Blue, Purple }

    /// <summary>A photo of a folder. Files with the same name (e.g. IMG_0001.CR2 + IMG_0001.JPG) are one photo.</summary>
    public sealed class PhotoItem : INotifyPropertyChanged
    {
        BitmapSource _thumbnail;
        PhotoState _state;
        int _rating, _burstIndex, _burstCount;
        PhotoLabel _label;
        PhotoMetadata _metadata;
        ShotQuality _quality;

        public PhotoItem(string name, IEnumerable<string> files)
        {
            Name = name;
            // RAW first: it is the file that gets edited.
            Files = files.OrderBy(f => RawImage.IsRawFile(f) ? 0 : 1).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string Name { get; }
        public IReadOnlyList<string> Files { get; }
        public bool IsRaw => RawImage.IsRawFile(EditPath);

        /// <summary>The file opened for editing (the RAW when there is one).</summary>
        public string EditPath => Files[0];

        /// <summary>The file used for previews: a camera JPEG decodes faster than the RAW's embedded preview.</summary>
        public string PreviewPath => Files.FirstOrDefault(ImageIO.IsJpeg) ?? EditPath;

        public string Badge => string.Join(" + ", Files.Select(f => Path.GetExtension(f).TrimStart('.').ToUpperInvariant()));

        public BitmapSource Thumbnail { get => _thumbnail; set { _thumbnail = value; Raise(); } }
        internal bool ThumbnailRequested { get; set; }

        // ---- culling marks
        /// <summary>0 = no stars, 1..5 stars.</summary>
        public int Rating { get => _rating; set { value = Math.Clamp(value, 0, 5); if (_rating == value) return; _rating = value; Raise(); Raise(nameof(RatingText)); } }
        public string RatingText => _rating == 0 ? "" : new string('★', _rating);
        public PhotoLabel Label { get => _label; set { if (_label == value) return; _label = value; Raise(); Raise(nameof(LabelBrush)); } }
        public Brush LabelBrush => LabelColors.Brush(_label);

        // ---- shooting data and bursts
        public PhotoMetadata Metadata { get => _metadata; set { _metadata = value; Raise(); } }
        internal bool MetadataRequested { get; set; }
        /// <summary>When the photo was taken (EXIF date, or the file date when missing).</summary>
        public DateTime? CaptureTime { get; set; }
        /// <summary>Photos taken one after the other within a second share a burst id (0 = not part of a burst).</summary>
        public int BurstId { get; private set; }
        public int BurstIndex => _burstIndex;
        public int BurstCount => _burstCount;
        public string BurstText => _burstCount > 1 ? $"▣ {_burstIndex}/{_burstCount}" : "";
        internal void SetBurst(int id, int index, int count)
        {
            BurstId = id;
            _burstIndex = index;
            _burstCount = count;
            Raise(nameof(BurstText));
        }

        // ---- automatic check (see ShotCheck)
        /// <summary>Sharpness and closed eyes; null until the background check reaches this photo.</summary>
        public ShotQuality Quality { get => _quality; internal set { _quality = value; RaiseQuality(); } }
        internal bool QualityRequested { get; set; }
        internal void RaiseQuality() { Raise(nameof(Quality)); Raise(nameof(QualityText)); }
        public string QualityText => _quality == null ? ""
            : _quality.ClosedEyes > 0 ? T("◡ occhi chiusi")
            : _quality.Blurry ? T("≋ poco nitida")
            : _quality.Best ? T("✓ la migliore")
            : "";

        /// <summary>Development settings to use for this photo (copied, from a preset, or from its last Camera Raw edit).</summary>
        public RawSettings Settings { get; set; }

        // ---- editing batch
        public Session Session { get; set; }
        public string OutputPath { get; set; }
        public PhotoState State { get => _state; set { _state = value; Raise(); Raise(nameof(StateText)); } }
        public string StateText => _state switch { PhotoState.Open => T("● aperta"), PhotoState.Saved => T("✓ salvata"), _ => "" };

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class LabelColors
    {
        static readonly Brush[] Brushes =
        {
            Frozen(Color.FromArgb(0, 0, 0, 0)),
            Frozen(Color.FromRgb(0xE5, 0x48, 0x4D)),
            Frozen(Color.FromRgb(0xE8, 0xC5, 0x47)),
            Frozen(Color.FromRgb(0x46, 0xA7, 0x58)),
            Frozen(Color.FromRgb(0x3E, 0x8E, 0xDE)),
            Frozen(Color.FromRgb(0x8E, 0x4E, 0xC6)),
        };

        public static readonly string[] Names = { T("Nessuno"), T("Rosso"), T("Giallo"), T("Verde"), T("Blu"), T("Viola") };

        public static Brush Brush(PhotoLabel l) => Brushes[(int)l];

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    /// <summary>Shooting data carried from the original file to the saved JPEG/TIFF.</summary>
    public sealed class PhotoMetadata
    {
        public DateTime? DateTaken { get; set; }
        public string Make { get; set; }
        public string Model { get; set; }
        public string Lens { get; set; }
        public ushort? Iso { get; set; }
        public double? FNumber { get; set; }
        public double? ExposureTime { get; set; }
        public double? FocalLength { get; set; }

        /// <summary>One line such as "Canon EOS R6 · RF50mm F1.8 · 50 mm · f/1.8 · 1/250 s · ISO 400 · 12/05/2026 14:32".</summary>
        public string Summary()
        {
            var parts = new List<string>();
            string camera = Model ?? Make;
            if (Model != null && Make != null && !Model.StartsWith(Make.Split(' ')[0], StringComparison.OrdinalIgnoreCase)) camera = Make + " " + Model;
            if (!string.IsNullOrWhiteSpace(camera)) parts.Add(camera);
            if (!string.IsNullOrWhiteSpace(Lens)) parts.Add(Lens);
            if (FocalLength is double fl && fl > 0) parts.Add(fl.ToString("0.#", CultureInfo.CurrentCulture) + " mm");
            if (FNumber is double f && f > 0) parts.Add("f/" + f.ToString("0.0#", CultureInfo.CurrentCulture));
            if (ExposureTime is double t && t > 0) parts.Add(t >= 0.3 ? t.ToString("0.#", CultureInfo.CurrentCulture) + " s" : "1/" + Math.Round(1 / t).ToString(CultureInfo.InvariantCulture) + " s");
            if (Iso is ushort iso && iso > 0) parts.Add("ISO " + iso);
            if (DateTaken is DateTime d) parts.Add(d.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture));
            return string.Join("   ·   ", parts);
        }
    }

    /// <summary>Histogram and clipped-pixel statistics of a preview, with the red/blue clipping overlay.</summary>
    public sealed class PhotoAnalysis
    {
        public int[] R, G, B;
        /// <summary>Fraction (0..1) of pixels with a channel at 254-255 / with every channel at 0-3.</summary>
        public double Highlights, Shadows;
        /// <summary>Transparent image with red on blown highlights and blue on blocked shadows.</summary>
        public BitmapSource Overlay;
    }

    public static class PhotoLibrary
    {
        static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp", ".heic", ".heif", ".jxr", ".wdp",
        };

        public static bool IsSupported(string path) =>
            RawImage.IsRawFile(path) || ImageExtensions.Contains(Path.GetExtension(path) ?? "");

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        sealed class ExplorerOrder : IComparer<string>
        {
            public int Compare(string a, string b) => StrCmpLogicalW(a, b);
        }

        /// <summary>The photos of a folder (not its subfolders), in the same order as Windows Explorer.</summary>
        /// <param name="extensions">Only files with these extensions (e.g. ".cr2"); null = every supported format.</param>
        public static List<PhotoItem> Scan(string folder, ICollection<string> extensions = null)
        {
            var files = SupportedFiles(folder);
            if (extensions != null) files = files.Where(f => extensions.Contains(Path.GetExtension(f)));
            return Group(files);
        }

        /// <summary>Groups files into photos (same name = one photo), in the same order as Windows Explorer.</summary>
        public static List<PhotoItem> Group(IEnumerable<string> files)
        {
            return files
                .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                .Select(g => new PhotoItem(g.Key, g))
                .OrderBy(p => p.Name, new ExplorerOrder())
                .ToList();
        }

        /// <summary>
        /// Marks the bursts: consecutive photos (in list order) taken at most one second apart.
        /// Photos without a capture time are never part of a burst.
        /// </summary>
        public static void MarkBursts(IList<PhotoItem> photos)
        {
            int id = 0, start = 0;
            for (int i = 1; i <= photos.Count; i++)
            {
                bool continues = i < photos.Count
                                 && photos[i].CaptureTime is DateTime a && photos[i - 1].CaptureTime is DateTime b
                                 && Math.Abs((a - b).TotalSeconds) <= 1.0;
                if (continues) continue;
                int count = i - start;
                if (count > 1) id++;
                for (int k = start; k < i; k++)
                    photos[k].SetBurst(count > 1 ? id : 0, k - start + 1, count > 1 ? count : 0);
                start = i;
            }
        }

        /// <summary>Histogram and clipping overlay, computed on a copy at most 1200 pixels wide.</summary>
        public static PhotoAnalysis Analyze(BitmapSource preview)
        {
            BitmapSource src = preview;
            int longest = Math.Max(src.PixelWidth, src.PixelHeight);
            if (longest > 1200)
            {
                double sc = 1200.0 / longest;
                src = new TransformedBitmap(src, new ScaleTransform(sc, sc));
            }
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            var r = new int[256]; var g = new int[256]; var b = new int[256];
            var overlay = new byte[px.Length];
            long high = 0, low = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                byte cb = px[i], cg = px[i + 1], cr = px[i + 2];
                b[cb]++; g[cg]++; r[cr]++;
                int max = Math.Max(cr, Math.Max(cg, cb));
                if (max >= 254)
                {
                    high++;
                    overlay[i + 2] = 255; overlay[i + 3] = 255;                      // red
                }
                else if (max <= 3)
                {
                    low++;
                    overlay[i] = 255; overlay[i + 1] = 90; overlay[i + 3] = 255;    // blue
                }
            }
            var ov = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, overlay, w * 4);
            ov.Freeze();
            double n = Math.Max(1, w * h);
            return new PhotoAnalysis { R = r, G = g, B = b, Highlights = high / n, Shadows = low / n, Overlay = ov };
        }

        /// <summary>How many photo files of each extension (lower case, e.g. ".jpg") the folder contains.</summary>
        public static SortedDictionary<string, int> CountExtensions(string folder) => CountExtensions(SupportedFiles(folder));

        public static SortedDictionary<string, int> CountExtensions(IEnumerable<string> files) =>
            new SortedDictionary<string, int>(files
                .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count()));

        static IEnumerable<string> SupportedFiles(string folder) =>
            new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && IsSupported(f.Name))
                .Select(f => f.FullName);

        // ================= Previews =================

        /// <summary>
        /// Decodes a frozen, correctly oriented preview whose longest side is at most maxSide pixels (0 = full size).
        /// RAW files use the JPEG preview embedded by the camera, which is much faster than developing the RAW.
        /// </summary>
        public static BitmapSource LoadPreview(string path, int maxSide)
        {
            if (RawImage.IsRawFile(path))
            {
                var embedded = LoadRawEmbedded(path, maxSide);
                if (embedded != null) return embedded;
                // No usable embedded preview: develop the RAW with the default settings (slower).
                var raw = RawImage.Load(path);
                if (maxSide > 0) raw = raw.Downscale(maxSide);
                var px = RawDevelop.Render(raw, RawSettings.Default(raw.SceneReferred));
                return ImageIO.ToBitmap(raw.Width, raw.Height, px);
            }
            return Decode(File.ReadAllBytes(path), maxSide, null);
        }

        static BitmapSource LoadRawEmbedded(string path, int maxSide)
        {
            try
            {
                using var ctx = RawContext.OpenFile(path);
                ctx.UnpackThumbnail();
                using var thumb = ctx.MakeDcrawMemoryThumbnail();
                // LibRaw "flip": 3 = 180°, 5 = 90° counter-clockwise, 6 = 90° clockwise (as EXIF orientations 3, 8, 6).
                int? flip = ReadFlip(ctx);
                int? orientation = flip switch { 3 => 3, 5 => 8, 6 => 6, null => null, _ => 1 };
                BitmapSource result;
                if (thumb.ImageType == ProcessedImageType.Jpeg)
                {
                    result = Decode(thumb.AsSpan<byte>().ToArray(), maxSide, orientation);
                }
                else
                {
                    if (thumb.Bits != 8 || thumb.Channels != 3) return null;
                    int w = thumb.Width, h = thumb.Height;
                    var rgb = thumb.AsSpan<byte>().Slice(0, w * h * 3).ToArray();
                    BitmapSource src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, rgb, w * 3);
                    if (maxSide > 0 && Math.Max(w, h) > maxSide)
                    {
                        double s = (double)maxSide / Math.Max(w, h);
                        src = new TransformedBitmap(src, new ScaleTransform(s, s));
                    }
                    result = Finish(Orient(src, orientation ?? 1));
                }
                // A tiny thumbnail is not good enough for a large preview.
                int longest = Math.Max(result.PixelWidth, result.PixelHeight);
                if (longest < 400 && (maxSide == 0 || maxSide > longest * 2)) return null;
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The RAW's orientation (LibRaw sizes.flip). The wrapper does not expose it, so it is read from the native
        /// libraw_data_t (image pointer, then libraw_image_sizes_t) after checking that the known fields match.
        /// </summary>
        static int? ReadFlip(RawContext ctx)
        {
            try
            {
                IntPtr p = ctx.UnsafeGetHandle();
                if (p == IntPtr.Zero || IntPtr.Size != 8) return null;
                const int sizes = 8;
                bool layoutOk =
                    (ushort)Marshal.ReadInt16(p, sizes + 0) == ctx.RawHeight && (ushort)Marshal.ReadInt16(p, sizes + 2) == ctx.RawWidth &&
                    (ushort)Marshal.ReadInt16(p, sizes + 12) == ctx.Height && (ushort)Marshal.ReadInt16(p, sizes + 14) == ctx.Width;
                if (!layoutOk) return null;
                int flip = Marshal.ReadInt32(p, sizes + 32);
                return flip is >= 0 and <= 7 ? flip : null;
            }
            catch
            {
                return null;
            }
        }

        static BitmapSource Decode(byte[] bytes, int maxSide, int? orientationOverride)
        {
            int w, h, orientation;
            using (var probe = new MemoryStream(bytes))
            {
                var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                w = frame.PixelWidth;
                h = frame.PixelHeight;
                orientation = orientationOverride ?? ReadOrientation(frame);
            }
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            if (maxSide > 0 && Math.Max(w, h) > maxSide)
            {
                if (w >= h) bi.DecodePixelWidth = maxSide; else bi.DecodePixelHeight = maxSide;
            }
            bi.EndInit();
            bi.Freeze();
            return Finish(Orient(bi, orientation));
        }

        static int ReadOrientation(BitmapFrame f)
        {
            try
            {
                if (f.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation")
                    && m.GetQuery("System.Photo.Orientation") is ushort u)
                    return u;
            }
            catch { }
            return 1;
        }

        /// <summary>Applies an EXIF orientation (same conventions as ImageIO.FromSource).</summary>
        static BitmapSource Orient(BitmapSource src, int orientation)
        {
            BitmapSource Rotate(BitmapSource s, double angle) => new TransformedBitmap(s, new RotateTransform(angle));
            BitmapSource Flip(BitmapSource s, bool horizontal) => new TransformedBitmap(s, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
            return orientation switch
            {
                2 => Flip(src, true),
                3 => Rotate(src, 180),
                4 => Flip(src, false),
                5 => Flip(Rotate(src, 90), true),
                6 => Rotate(src, 90),
                7 => Flip(Rotate(src, 90), false),
                8 => Rotate(src, 270),
                _ => src,
            };
        }

        static BitmapSource Finish(BitmapSource src)
        {
            // Materialise lazy transforms once, so rendering does not repeat them.
            BitmapSource result = src is BitmapImage ? src : new CachedBitmap(src, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (!result.IsFrozen) result.Freeze();
            return result;
        }

        // ================= Metadata =================

        public static PhotoMetadata ReadMetadata(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                if (RawImage.IsRawFile(path))
                {
                    using var ctx = RawContext.OpenFile(path);
                    var ip = ctx.ImageParams;
                    var op = ctx.ImageOtherParams;
                    var md = new PhotoMetadata
                    {
                        Make = string.IsNullOrWhiteSpace(ip.Make) ? null : ip.Make.Trim(),
                        Model = string.IsNullOrWhiteSpace(ip.Model) ? null : ip.Model.Trim(),
                    };
                    if (op.IsoSpeed > 0) md.Iso = (ushort)Math.Min(65535, Math.Round(op.IsoSpeed));
                    if (op.Aperture > 0) md.FNumber = op.Aperture;
                    if (op.Shutter > 0) md.ExposureTime = op.Shutter;
                    if (op.FocalLength > 0) md.FocalLength = op.FocalLength;
                    if (op.Timestamp > 0) md.DateTaken = DateTimeOffset.FromUnixTimeSeconds((long)op.Timestamp).LocalDateTime;
                    try
                    {
                        string lens = ctx.LensInfo.Lens;
                        if (!string.IsNullOrWhiteSpace(lens)) md.Lens = lens.Trim();
                    }
                    catch { }
                    return md;
                }
                using var fs = File.OpenRead(path);
                var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (frame.Metadata is not BitmapMetadata m) return null;
                var r = new PhotoMetadata();
                try { r.Make = m.CameraManufacturer; r.Model = m.CameraModel; } catch { }
                try { if (DateTime.TryParse(m.DateTaken, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt)) r.DateTaken = dt; } catch { }
                r.Iso = Query(m, "System.Photo.ISOSpeed") is ushort iso ? iso : null;
                r.FNumber = Query(m, "System.Photo.FNumber") as double?;
                r.ExposureTime = Query(m, "System.Photo.ExposureTime") as double?;
                r.FocalLength = Query(m, "System.Photo.FocalLength") as double?;
                r.Lens = Query(m, "System.Photo.LensModel") as string;
                return r;
            }
            catch
            {
                return null;
            }
        }

        static object Query(BitmapMetadata m, string q)
        {
            try { return m.ContainsQuery(q) ? m.GetQuery(q) : null; } catch { return null; }
        }

        /// <summary>EXIF block for a new JPEG or TIFF (orientation is not written: pixels are already upright).</summary>
        internal static BitmapMetadata ToBitmapMetadata(PhotoMetadata md, bool jpeg)
        {
            if (md == null) return null;
            string ifd = jpeg ? "/app1/ifd" : "/ifd";
            var m = new BitmapMetadata(jpeg ? "jpg" : "tiff");
            if (!string.IsNullOrEmpty(md.Make)) m.SetQuery(ifd + "/{ushort=271}", md.Make);
            if (!string.IsNullOrEmpty(md.Model)) m.SetQuery(ifd + "/{ushort=272}", md.Model);
            m.SetQuery(ifd + "/{ushort=305}", "PhotoStudio");
            if (md.DateTaken is DateTime dt)
            {
                string s = dt.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                m.SetQuery(ifd + "/{ushort=306}", s);
                m.SetQuery(ifd + "/exif/{ushort=36867}", s);
                m.SetQuery(ifd + "/exif/{ushort=36868}", s);
            }
            if (md.Iso is ushort iso) m.SetQuery(ifd + "/exif/{ushort=34855}", iso);
            if (md.FNumber is double f) m.SetQuery(ifd + "/exif/{ushort=33437}", Rational(f, 10));
            if (md.ExposureTime is double t) m.SetQuery(ifd + "/exif/{ushort=33434}", t < 1 ? Rational(t, Math.Round(1 / t)) : Rational(t, 10));   // 1/250 s -> 1/250
            if (md.FocalLength is double fl) m.SetQuery(ifd + "/exif/{ushort=37386}", Rational(fl, 10));
            return m;
        }

        // WIC stores an unsigned EXIF RATIONAL as a 64-bit value: numerator in the low 32 bits, denominator in the high 32 bits.
        static ulong Rational(double value, double denominator)
        {
            uint den = (uint)Math.Max(1, denominator);
            uint num = (uint)Math.Round(value * den);
            return ((ulong)den << 32) | num;
        }

        // ================= Recycle Bin =================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>
        /// True when Windows keeps what is deleted from this path in its Recycle Bin: only the internal disks. On a
        /// memory card, a USB stick or a network drive "deleting" is permanent, without a word.
        /// </summary>
        public static bool HasRecycleBin(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return false;
                return new DriveInfo(root).DriveType == DriveType.Fixed;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Moves files to the Windows Recycle Bin. Returns false if the user cancelled. Refuses (IOException) a drive
        /// without the Recycle Bin, where Windows would delete them for good: use <see cref="MoveToTrash"/> there.
        /// </summary>
        public static bool MoveToRecycleBin(IEnumerable<string> files, IntPtr owner)
        {
            var list = files.Where(File.Exists).Select(Path.GetFullPath).ToList();
            if (list.Count == 0) return true;
            if (!list.All(HasRecycleBin))
                throw new IOException(T("Su questa unità Windows non ha il Cestino: il file verrebbe eliminato definitivamente."));
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", list) + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING),
            };
            int rc = SHFileOperation(ref op);
            if (op.fAnyOperationsAborted) return false;
            if (rc != 0) throw new IOException(T("Windows non è riuscito a spostare il file nel Cestino (codice 0x{0:X}). Il file potrebbe essere aperto in un altro programma.", rc));
            var left = list.Where(File.Exists).ToList();
            if (left.Count > 0) throw new IOException(T("Il file è ancora presente: {0}", Path.GetFileName(left[0])));
            return true;
        }

        /// <summary>
        /// The hidden folder, next to the photos, that takes the deleted ones on a drive without the Windows Recycle
        /// Bin (memory card, USB stick, network): on the same drive, so moving there and back is instant.
        /// </summary>
        public const string TrashFolder = ".PhotoStudio - foto eliminate";

        /// <summary>
        /// Moves files into <see cref="TrashFolder"/> next to them. Returns where each one went; on an error the files
        /// already moved go back and the error is thrown.
        /// </summary>
        public static List<(string Original, string Trashed)> MoveToTrash(IEnumerable<string> files)
        {
            var moved = new List<(string, string)>();
            try
            {
                foreach (var f in files.Where(File.Exists).Select(Path.GetFullPath))
                {
                    string dir = Path.Combine(Path.GetDirectoryName(f), TrashFolder);
                    var info = Directory.CreateDirectory(dir);
                    info.Attributes |= FileAttributes.Hidden;
                    string dest = Path.Combine(dir, Path.GetFileName(f));
                    for (int n = 2; File.Exists(dest); n++)
                        dest = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(f)} ({n}){Path.GetExtension(f)}");
                    File.Move(f, dest);
                    moved.Add((f, dest));
                }
                return moved;
            }
            catch
            {
                RestoreFromTrash(moved);
                throw;
            }
        }

        /// <summary>Puts back files moved with <see cref="MoveToTrash"/>; returns the ones that could not go back.</summary>
        public static List<string> RestoreFromTrash(IEnumerable<(string Original, string Trashed)> moved)
        {
            var failed = new List<string>();
            foreach (var (original, trashed) in moved)
            {
                try
                {
                    if (File.Exists(original) || !File.Exists(trashed)) { failed.Add(original); continue; }
                    File.Move(trashed, original);
                }
                catch
                {
                    failed.Add(original);
                }
            }
            return failed;
        }

        /// <summary>Deletes for good files moved with <see cref="MoveToTrash"/>, and the trash folder once empty.</summary>
        public static void EmptyTrash(IEnumerable<(string Original, string Trashed)> moved)
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, trashed) in moved)
            {
                try { File.Delete(trashed); } catch { }
                dirs.Add(Path.GetDirectoryName(trashed));
            }
            foreach (var d in dirs)
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }

        /// <summary>
        /// Puts back a file this user recently moved to the Recycle Bin (the most recent copy with that original path).
        /// Reads the Recycle Bin's $I index files; returns false if the file is not there any more.
        /// </summary>
        public static bool RestoreFromRecycleBin(string originalPath)
        {
            originalPath = Path.GetFullPath(originalPath);
            if (File.Exists(originalPath)) return false;
            string sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid == null) return false;
            string bin = Path.Combine(Path.GetPathRoot(originalPath), "$Recycle.Bin", sid);
            if (!Directory.Exists(bin)) return false;

            string bestInfo = null;
            long bestTime = long.MinValue;
            foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
            {
                try
                {
                    var b = File.ReadAllBytes(info);
                    if (b.Length < 28) continue;
                    long version = BitConverter.ToInt64(b, 0), deleted = BitConverter.ToInt64(b, 16);
                    string path;
                    if (version == 2)
                    {
                        int chars = Math.Min(BitConverter.ToInt32(b, 24), (b.Length - 28) / 2);
                        path = Encoding.Unicode.GetString(b, 28, chars * 2).TrimEnd('\0');
                    }
                    else if (version == 1)
                    {
                        path = Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24)).Split('\0')[0];
                    }
                    else continue;
                    if (string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase) && deleted > bestTime)
                    {
                        bestTime = deleted;
                        bestInfo = info;
                    }
                }
                catch { }
            }
            if (bestInfo == null) return false;
            string data = Path.Combine(bin, "$R" + Path.GetFileName(bestInfo).Substring(2));
            if (!File.Exists(data)) return false;
            File.Move(data, originalPath);
            try { File.Delete(bestInfo); } catch { }
            return true;
        }
    }

    /// <summary>
    /// Checks every photo in the background (sharpness, closed eyes), nearest to the photo being viewed first, and
    /// marks the best shot of each burst. Create and use it on the UI thread: results are assigned there.
    /// </summary>
    public sealed class QualityLoader
    {
        readonly IList<PhotoItem> _items;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        bool _running;

        public QualityLoader(IList<PhotoItem> items) => _items = items;

        /// <summary>Index in the list given to the constructor of the photo being viewed.</summary>
        public int Focus { get; set; }
        /// <summary>Raised on the UI thread after each photo checked.</summary>
        public event Action<PhotoItem> Checked;

        public void Kick()
        {
            if (_running || _cts.IsCancellationRequested) return;
            _running = true;
            Work();
        }

        public void Stop() => _cts.Cancel();

        async void Work()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var next = Pick();
                    if (next == null) break;
                    next.QualityRequested = true;
                    ShotQuality q = null;
                    try { q = await Task.Run(() => ShotCheck.Assess(next.PreviewPath)); }
                    catch { }
                    if (q == null || _cts.IsCancellationRequested) continue;
                    next.Quality = q;
                    Rank(next.BurstId);
                    Checked?.Invoke(next);
                }
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>Ranks the shots again; burst 0 = all of them (after the bursts have been found or changed).</summary>
        public void Rank(int burst = 0)
        {
            var shots = _items.Where(p => p.Quality != null).Select(p => (p.Quality, p.BurstId)).ToList();
            ShotCheck.Rank(shots);
            foreach (var p in _items)
                if (p.Quality != null && (burst == 0 || p.BurstId == burst)) p.RaiseQuality();
        }

        PhotoItem Pick()
        {
            int n = _items.Count, f = Math.Clamp(Focus, 0, Math.Max(0, n - 1));
            for (int d = 0; d < n; d++)
            {
                if (f + d < n && !_items[f + d].QualityRequested) return _items[f + d];
                if (d > 0 && f - d >= 0 && !_items[f - d].QualityRequested) return _items[f - d];
            }
            return null;
        }
    }

    /// <summary>
    /// Loads thumbnails in the background, nearest to the photo being viewed first.
    /// Create and use it on the UI thread: results are assigned there.
    /// </summary>
    public sealed class ThumbnailLoader
    {
        public const int Size = 300;

        readonly IList<PhotoItem> _items;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly int _maxWorkers;
        int _workers;

        public ThumbnailLoader(IList<PhotoItem> items, int workers = 2)
        {
            _items = items;
            _maxWorkers = Math.Max(1, workers);
        }

        public int Focus { get; set; }

        /// <summary>Starts loading (again): call it after adding items.</summary>
        public void Kick()
        {
            // Work() can finish synchronously when there is nothing left to load: start a fixed number of workers,
            // never "until the pool is full", or this would spin forever.
            int missing = _maxWorkers - _workers;
            for (int i = 0; i < missing && !_cts.IsCancellationRequested; i++)
            {
                _workers++;
                Work();
            }
        }

        public void Stop() => _cts.Cancel();

        async void Work()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var next = Pick();
                    if (next == null) break;
                    next.ThumbnailRequested = true;
                    BitmapSource bmp = null;
                    try { bmp = await Task.Run(() => PhotoLibrary.LoadPreview(next.PreviewPath, Size)); }
                    catch { }
                    if (bmp != null) next.Thumbnail = bmp;
                }
            }
            finally
            {
                _workers--;
            }
        }

        PhotoItem Pick()
        {
            int n = _items.Count, f = Math.Clamp(Focus, 0, Math.Max(0, n - 1));
            for (int d = 0; d < n; d++)
            {
                if (f + d < n && !_items[f + d].ThumbnailRequested) return _items[f + d];
                if (d > 0 && f - d >= 0 && !_items[f - d].ThumbnailRequested) return _items[f - d];
            }
            return null;
        }
    }
}
