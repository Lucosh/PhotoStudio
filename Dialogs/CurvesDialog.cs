using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>Photoshop-like Curves: RGB master curve plus per-channel curves.</summary>
    public sealed class CurvesDialog : DialogWindow
    {
        const double S = 256;
        readonly List<Point>[] _curves = new List<Point>[4]; // 0 RGB, 1 R, 2 G, 3 B (x input, y output, 0..255)
        readonly Canvas _canvas;
        readonly Polyline _line = new Polyline { StrokeThickness = 1.6 };
        readonly Polygon _hist = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(70, 200, 200, 200)) };
        readonly TextBlock _info = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };
        readonly ComboBox _channel;
        readonly int[][] _histograms;
        readonly Action<byte[][]> _preview;
        readonly DispatcherTimer _timer;
        int _drag = -1;

        public CurvesDialog(Window owner, byte[] pixels, Action<byte[][]> preview) : base(owner, T("Curve"))
        {
            _preview = preview;
            for (int i = 0; i < 4; i++) _curves[i] = new List<Point> { new Point(0, 0), new Point(255, 255) };
            _histograms = BuildHistograms(pixels);

            _channel = new ComboBox { Width = 120, ItemsSource = new[] { "RGB", T("Rosso"), T("Verde"), T("Blu") }, SelectedIndex = 0 };
            _channel.SelectionChanged += (s, e) => Redraw();
            Body.Children.Add(Row(Label(T("Canale:"), 60), _channel));

            _canvas = new Canvas { Width = S, Height = S, Background = (Brush)Application.Current.FindResource("InputBg"), ClipToBounds = true, Cursor = Cursors.Cross };
            var grid = new SolidColorBrush(Color.FromRgb(70, 70, 70));
            _canvas.Children.Add(_hist);
            for (int i = 1; i < 4; i++)
            {
                _canvas.Children.Add(new Line { X1 = i * S / 4, X2 = i * S / 4, Y1 = 0, Y2 = S, Stroke = grid });
                _canvas.Children.Add(new Line { Y1 = i * S / 4, Y2 = i * S / 4, X1 = 0, X2 = S, Stroke = grid });
            }
            _canvas.Children.Add(new Line { X1 = 0, Y1 = S, X2 = S, Y2 = 0, Stroke = grid, StrokeDashArray = new DoubleCollection { 3, 3 } });
            _canvas.Children.Add(_line);
            _canvas.MouseLeftButtonDown += Canvas_Down;
            _canvas.MouseMove += Canvas_Move;
            _canvas.MouseLeftButtonUp += (s, e) => { _drag = -1; _canvas.ReleaseMouseCapture(); };
            _canvas.MouseRightButtonDown += Canvas_Right;

            Body.Children.Add(new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), Child = _canvas, HorizontalAlignment = HorizontalAlignment.Left });
            Body.Children.Add(_info);
            Body.Children.Add(new TextBlock
            {
                Text = T("Clic per aggiungere un punto, trascina per modificarlo,\nclic destro per eliminarlo."),
                Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
                Margin = new Thickness(0, 4, 0, 6),
            });

            var reset = new Button { Content = T("Ripristina"), MinWidth = 84 };
            reset.Click += (s, e) =>
            {
                for (int i = 0; i < 4; i++) _curves[i] = new List<Point> { new Point(0, 0), new Point(255, 255) };
                Redraw(); Changed();
            };
            ButtonBar.Children.Insert(0, reset);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (s, e) => { _timer.Stop(); _preview?.Invoke(Luts); };
            Loaded += (s, e) => Redraw();
        }

        List<Point> Current => _curves[_channel.SelectedIndex];

        /// <summary>Final LUTs for R, G, B (channel curve followed by the RGB master curve).</summary>
        public byte[][] Luts
        {
            get
            {
                var master = BuildLut(_curves[0]);
                var res = new byte[3][];
                for (int c = 0; c < 3; c++)
                {
                    var ch = BuildLut(_curves[c + 1]);
                    var l = new byte[256];
                    for (int i = 0; i < 256; i++) l[i] = master[ch[i]];
                    res[c] = l;
                }
                return res;
            }
        }

        void Changed() { _timer.Stop(); _timer.Start(); }

        static Point ToCurve(Point p) => new Point(Math.Clamp(p.X, 0, 255), Math.Clamp(255 - p.Y, 0, 255));

        void Canvas_Down(object sender, MouseButtonEventArgs e)
        {
            var p = ToCurve(e.GetPosition(_canvas));
            var pts = Current;
            _drag = pts.FindIndex(q => Math.Abs(q.X - p.X) < 7 && Math.Abs(q.Y - p.Y) < 7);
            if (_drag < 0)
            {
                if (pts.Any(q => Math.Abs(q.X - p.X) < 4)) return;
                pts.Add(p);
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                _drag = pts.IndexOf(p);
            }
            _canvas.CaptureMouse();
            Redraw(); Changed();
        }

        void Canvas_Move(object sender, MouseEventArgs e)
        {
            var p = ToCurve(e.GetPosition(_canvas));
            _info.Text = T("Input: {0:0}   Output: {1:0}", p.X, p.Y);
            if (_drag < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            var pts = Current;
            double minX = _drag > 0 ? pts[_drag - 1].X + 1 : 0;
            double maxX = _drag < pts.Count - 1 ? pts[_drag + 1].X - 1 : 255;
            pts[_drag] = new Point(Math.Clamp(p.X, minX, maxX), p.Y);
            Redraw(); Changed();
        }

        void Canvas_Right(object sender, MouseButtonEventArgs e)
        {
            var p = ToCurve(e.GetPosition(_canvas));
            var pts = Current;
            int i = pts.FindIndex(q => Math.Abs(q.X - p.X) < 7 && Math.Abs(q.Y - p.Y) < 7);
            if (i > 0 && i < pts.Count - 1) { pts.RemoveAt(i); Redraw(); Changed(); }
        }

        void Redraw()
        {
            int ch = _channel.SelectedIndex;
            var color = ch switch { 1 => Colors.IndianRed, 2 => Colors.LimeGreen, 3 => Colors.CornflowerBlue, _ => Colors.WhiteSmoke };
            _line.Stroke = new SolidColorBrush(color);
            var lut = BuildLut(Current);
            var pc = new PointCollection(256);
            for (int i = 0; i < 256; i++) pc.Add(new Point(i + 0.5, S - lut[i]));
            _line.Points = pc;

            var h = _histograms[ch];
            int max = 1;
            for (int i = 1; i < 255; i++) max = Math.Max(max, h[i]);
            var hp = new PointCollection(260) { new Point(0, S) };
            for (int i = 0; i < 256; i++) hp.Add(new Point(i, S - Math.Min(S, h[i] * S * 0.9 / max)));
            hp.Add(new Point(S, S));
            _hist.Points = hp;

            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
                if (_canvas.Children[i] is Rectangle) _canvas.Children.RemoveAt(i);
            foreach (var p in Current)
            {
                var r = new Rectangle { Width = 7, Height = 7, Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(r, p.X - 3.5);
                Canvas.SetTop(r, S - p.Y - 3.5);
                _canvas.Children.Add(r);
            }
        }

        static int[][] BuildHistograms(byte[] px)
        {
            var h = new int[4][];
            for (int i = 0; i < 4; i++) h[i] = new int[256];
            int step = Math.Max(1, px.Length / 4 / 300000) * 4;
            for (int i = 0; i < px.Length; i += step)
            {
                if (px[i + 3] == 0) continue;
                h[1][px[i + 2]]++; h[2][px[i + 1]]++; h[3][px[i]]++;
                h[0][(px[i + 2] * 77 + px[i + 1] * 150 + px[i] * 29) >> 8]++;
            }
            return h;
        }

        /// <summary>Monotone cubic (Fritsch-Carlson) interpolation through the control points.</summary>
        public static byte[] BuildLut(List<Point> points)
        {
            var p = points.OrderBy(q => q.X).ToList();
            int n = p.Count;
            var lut = new byte[256];
            if (n == 1)
            {
                for (int i = 0; i < 256; i++) lut[i] = ImageOps.ClampByte(p[0].Y);
                return lut;
            }
            var dx = new double[n - 1];
            var m = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                dx[i] = p[i + 1].X - p[i].X;
                m[i] = dx[i] == 0 ? 0 : (p[i + 1].Y - p[i].Y) / dx[i];
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
            for (int x = 0; x < 256; x++)
            {
                double y;
                if (x <= p[0].X) y = p[0].Y;
                else if (x >= p[n - 1].X) y = p[n - 1].Y;
                else
                {
                    while (seg < n - 2 && x > p[seg + 1].X) seg++;
                    double hh = dx[seg], u = (x - p[seg].X) / hh;
                    double u2 = u * u, u3 = u2 * u;
                    y = (2 * u3 - 3 * u2 + 1) * p[seg].Y + (u3 - 2 * u2 + u) * hh * t[seg]
                      + (-2 * u3 + 3 * u2) * p[seg + 1].Y + (u3 - u2) * hh * t[seg + 1];
                }
                lut[x] = ImageOps.ClampByte(y);
            }
            return lut;
        }
    }
}
