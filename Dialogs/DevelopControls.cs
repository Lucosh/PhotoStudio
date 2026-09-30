using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>Point tone curve (0..1 both axes): click to add a point, drag it, right click to remove it.</summary>
    public sealed class CurveEditor : Border
    {
        const double S = 280;
        readonly Canvas _canvas;
        readonly Polyline _line = new Polyline { StrokeThickness = 1.8 };
        readonly Polygon _hist = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(60, 200, 200, 200)) };
        List<Point> _pts = Identity();
        int _drag = -1;

        public event Action Changed;
        public Color CurveColor { set => _line.Stroke = new SolidColorBrush(value); }

        public CurveEditor()
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            BorderThickness = new Thickness(1);
            HorizontalAlignment = HorizontalAlignment.Left;
            _canvas = new Canvas { Width = S, Height = S, Background = (Brush)Application.Current.FindResource("InputBg"), ClipToBounds = true, Cursor = Cursors.Cross };
            var grid = new SolidColorBrush(Color.FromRgb(62, 62, 62));
            _canvas.Children.Add(_hist);
            for (int i = 1; i < 4; i++)
            {
                _canvas.Children.Add(new Line { X1 = i * S / 4, X2 = i * S / 4, Y1 = 0, Y2 = S, Stroke = grid });
                _canvas.Children.Add(new Line { Y1 = i * S / 4, Y2 = i * S / 4, X1 = 0, X2 = S, Stroke = grid });
            }
            _canvas.Children.Add(new Line { X1 = 0, Y1 = S, X2 = S, Y2 = 0, Stroke = grid, StrokeDashArray = new DoubleCollection { 3, 3 } });
            _canvas.Children.Add(_line);
            _line.Stroke = Brushes.WhiteSmoke;
            _canvas.MouseLeftButtonDown += Down;
            _canvas.MouseMove += Move;
            _canvas.MouseLeftButtonUp += (s, e) => { _drag = -1; _canvas.ReleaseMouseCapture(); };
            _canvas.MouseRightButtonDown += Right;
            Child = _canvas;
            Redraw();
        }

        static List<Point> Identity() => new List<Point> { new Point(0, 0), new Point(1, 1) };

        /// <summary>Control points as x0,y0,x1,y1... (null = straight line).</summary>
        public double[] Points
        {
            get => CurveMath.IsIdentity(Flatten()) ? null : Flatten();
            set
            {
                _pts = Identity();
                if (value != null && value.Length >= 4)
                {
                    _pts = new List<Point>();
                    for (int i = 0; i + 1 < value.Length; i += 2) _pts.Add(new Point(value[i], value[i + 1]));
                    _pts.Sort((a, b) => a.X.CompareTo(b.X));
                }
                Redraw();
            }
        }

        double[] Flatten() => _pts.SelectMany(p => new[] { Math.Round(p.X, 4), Math.Round(p.Y, 4) }).ToArray();

        /// <summary>Histogram shown behind the curve (256 bins).</summary>
        public void SetHistogram(int[] h)
        {
            if (h == null) { _hist.Points = null; return; }
            int max = 1;
            for (int i = 1; i < 255; i++) max = Math.Max(max, h[i]);
            var pc = new PointCollection(260) { new Point(0, S) };
            for (int i = 0; i < 256; i++) pc.Add(new Point(i * S / 255, S - Math.Min(S, h[i] * S * 0.9 / max)));
            pc.Add(new Point(S, S));
            pc.Freeze();
            _hist.Points = pc;
        }

        static Point ToCurve(Point p) => new Point(Math.Clamp(p.X / S, 0, 1), Math.Clamp(1 - p.Y / S, 0, 1));

        void Down(object sender, MouseButtonEventArgs e)
        {
            var p = ToCurve(e.GetPosition(_canvas));
            _drag = _pts.FindIndex(q => Math.Abs(q.X - p.X) < 0.03 && Math.Abs(q.Y - p.Y) < 0.03);
            if (_drag < 0)
            {
                if (_pts.Any(q => Math.Abs(q.X - p.X) < 0.015)) return;
                _pts.Add(p);
                _pts.Sort((a, b) => a.X.CompareTo(b.X));
                _drag = _pts.IndexOf(p);
            }
            _canvas.CaptureMouse();
            Redraw();
            Changed?.Invoke();
        }

        void Move(object sender, MouseEventArgs e)
        {
            if (_drag < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            var p = ToCurve(e.GetPosition(_canvas));
            double minX = _drag > 0 ? _pts[_drag - 1].X + 0.01 : 0;
            double maxX = _drag < _pts.Count - 1 ? _pts[_drag + 1].X - 0.01 : 1;
            _pts[_drag] = new Point(Math.Clamp(p.X, minX, maxX), p.Y);
            Redraw();
            Changed?.Invoke();
        }

        void Right(object sender, MouseButtonEventArgs e)
        {
            var p = ToCurve(e.GetPosition(_canvas));
            int i = _pts.FindIndex(q => Math.Abs(q.X - p.X) < 0.03 && Math.Abs(q.Y - p.Y) < 0.03);
            if (i > 0 && i < _pts.Count - 1) { _pts.RemoveAt(i); Redraw(); Changed?.Invoke(); }
        }

        void Redraw()
        {
            var lut = CurveMath.BuildLut(Flatten());
            var pc = new PointCollection(130);
            for (int i = 0; i <= 128; i++)
            {
                double x = i / 128.0;
                pc.Add(new Point(x * S, S - CurveMath.Apply(lut, (float)x) * S));
            }
            _line.Points = pc;
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
                if (_canvas.Children[i] is Rectangle) _canvas.Children.RemoveAt(i);
            foreach (var p in _pts)
            {
                var r = new Rectangle { Width = 8, Height = 8, Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(r, p.X * S - 4);
                Canvas.SetTop(r, S - p.Y * S - 4);
                _canvas.Children.Add(r);
            }
        }
    }

    /// <summary>Colour wheel for colour grading: angle = hue, distance from the centre = saturation.</summary>
    public sealed class ColorWheel : Grid
    {
        static BitmapSource _disc;
        readonly Ellipse _handle;
        readonly double _size;
        double _hue, _sat;

        public event Action Changed;

        public ColorWheel(double size)
        {
            _size = size;
            Width = Height = size;
            var img = new Image { Source = Disc(), Width = size, Height = size, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Children.Add(img);
            var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _handle = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsHitTestVisible = false };
            canvas.Children.Add(_handle);
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Hue = 0; Saturation = 0; Changed?.Invoke(); return; }
                canvas.CaptureMouse();
                Pick(e.GetPosition(canvas));
            };
            canvas.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && canvas.IsMouseCaptured) Pick(e.GetPosition(canvas)); };
            canvas.MouseLeftButtonUp += (s, e) => canvas.ReleaseMouseCapture();
            Children.Add(canvas);
            ToolTip = T("Trascina per scegliere il colore (distanza dal centro = intensità). Doppio clic per azzerare.");
            Place();
        }

        public double Hue { get => _hue; set { _hue = value; Place(); } }
        public double Saturation { get => _sat; set { _sat = Math.Clamp(value, 0, 100); Place(); } }

        void Pick(Point p)
        {
            double r = _size / 2, dx = p.X - r, dy = p.Y - r;
            double dist = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / (r - 2));
            _hue = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
            _sat = Math.Round(dist * 100);
            Place();
            Changed?.Invoke();
        }

        void Place()
        {
            double r = _size / 2 - 2, a = _hue * Math.PI / 180, d = _sat / 100 * r;
            Canvas.SetLeft(_handle, _size / 2 + Math.Cos(a) * d - 6);
            Canvas.SetTop(_handle, _size / 2 - Math.Sin(a) * d - 6);
        }

        static BitmapSource Disc()
        {
            if (_disc != null) return _disc;
            const int n = 160;
            var px = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double dx = x + 0.5 - n / 2.0, dy = y + 0.5 - n / 2.0, d = Math.Sqrt(dx * dx + dy * dy) / (n / 2.0);
                    int i = (y * n + x) * 4;
                    if (d > 1) continue;
                    double h = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
                    var c = Hsv(h, d * 0.85, 0.85);
                    double a = Math.Clamp((1 - d) * n / 2, 0, 1);   // anti-aliased edge
                    px[i] = (byte)(c.B * a); px[i + 1] = (byte)(c.G * a); px[i + 2] = (byte)(c.R * a); px[i + 3] = (byte)(255 * a);
                }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            return _disc = bmp;
        }

        internal static Color Hsv(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360 / 60;
            double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
            (double r, double g, double b) = (int)h switch
            {
                0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }

    /// <summary>Black-to-white bar showing which tones a zone-of-light mask selects.</summary>
    public sealed class RangeBar : Grid
    {
        readonly Canvas _canvas = new Canvas { ClipToBounds = true };
        readonly Rectangle _band = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(110, 0x2F, 0x80, 0xED)), Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xFF)), StrokeThickness = 1.5 };
        double _low, _high = 1, _feather = 0.1;

        public RangeBar()
        {
            Height = 22;
            Margin = new Thickness(0, 4, 0, 4);
            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new LinearGradientBrush(Colors.Black, Colors.White, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), BorderThickness = new Thickness(1),
            });
            _canvas.Children.Add(_band);
            Children.Add(_canvas);
            SizeChanged += (s, e) => Place();
            IsHitTestVisible = false;
        }

        public void Set(double low, double high, double feather)
        {
            _low = low; _high = high; _feather = feather;
            Place();
        }

        void Place()
        {
            double w = ActualWidth;
            if (w <= 0) return;
            double l = Math.Clamp(_low - (_low > 0 ? _feather / 2 : 0), 0, 1), r = Math.Clamp(_high + (_high < 1 ? _feather / 2 : 0), 0, 1);
            Canvas.SetLeft(_band, l * w);
            Canvas.SetTop(_band, 1);
            _band.Width = Math.Max(3, (r - l) * w);
            _band.Height = Math.Max(1, ActualHeight - 2);
        }
    }
}
