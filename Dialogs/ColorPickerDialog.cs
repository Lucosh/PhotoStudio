using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>HSB color picker in the style of Photoshop's "Selettore colore".</summary>
    public sealed class ColorPickerDialog : DialogWindow
    {
        const double Size = 256;
        double _h, _s, _v;
        bool _updating;
        readonly Rectangle _hueLayer = new Rectangle { Width = Size, Height = Size };
        readonly Ellipse _svMarker = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
        readonly Rectangle _hueMarker = new Rectangle { Width = 26, Height = 4, Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false };
        readonly Rectangle _newSwatch = new Rectangle { Width = 70, Height = 34 };
        readonly TextBox _r, _g, _b, _hBox, _sBox, _vBox, _hex;

        public ColorPickerDialog(Window owner, string title, Color initial) : base(owner, title)
        {
            SelectedColor = initial;
            RgbToHsv(initial, out _h, out _s, out _v);

            // Saturation/brightness square
            var sv = new Canvas { Width = Size, Height = Size, ClipToBounds = true, Cursor = Cursors.Cross };
            sv.Children.Add(_hueLayer);
            sv.Children.Add(new Rectangle
            {
                Width = Size, Height = Size,
                Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0),
            });
            sv.Children.Add(new Rectangle
            {
                Width = Size, Height = Size,
                Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90),
            });
            sv.Children.Add(_svMarker);
            sv.MouseLeftButtonDown += (s, e) => { sv.CaptureMouse(); PickSv(e.GetPosition(sv)); };
            sv.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && sv.IsMouseCaptured) PickSv(e.GetPosition(sv)); };
            sv.MouseLeftButtonUp += (s, e) => sv.ReleaseMouseCapture();

            // Hue bar
            var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(HsvToColor((360 - i * 60) % 360, 1, 1), i / 6.0));
            var hue = new Canvas { Width = 24, Height = Size, Margin = new Thickness(12, 0, 0, 0), Cursor = Cursors.Hand, ClipToBounds = false };
            hue.Children.Add(new Rectangle { Width = 24, Height = Size, Fill = hueBrush });
            hue.Children.Add(_hueMarker);
            hue.MouseLeftButtonDown += (s, e) => { hue.CaptureMouse(); PickHue(e.GetPosition(hue)); };
            hue.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && hue.IsMouseCaptured) PickHue(e.GetPosition(hue)); };
            hue.MouseLeftButtonUp += (s, e) => hue.ReleaseMouseCapture();

            // Right column
            var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            right.Children.Add(new TextBlock { Text = T("nuovo"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray });
            right.Children.Add(_newSwatch);
            right.Children.Add(new Rectangle { Width = 70, Height = 34, Fill = new SolidColorBrush(initial) });
            right.Children.Add(new TextBlock { Text = T("attuale"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12) });

            _hBox = Field(right, "H:", "°");
            _sBox = Field(right, "S:", "%");
            _vBox = Field(right, "B:", "%");
            right.Children.Add(new Border { Height = 8 });
            _r = Field(right, "R:", "");
            _g = Field(right, "G:", "");
            _b = Field(right, "B:", "");
            right.Children.Add(new Border { Height = 8 });
            _hex = Field(right, "#", "", 70);

            foreach (var tb in new[] { _hBox, _sBox, _vBox }) tb.LostKeyboardFocus += (s, e) => FromHsvFields();
            foreach (var tb in new[] { _r, _g, _b }) tb.LostKeyboardFocus += (s, e) => FromRgbFields();
            _hex.LostKeyboardFocus += (s, e) => FromHex();

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Child = sv, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1) });
            row.Children.Add(hue);
            row.Children.Add(right);
            Body.Children.Add(row);

            Update();
        }

        public Color SelectedColor { get; private set; }

        protected override void OnOk()
        {
            // Enter triggers OK without moving focus: apply the field being edited first.
            if (Keyboard.FocusedElement == _hex) FromHex();
            else if (Keyboard.FocusedElement == _r || Keyboard.FocusedElement == _g || Keyboard.FocusedElement == _b) FromRgbFields();
            else if (Keyboard.FocusedElement is TextBox) FromHsvFields();
            base.OnOk();
        }

        static TextBox Field(Panel parent, string label, string suffix, double width = 48)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(new TextBlock { Text = label, Width = 22, VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBox { Width = width, Height = 22 };
            sp.Children.Add(tb);
            sp.Children.Add(new TextBlock { Text = suffix, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            parent.Children.Add(sp);
            return tb;
        }

        void PickSv(Point p)
        {
            _s = Math.Clamp(p.X / Size, 0, 1);
            _v = Math.Clamp(1 - p.Y / Size, 0, 1);
            Update();
        }

        void PickHue(Point p)
        {
            _h = Math.Clamp(360 - p.Y / Size * 360, 0, 360) % 360;
            Update();
        }

        void FromHsvFields()
        {
            if (_updating) return;
            if (TryParse(_hBox.Text, out double h)) _h = Math.Clamp(h, 0, 359.9);
            if (TryParse(_sBox.Text, out double s)) _s = Math.Clamp(s / 100, 0, 1);
            if (TryParse(_vBox.Text, out double v)) _v = Math.Clamp(v / 100, 0, 1);
            Update();
        }

        void FromRgbFields()
        {
            if (_updating) return;
            var c = SelectedColor;
            byte P(TextBox t, byte d) => TryParse(t.Text, out double v) ? (byte)Math.Clamp(Math.Round(v), 0, 255) : d;
            var nc = Color.FromRgb(P(_r, c.R), P(_g, c.G), P(_b, c.B));
            RgbToHsv(nc, out _h, out _s, out _v);
            Update(nc);
        }

        void FromHex()
        {
            if (_updating) return;
            string t = _hex.Text.Trim().TrimStart('#');
            if (t.Length == 3) t = string.Concat(t[0], t[0], t[1], t[1], t[2], t[2]);
            if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            {
                var nc = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                RgbToHsv(nc, out _h, out _s, out _v);
                Update(nc);
            }
            else Update();
        }

        void Update(Color? exact = null)
        {
            _updating = true;
            var c = exact ?? HsvToColor(_h, _s, _v);
            SelectedColor = c;
            _hueLayer.Fill = new SolidColorBrush(HsvToColor(_h, 1, 1));
            _newSwatch.Fill = new SolidColorBrush(c);
            Canvas.SetLeft(_svMarker, _s * Size - 6);
            Canvas.SetTop(_svMarker, (1 - _v) * Size - 6);
            _svMarker.Stroke = _v > 0.6 && _s < 0.4 ? Brushes.Black : Brushes.White;
            Canvas.SetLeft(_hueMarker, -1);
            Canvas.SetTop(_hueMarker, (360 - _h) / 360 * Size - 2);
            _hBox.Text = Math.Round(_h).ToString(CultureInfo.InvariantCulture);
            _sBox.Text = Math.Round(_s * 100).ToString(CultureInfo.InvariantCulture);
            _vBox.Text = Math.Round(_v * 100).ToString(CultureInfo.InvariantCulture);
            _r.Text = c.R.ToString(); _g.Text = c.G.ToString(); _b.Text = c.B.ToString();
            _hex.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
            _updating = false;
        }

        public static Color HsvToColor(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360 / 60;
            int i = (int)Math.Floor(h);
            double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        public static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max == 0 ? 0 : d / max;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d + 6) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
    }
}
