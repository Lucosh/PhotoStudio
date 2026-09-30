using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    public static class ImageIO
    {
        public static string OpenFilter =>
            T("Tutti i formati supportati") + "|*.psx;*.png;*.jpg;*.jpeg;*.jpe;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.ico;*.jxr;*.wdp;" + RawImage.FilterPattern + "|" +
            "Camera RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2...)|" + RawImage.FilterPattern + "|" +
            T("Progetto PhotoStudio (*.psx)") + "|*.psx|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg;*.jpe|" +
            "BMP (*.bmp)|*.bmp|TIFF (*.tif;*.tiff)|*.tif;*.tiff|GIF (*.gif)|*.gif|WebP / HEIC|*.webp;*.heic;*.heif|" + T("Tutti i file") + "|*.*";

        public static string SaveFilter =>
            T("Progetto PhotoStudio con livelli (*.psx)") + "|*.psx|PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|" +
            "BMP (*.bmp)|*.bmp|TIFF (*.tif)|*.tif|GIF (*.gif)|*.gif";

        public static (int w, int h, byte[] px) LoadBitmap(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = dec.Frames[0];
            return FromSource(frame, ReadOrientation(frame));
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

        public static (int w, int h, byte[] px) FromSource(BitmapSource src, int orientation = 1)
        {
            BitmapSource s = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = s.PixelWidth, h = s.PixelHeight;
            var px = new byte[w * h * 4];
            s.CopyPixels(px, w * 4, 0);
            switch (orientation)
            {
                case 2: px = ImageOps.FlipH(px, w, h); break;
                case 3: px = ImageOps.Rotate180(px, w, h); break;
                case 4: px = ImageOps.FlipV(px, w, h); break;
                case 5: px = ImageOps.FlipH(ImageOps.Rotate90(px, w, h, true), h, w); (w, h) = (h, w); break;
                case 6: px = ImageOps.Rotate90(px, w, h, true); (w, h) = (h, w); break;
                case 7: px = ImageOps.FlipV(ImageOps.Rotate90(px, w, h, true), h, w); (w, h) = (h, w); break;
                case 8: px = ImageOps.Rotate90(px, w, h, false); (w, h) = (h, w); break;
            }
            return (w, h, px);
        }

        public static BitmapSource ToBitmap(int w, int h, byte[] px)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            return bmp;
        }

        static BitmapSource Opaque(int w, int h, byte[] px)
        {
            var d = new byte[w * h * 3];
            for (int i = 0, j = 0; i < px.Length; i += 4, j += 3)
            {
                int a = px[i + 3];
                d[j] = (byte)((px[i] * a + 255 * (255 - a) + 127) / 255);
                d[j + 1] = (byte)((px[i + 1] * a + 255 * (255 - a) + 127) / 255);
                d[j + 2] = (byte)((px[i + 2] * a + 255 * (255 - a) + 127) / 255);
            }
            return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, d, w * 3);
        }

        public static bool IsJpeg(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".jpe";
        }

        /// <summary>Saves an image; for JPEG and TIFF the shooting data of the original photo (if given) is written as EXIF.</summary>
        public static void SaveBitmap(string path, int w, int h, byte[] px, int jpegQuality = 92, PhotoMetadata metadata = null)
        {
            if (metadata != null)
            {
                try
                {
                    SaveBitmapCore(path, w, h, px, jpegQuality, metadata);
                    return;
                }
                catch
                {
                    // Unusual metadata must never prevent saving the picture: retry without it.
                }
            }
            SaveBitmapCore(path, w, h, px, jpegQuality, null);
        }

        static void SaveBitmapCore(string path, int w, int h, byte[] px, int jpegQuality, PhotoMetadata metadata)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder enc;
            BitmapSource src;
            BitmapMetadata exif = null;
            switch (ext)
            {
                case ".jpg": case ".jpeg": case ".jpe":
                    enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
                    src = Opaque(w, h, px);
                    exif = PhotoLibrary.ToBitmapMetadata(metadata, true);
                    break;
                case ".bmp":
                    enc = new BmpBitmapEncoder();
                    src = Opaque(w, h, px);
                    break;
                case ".tif": case ".tiff":
                    enc = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                    src = ToBitmap(w, h, px);
                    exif = PhotoLibrary.ToBitmapMetadata(metadata, false);
                    break;
                case ".gif":
                    enc = new GifBitmapEncoder();
                    src = ToBitmap(w, h, px);
                    break;
                default:
                    enc = new PngBitmapEncoder();
                    src = ToBitmap(w, h, px);
                    break;
            }
            enc.Frames.Add(exif != null ? BitmapFrame.Create(src, null, exif, null) : BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            File.WriteAllBytes(path, ms.ToArray());
        }

        public static byte[] EncodeJpeg(int w, int h, byte[] px, int quality)
        {
            var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            enc.Frames.Add(BitmapFrame.Create(Opaque(w, h, px)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static byte[] EncodePng(int w, int h, byte[] px)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(ToBitmap(w, h, px)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        static byte[] DecodePng(Stream s)
        {
            var dec = new PngBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return FromSource(dec.Frames[0]).px;
        }

        // ---------- Layered project (.psx = zip with json + png per layer) ----------

        sealed class ProjectMeta
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public List<ProjectLayer> Layers { get; set; } = new List<ProjectLayer>();
        }

        sealed class ProjectLayer
        {
            public string Name { get; set; }
            public bool Visible { get; set; }
            public double Opacity { get; set; }
            public string Blend { get; set; }
            public string File { get; set; }
        }

        public static void SaveProject(string path, Document doc)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var meta = new ProjectMeta { Width = doc.Width, Height = doc.Height };
                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    var l = doc.Layers[i];
                    string name = $"layer{i}.png";
                    meta.Layers.Add(new ProjectLayer { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity, Blend = l.Blend.ToString(), File = name });
                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    using var es = entry.Open();
                    var png = EncodePng(doc.Width, doc.Height, l.Pixels);
                    es.Write(png, 0, png.Length);
                }
                var je = zip.CreateEntry("document.json");
                using (var js = je.Open())
                    JsonSerializer.Serialize(js, meta, new JsonSerializerOptions { WriteIndented = true });

                var prev = zip.CreateEntry("preview.png", CompressionLevel.NoCompression);
                using (var ps = prev.Open())
                {
                    var png = EncodePng(doc.Width, doc.Height, doc.Render());
                    ps.Write(png, 0, png.Length);
                }
            }
            File.WriteAllBytes(path, ms.ToArray());
        }

        public static Document LoadProject(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            ProjectMeta meta;
            using (var js = zip.GetEntry("document.json").Open())
                meta = JsonSerializer.Deserialize<ProjectMeta>(js);
            var doc = new Document(meta.Width, meta.Height);
            foreach (var pl in meta.Layers)
            {
                byte[] px;
                using (var es = zip.GetEntry(pl.File).Open())
                using (var buf = new MemoryStream())
                {
                    es.CopyTo(buf);
                    buf.Position = 0;
                    px = DecodePng(buf);
                }
                var l = new Layer(pl.Name, meta.Width, meta.Height, px)
                {
                    Visible = pl.Visible,
                    Opacity = pl.Opacity,
                    Blend = Enum.TryParse(pl.Blend, out BlendMode bm) ? bm : BlendMode.Normal,
                };
                doc.Layers.Add(l);
            }
            return doc;
        }

        // ---------- Clipboard ----------

        public static void CopyToClipboard(int w, int h, byte[] px)
        {
            var data = new DataObject();
            data.SetImage(ToBitmap(w, h, px));
            data.SetData("PNG", new MemoryStream(EncodePng(w, h, px)));
            Clipboard.SetDataObject(data, true);
        }

        public static (int w, int h, byte[] px)? GetClipboardImage()
        {
            try
            {
                if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream ms)
                {
                    ms.Position = 0;
                    var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    return FromSource(dec.Frames[0]);
                }
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var f in Clipboard.GetFileDropList())
                    {
                        try { return LoadBitmap(f); } catch { }
                    }
                }
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        var r = FromSource(img);
                        bool allTransparent = true;
                        for (int i = 3; i < r.px.Length; i += 4) if (r.px[i] != 0) { allTransparent = false; break; }
                        if (allTransparent) for (int i = 3; i < r.px.Length; i += 4) r.px[i] = 255;
                        return r;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
