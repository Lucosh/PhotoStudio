using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    public enum BlendMode
    {
        Normal, Multiply, Screen, Overlay, SoftLight, HardLight,
        Darken, Lighten, ColorBurn, ColorDodge, LinearDodge, Difference, Exclusion
    }

    public sealed record BlendItem(BlendMode Mode, string Name)
    {
        public static readonly BlendItem[] All =
        {
            new(BlendMode.Normal, T("Normale")),
            new(BlendMode.Darken, T("Scurisci")),
            new(BlendMode.Multiply, T("Moltiplica")),
            new(BlendMode.ColorBurn, T("Brucia colore")),
            new(BlendMode.Lighten, T("Schiarisci")),
            new(BlendMode.Screen, T("Scolora")),
            new(BlendMode.ColorDodge, T("Colore scherma")),
            new(BlendMode.LinearDodge, T("Scherma lineare")),
            new(BlendMode.Overlay, T("Sovrapponi")),
            new(BlendMode.SoftLight, T("Luce soffusa")),
            new(BlendMode.HardLight, T("Luce intensa")),
            new(BlendMode.Difference, T("Differenza")),
            new(BlendMode.Exclusion, T("Esclusione")),
        };
    }

    /// <summary>A raster layer: non-premultiplied BGRA pixels, same size as the document.</summary>
    public sealed class Layer : INotifyPropertyChanged
    {
        string _name;
        bool _visible = true;
        double _opacity = 100;
        BlendMode _blend = BlendMode.Normal;
        BitmapSource _thumbnail;
        byte[] _pixels;

        // Immutable copy shared with the history. Never mutate it.
        internal byte[] SnapshotPixels;
        // True when Pixels changed since the last snapshot.
        internal bool Dirty = true;

        public Layer(string name, int width, int height, byte[] pixels = null)
        {
            _name = name;
            Width = width;
            Height = height;
            _pixels = pixels ?? new byte[width * height * 4];
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int Width { get; private set; }
        public int Height { get; private set; }

        public byte[] Pixels
        {
            get => _pixels;
            set { _pixels = value; Dirty = true; }
        }

        public string Name { get => _name; set { if (_name != value) { _name = value; Raise(); } } }
        public bool Visible { get => _visible; set { if (_visible != value) { _visible = value; Raise(); } } }
        public BlendMode Blend { get => _blend; set { if (_blend != value) { _blend = value; Raise(); } } }
        public BitmapSource Thumbnail { get => _thumbnail; private set { _thumbnail = value; Raise(); } }

        public double Opacity
        {
            get => _opacity;
            set
            {
                value = Math.Clamp(value, 0, 100);
                if (_opacity != value) { _opacity = value; Raise(); }
            }
        }

        public void MarkDirty() => Dirty = true;

        public void SetContent(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public Layer Clone(string name) =>
            new Layer(name, Width, Height, (byte[])_pixels.Clone()) { _visible = _visible, _opacity = _opacity, _blend = _blend };

        public void Fill(Color c)
        {
            var p = _pixels;
            for (int i = 0; i < p.Length; i += 4)
            {
                p[i] = c.B; p[i + 1] = c.G; p[i + 2] = c.R; p[i + 3] = c.A;
            }
            Dirty = true;
        }

        public void UpdateThumbnail()
        {
            const int max = 40;
            double s = Math.Min((double)max / Width, (double)max / Height);
            int tw = Math.Max(1, (int)(Width * s)), th = Math.Max(1, (int)(Height * s));
            var buf = new byte[tw * th * 4];
            for (int y = 0; y < th; y++)
            {
                int sy = Math.Min(Height - 1, (int)((y + 0.5) / s));
                for (int x = 0; x < tw; x++)
                {
                    int sx = Math.Min(Width - 1, (int)((x + 0.5) / s));
                    Buffer.BlockCopy(_pixels, (sy * Width + sx) * 4, buf, (y * tw + x) * 4, 4);
                }
            }
            var bmp = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Bgra32, null, buf, tw * 4);
            bmp.Freeze();
            Thumbnail = bmp;
        }
    }
}
