using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    public sealed class NewImageDialog : DialogWindow
    {
        static readonly (string Name, int W, int H)[] Presets =
        {
            (T("Personalizzato"), 0, 0),
            (T("Full HD 1920 × 1080"), 1920, 1080),
            (T("4K UHD 3840 × 2160"), 3840, 2160),
            (T("Instagram post 1080 × 1080"), 1080, 1080),
            (T("Instagram storia 1080 × 1920"), 1080, 1920),
            (T("A4 300 ppi 2480 × 3508"), 2480, 3508),
            (T("Web 1280 × 720"), 1280, 720),
            ("800 × 600", 800, 600),
        };

        readonly TextBox _w, _h;
        readonly ComboBox _bg;

        public NewImageDialog(Window owner, int defW, int defH) : base(owner, T("Nuovo documento"))
        {
            var preset = new ComboBox { Width = 230 };
            foreach (var p in Presets) preset.Items.Add(p.Name);
            preset.SelectedIndex = 0;
            _w = NumberBox(defW);
            _h = NumberBox(defH);
            _bg = new ComboBox { Width = 230, ItemsSource = new[] { T("Bianco"), T("Nero"), T("Trasparente"), T("Colore di sfondo") }, SelectedIndex = 0 };
            preset.SelectionChanged += (s, e) =>
            {
                var p = Presets[preset.SelectedIndex];
                if (p.W > 0) { _w.Text = p.W.ToString(); _h.Text = p.H.ToString(); }
            };
            Body.Children.Add(Row(Label(T("Predefinito:")), preset));
            Body.Children.Add(Row(Label(T("Larghezza:")), _w, new TextBlock { Text = T(" pixel"), VerticalAlignment = VerticalAlignment.Center }));
            Body.Children.Add(Row(Label(T("Altezza:")), _h, new TextBlock { Text = T(" pixel"), VerticalAlignment = VerticalAlignment.Center }));
            Body.Children.Add(Row(Label(T("Sfondo:")), _bg));
        }

        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        /// <summary>0 white, 1 black, 2 transparent, 3 background color.</summary>
        public int BackgroundChoice { get; private set; }

        protected override void OnOk()
        {
            if (!TryParse(_w.Text, out double w) || !TryParse(_h.Text, out double h) || w < 1 || h < 1 || w > 30000 || h > 30000)
            {
                Error(T("Inserisci dimensioni valide (1 - 30000 pixel)."));
                return;
            }
            PixelWidth = (int)w; PixelHeight = (int)h; BackgroundChoice = _bg.SelectedIndex;
            base.OnOk();
        }
    }

    public sealed class ResizeDialog : DialogWindow
    {
        readonly int _ow, _oh;
        readonly TextBox _w, _h;
        readonly ComboBox _unit;
        readonly CheckBox _keep;
        bool _sync;

        public ResizeDialog(Window owner, int w, int h) : base(owner, T("Dimensione immagine"))
        {
            _ow = w; _oh = h;
            _w = NumberBox(w);
            _h = NumberBox(h);
            _unit = new ComboBox { Width = 110, ItemsSource = new[] { T("Pixel"), T("Percentuale") }, SelectedIndex = 0, Margin = new Thickness(8, 0, 0, 0) };
            _keep = new CheckBox { Content = T("Mantieni proporzioni"), IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
            Body.Children.Add(new TextBlock { Text = T("Dimensione attuale: {0} × {1} pixel", w, h), Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12) });
            Body.Children.Add(Row(Label(T("Larghezza:")), _w, _unit));
            Body.Children.Add(Row(Label(T("Altezza:")), _h));
            Body.Children.Add(_keep);

            _w.TextChanged += (s, e) => Sync(true);
            _h.TextChanged += (s, e) => Sync(false);
            _unit.SelectionChanged += (s, e) =>
            {
                _sync = true;
                bool pct = _unit.SelectedIndex == 1;
                _w.Text = pct ? "100" : _ow.ToString();
                _h.Text = pct ? "100" : _oh.ToString();
                _sync = false;
            };
        }

        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }

        void Sync(bool fromWidth)
        {
            if (_sync || _keep.IsChecked != true) return;
            _sync = true;
            bool pct = _unit.SelectedIndex == 1;
            if (fromWidth && TryParse(_w.Text, out double w))
                _h.Text = pct ? w.ToString("0.##") : Math.Max(1, Math.Round(w * _oh / _ow)).ToString();
            else if (!fromWidth && TryParse(_h.Text, out double h))
                _w.Text = pct ? h.ToString("0.##") : Math.Max(1, Math.Round(h * _ow / _oh)).ToString();
            _sync = false;
        }

        protected override void OnOk()
        {
            if (!TryParse(_w.Text, out double w) || !TryParse(_h.Text, out double h) || w <= 0 || h <= 0)
            {
                Error(T("Inserisci valori validi."));
                return;
            }
            if (_unit.SelectedIndex == 1) { w = _ow * w / 100; h = _oh * h / 100; }
            NewWidth = (int)Math.Round(w); NewHeight = (int)Math.Round(h);
            if (NewWidth < 1 || NewHeight < 1 || NewWidth > 30000 || NewHeight > 30000)
            {
                Error(T("Le dimensioni devono essere comprese tra 1 e 30000 pixel."));
                return;
            }
            base.OnOk();
        }
    }

    public sealed class CanvasSizeDialog : DialogWindow
    {
        readonly TextBox _w, _h;
        readonly RadioButton[] _anchors = new RadioButton[9];

        public CanvasSizeDialog(Window owner, int w, int h) : base(owner, T("Dimensione quadro"))
        {
            _w = NumberBox(w);
            _h = NumberBox(h);
            Body.Children.Add(new TextBlock { Text = T("Dimensione attuale: {0} × {1} pixel", w, h), Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12) });
            Body.Children.Add(Row(Label(T("Larghezza:")), _w));
            Body.Children.Add(Row(Label(T("Altezza:")), _h));
            var grid = new UniformGrid3x3();
            for (int i = 0; i < 9; i++)
            {
                _anchors[i] = new RadioButton { GroupName = "anchor", Width = 26, Height = 26, Margin = new Thickness(1), IsChecked = i == 4 };
                _anchors[i].Template = AnchorTemplate();
                grid.Children.Add(_anchors[i]);
            }
            Body.Children.Add(Row(Label(T("Ancoraggio:")), grid));
        }

        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }
        public int AnchorX { get; private set; }
        public int AnchorY { get; private set; }

        static ControlTemplate AnchorTemplate()
        {
            var t = new ControlTemplate(typeof(RadioButton));
            var border = new FrameworkElementFactory(typeof(Border), "B");
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            t.VisualTree = border;
            var trig = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BackgroundProperty, Application.Current.FindResource("AccentBrush"), "B"));
            t.Triggers.Add(trig);
            return t;
        }

        protected override void OnOk()
        {
            if (!TryParse(_w.Text, out double w) || !TryParse(_h.Text, out double h) || w < 1 || h < 1 || w > 30000 || h > 30000)
            {
                Error(T("Inserisci dimensioni valide (1 - 30000 pixel)."));
                return;
            }
            NewWidth = (int)w; NewHeight = (int)h;
            int idx = Array.FindIndex(_anchors, a => a.IsChecked == true);
            AnchorX = idx % 3; AnchorY = idx / 3;
            base.OnOk();
        }

        sealed class UniformGrid3x3 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid3x3() { Rows = 3; Columns = 3; }
        }
    }

    public sealed class TextDialog : DialogWindow
    {
        readonly TextBox _text;

        public TextDialog(Window owner, string initial, FontFamily font) : base(owner, T("Testo"))
        {
            _text = new TextBox
            {
                Text = initial, Width = 380, Height = 120, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top, FontFamily = font, FontSize = 16,
            };
            Body.Children.Add(new TextBlock { Text = T("Testo da inserire (usa la barra delle opzioni per font e dimensione):"), Margin = new Thickness(0, 0, 0, 8) });
            Body.Children.Add(_text);
            Loaded += (s, e) => { _text.Focus(); _text.SelectAll(); };
        }

        public string Text => _text.Text;
    }

    public sealed class InputDialog : DialogWindow
    {
        readonly TextBox _box;

        public InputDialog(Window owner, string title, string label, string value) : base(owner, title)
        {
            _box = new TextBox { Text = value, Width = 260, Height = 24 };
            Body.Children.Add(Row(Label(label, 60), _box));
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }

        public string Value => _box.Text;
    }
}
