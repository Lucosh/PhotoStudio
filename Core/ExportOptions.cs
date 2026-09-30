using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    public enum WatermarkPosition { BottomRight, BottomLeft, TopRight, TopLeft, Center }

    /// <summary>
    /// How the edited photos are written by the "cartella di modifica": size, file names and watermark.
    /// Saved in %APPDATA%\PhotoStudio\export.json.
    /// </summary>
    public sealed class ExportOptions
    {
        public bool Resize { get; set; }
        /// <summary>Longest side in pixels (photos are never enlarged).</summary>
        public int LongSide { get; set; } = 2048;

        public bool Rename { get; set; }
        public string Prefix { get; set; } = T("Foto");
        public int StartNumber { get; set; } = 1;
        public int Digits { get; set; } = 3;

        public bool Watermark { get; set; }
        public string WatermarkText { get; set; } = "© ";
        public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;
        /// <summary>Text height as a percentage of the photo's longest side.</summary>
        public double WatermarkSize { get; set; } = 3;
        /// <summary>0..100.</summary>
        public double WatermarkOpacity { get; set; } = 70;

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "export.json");

        public static ExportOptions Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<ExportOptions>(File.ReadAllText(FilePath), FolderState.Json) ?? new ExportOptions();
            }
            catch { }
            return new ExportOptions();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, FolderState.Json));
        }

        public ExportOptions Clone() => (ExportOptions)MemberwiseClone();

        /// <summary>File name without extension: the original name, or e.g. "Matrimonio_007".</summary>
        public string FileName(string originalName, int position)
        {
            if (!Rename) return originalName;
            string prefix = string.IsNullOrWhiteSpace(Prefix) ? T("Foto") : Prefix.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) prefix = prefix.Replace(c, '_');
            int n = StartNumber + position;
            return prefix + "_" + n.ToString(new string('0', Math.Clamp(Digits, 1, 6)), CultureInfo.InvariantCulture);
        }

        /// <summary>A short description for tooltips and the status bar.</summary>
        public string Describe()
        {
            string size = Resize ? T("lato lungo {0} px", LongSide) : T("dimensione originale");
            string name = Rename ? T("nomi {0}, {1}...", FileName("", 0), FileName("", 1)) : T("nomi originali");
            string wm = Watermark && !string.IsNullOrWhiteSpace(WatermarkText) ? T("filigrana \"{0}\"", WatermarkText.Trim()) : T("senza filigrana");
            return $"{size} · {name} · {wm}";
        }

        /// <summary>Resizes (never enlarges) as configured. Safe to call from any thread.</summary>
        public byte[] ApplySize(byte[] px, ref int w, ref int h)
        {
            if (!Resize || LongSide < 16 || Math.Max(w, h) <= LongSide) return px;
            double s = (double)LongSide / Math.Max(w, h);
            int nw = Math.Max(1, (int)Math.Round(w * s)), nh = Math.Max(1, (int)Math.Round(h * s));
            px = ImageOps.Resize(px, w, h, nw, nh);
            w = nw; h = nh;
            return px;
        }

        /// <summary>Draws the watermark into the pixels (BGRA). Uses WPF text rendering: call it on the UI thread.</summary>
        public void ApplyWatermark(byte[] px, int w, int h)
        {
            if (!Watermark || string.IsNullOrWhiteSpace(WatermarkText)) return;
            double size = Math.Max(8, Math.Max(w, h) * Math.Clamp(WatermarkSize, 0.5, 20) / 100);
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var text = new FormattedText(WatermarkText.Trim(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, Brushes.White, 1.0);
            double shadow = Math.Max(1, size * 0.06);
            int tw = (int)Math.Ceiling(text.WidthIncludingTrailingWhitespace + shadow + 2), th = (int)Math.Ceiling(text.Height + shadow + 2);
            if (tw <= 0 || th <= 0 || tw > w || th > h) return;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var shadowText = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size,
                    new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1.0);
                dc.DrawText(shadowText, new Point(1 + shadow, 1 + shadow));
                dc.DrawText(text, new Point(1, 1));
            }
            var rtb = new RenderTargetBitmap(tw, th, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var mark = new byte[tw * th * 4];
            rtb.CopyPixels(mark, tw * 4, 0);

            int margin = (int)Math.Round(size * 0.8);
            (int x0, int y0) = Position switch
            {
                WatermarkPosition.BottomLeft => (margin, h - th - margin),
                WatermarkPosition.TopRight => (w - tw - margin, margin),
                WatermarkPosition.TopLeft => (margin, margin),
                WatermarkPosition.Center => ((w - tw) / 2, (h - th) / 2),
                _ => (w - tw - margin, h - th - margin),
            };
            x0 = Math.Clamp(x0, 0, w - tw);
            y0 = Math.Clamp(y0, 0, h - th);
            double opacity = Math.Clamp(WatermarkOpacity, 0, 100) / 100;
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    int si = (y * tw + x) * 4;
                    double a = mark[si + 3] / 255.0 * opacity;
                    if (a <= 0) continue;
                    int di = ((y0 + y) * w + x0 + x) * 4;
                    // Premultiplied source over the photo.
                    for (int c = 0; c < 3; c++)
                        px[di + c] = ImageOps.ClampByte(mark[si + c] * opacity + px[di + c] * (1 - a));
                }
            }
        }
    }
}
