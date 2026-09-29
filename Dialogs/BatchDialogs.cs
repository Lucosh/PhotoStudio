using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PhotoStudio.Core;

namespace PhotoStudio.Dialogs
{
    /// <summary>Asks for a short name (e.g. of a preset).</summary>
    public sealed class NameDialog : DialogWindow
    {
        readonly TextBox _box;

        public NameDialog(Window owner, string title, string prompt, string initial) : base(owner, title)
        {
            Body.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
            _box = new TextBox { Text = initial ?? "", Width = 320, Height = 24 };
            Body.Children.Add(_box);
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }

        public string Value { get; private set; }

        protected override void OnOk()
        {
            string v = _box.Text.Trim();
            if (v.Length == 0) { Error("Scrivi un nome."); return; }
            Value = v;
            base.OnOk();
        }
    }

    /// <summary>"Applica impostazioni": copied settings or a preset, to the open photo or to the whole editing folder.</summary>
    public sealed class ApplySettingsDialog : DialogWindow
    {
        sealed class Source
        {
            public string Name { get; init; }
            public RawSettings Settings { get; init; }
        }

        readonly ComboBox _source, _minRating;
        readonly TextBlock _describe;
        readonly CheckBox _wb, _exposure, _tone, _presence, _detail, _curve, _color, _effects, _geometry, _masks;
        readonly RadioButton _toOpen, _toBatch, _saveNow, _startFrom;

        public ApplySettingsDialog(Window owner, RawSettings copied, bool hasOpenPhoto, int pendingCount) : base(owner, "Applica impostazioni di sviluppo")
        {
            var sources = new List<Source>();
            if (copied != null) sources.Add(new Source { Name = "Impostazioni copiate", Settings = copied });
            sources.AddRange(RawPreset.LoadAll().Select(p => new Source { Name = "Preset: " + p.Name, Settings = p.Settings }));

            _source = new ComboBox { Width = 330, ItemsSource = sources, DisplayMemberPath = nameof(Source.Name), SelectedIndex = 0 };
            _describe = new TextBlock { Width = 440, TextWrapping = TextWrapping.Wrap, Foreground = Dim, Margin = new Thickness(0, 0, 0, 12) };
            _source.SelectionChanged += (s, e) => UpdateDescription();
            Body.Children.Add(Row(Label("Impostazioni:"), _source));
            Body.Children.Add(_describe);

            Body.Children.Add(Header("DA APPLICARE"));
            _wb = Check("Bilanciamento del bianco", true);
            _exposure = Check("Esposizione", true);
            _tone = Check("Tono (contrasto, luci, ombre, bianchi, neri)", true);
            _presence = Check("Presenza (texture, chiarezza, foschia, vividezza, saturazione)", true);
            _detail = Check("Nitidezza e rumore", true);
            _curve = Check("Curva di tono", true);
            _color = Check("Mix colori e color grading", true);
            _effects = Check("Vignettatura e grana", true);
            _masks = Check("Maschere", true);
            _geometry = Check("Raddrizza e ritaglio", false);
            _geometry.ToolTip = "Di solito ogni foto ha il suo ritaglio: attivalo solo per foto con la stessa inquadratura";
            var groups = new WrapPanel { Width = 440, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var c in new[] { _wb, _exposure, _tone, _presence, _detail, _curve, _color, _effects, _masks, _geometry }) groups.Children.Add(c);
            Body.Children.Add(groups);

            Body.Children.Add(Header("A QUALI FOTO"));
            _toOpen = new RadioButton { Content = "Solo la foto aperta", GroupName = "target", IsEnabled = hasOpenPhoto, Margin = new Thickness(0, 0, 0, 6) };
            _toBatch = new RadioButton
            {
                Content = pendingCount > 0 ? $"Foto della cartella di modifica non ancora salvate ({pendingCount})" : "Foto della cartella di modifica (nessuna da salvare)",
                GroupName = "target", IsEnabled = pendingCount > 0, Margin = new Thickness(0, 0, 0, 6),
            };
            Body.Children.Add(_toOpen);
            Body.Children.Add(_toBatch);

            var batchOptions = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
            _minRating = new ComboBox { Width = 150, SelectedIndex = 0, ItemsSource = new[] { "tutte", "★ o più", "★★ o più", "★★★ o più", "★★★★ o più", "★★★★★" } };
            batchOptions.Children.Add(Row(new TextBlock { Text = "Solo le foto con:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, _minRating));
            _saveNow = new RadioButton { Content = "Sviluppa e salva subito nella cartella \"Salva in\" (con le opzioni di esportazione)", GroupName = "mode", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
            _startFrom = new RadioButton { Content = "Usale come punto di partenza: le rifinisco quando apro ogni foto", GroupName = "mode", Margin = new Thickness(0, 0, 0, 6) };
            batchOptions.Children.Add(_saveNow);
            batchOptions.Children.Add(_startFrom);
            Body.Children.Add(batchOptions);
            _toBatch.Checked += (s, e) => batchOptions.IsEnabled = true;
            _toOpen.Checked += (s, e) => batchOptions.IsEnabled = false;

            if (pendingCount > 0) _toBatch.IsChecked = true; else _toOpen.IsChecked = true;
            batchOptions.IsEnabled = _toBatch.IsChecked == true;
            OkButton.Content = "Applica";
            UpdateDescription();
        }

        public RawSettings Settings { get; private set; }
        public SettingsGroups Groups { get; private set; }
        public bool ToOpenPhoto { get; private set; }
        public bool SaveNow { get; private set; }
        public int MinRating { get; private set; }

        static Brush Dim => (Brush)Application.Current.FindResource("TextDimBrush");

        static TextBlock Header(string text) =>
            new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Dim, Margin = new Thickness(0, 0, 0, 6) };

        static CheckBox Check(string text, bool on) => new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 0, 16, 6) };

        void UpdateDescription() =>
            _describe.Text = _source.SelectedItem is Source s ? "Valori: " + s.Settings.Describe() + $", nitidezza {s.Settings.Sharpening:0}" : "";

        protected override void OnOk()
        {
            if (_source.SelectedItem is not Source s) { Error("Non ci sono impostazioni da applicare."); return; }
            var g = SettingsGroups.None;
            if (_wb.IsChecked == true) g |= SettingsGroups.WhiteBalance;
            if (_exposure.IsChecked == true) g |= SettingsGroups.Exposure;
            if (_tone.IsChecked == true) g |= SettingsGroups.Tone;
            if (_presence.IsChecked == true) g |= SettingsGroups.Presence;
            if (_detail.IsChecked == true) g |= SettingsGroups.Detail;
            if (_curve.IsChecked == true) g |= SettingsGroups.Curve;
            if (_color.IsChecked == true) g |= SettingsGroups.Color;
            if (_effects.IsChecked == true) g |= SettingsGroups.Effects;
            if (_masks.IsChecked == true) g |= SettingsGroups.Masks;
            if (_geometry.IsChecked == true) g |= SettingsGroups.Geometry;
            if (g == SettingsGroups.None) { Error("Scegli almeno un gruppo di impostazioni da applicare."); return; }
            Settings = s.Settings.Clone();
            Groups = g;
            ToOpenPhoto = _toOpen.IsChecked == true;
            SaveNow = _saveNow.IsChecked == true;
            MinRating = _minRating.SelectedIndex;
            base.OnOk();
        }
    }

    /// <summary>"Luce intelligente": strength and which photos.</summary>
    public sealed class SmartLightDialog : DialogWindow
    {
        readonly Slider _intensity;
        readonly RadioButton _toOpen, _toBatch, _saveNow;
        readonly ComboBox _minRating;

        public SmartLightDialog(Window owner, bool hasOpenPhoto, int pendingCount, double intensity) : base(owner, "☀ Luce intelligente")
        {
            Body.Children.Add(new TextBlock
            {
                Width = 440, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                Text = "Sistema la luce in un clic: esposizione e toni come l'Automatico di Camera Raw, poi maschere di luce solo dove servono " +
                       "(schiarisce le zone scure, recupera luci e cielo, dà luce al soggetto). Ogni foto viene analizzata da sola.",
            });
            var value = new TextBlock { Width = 44, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            _intensity = new Slider { Width = 250, Minimum = 0, Maximum = 150, Value = intensity, SmallChange = 5, LargeChange = 25, VerticalAlignment = VerticalAlignment.Center };
            _intensity.ValueChanged += (s, e) => value.Text = Math.Round(_intensity.Value) + " %";
            value.Text = Math.Round(intensity) + " %";
            Body.Children.Add(Row(Label("Intensità:"), _intensity, value));
            Body.Children.Add(new TextBlock { Text = "50% = tocco leggero · 100% = normale · 150% = deciso", Foreground = (Brush)Application.Current.FindResource("TextDimBrush"), Margin = new Thickness(118, -6, 0, 12) });

            _toOpen = new RadioButton { Content = "Solo la foto aperta", GroupName = "t", IsEnabled = hasOpenPhoto, Margin = new Thickness(0, 0, 0, 6) };
            _toBatch = new RadioButton
            {
                Content = pendingCount > 0 ? $"Foto della cartella di modifica non ancora salvate ({pendingCount})" : "Foto della cartella di modifica (nessuna)",
                GroupName = "t", IsEnabled = pendingCount > 0, Margin = new Thickness(0, 0, 0, 6),
            };
            Body.Children.Add(_toOpen);
            Body.Children.Add(_toBatch);
            var batch = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
            _minRating = new ComboBox { Width = 150, SelectedIndex = 0, ItemsSource = new[] { "tutte", "★ o più", "★★ o più", "★★★ o più", "★★★★ o più", "★★★★★" } };
            batch.Children.Add(Row(new TextBlock { Text = "Solo le foto con:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, _minRating));
            _saveNow = new RadioButton { Content = "Sistema e salva subito nella cartella \"Salva in\"", GroupName = "m", Margin = new Thickness(0, 0, 0, 6) };
            var later = new RadioButton { Content = "Prepara soltanto: le trovo sistemate quando apro ogni foto", GroupName = "m", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
            batch.Children.Add(_saveNow);
            batch.Children.Add(later);
            Body.Children.Add(batch);
            _toBatch.Checked += (s, e) => batch.IsEnabled = true;
            _toOpen.Checked += (s, e) => batch.IsEnabled = false;
            if (hasOpenPhoto) _toOpen.IsChecked = true; else _toBatch.IsChecked = true;
            batch.IsEnabled = _toBatch.IsChecked == true;
            OkButton.Content = "☀ Applica";
        }

        public double Intensity { get; private set; }
        public bool ToOpenPhoto { get; private set; }
        public bool SaveNow { get; private set; }
        public int MinRating { get; private set; }

        protected override void OnOk()
        {
            Intensity = Math.Round(_intensity.Value);
            ToOpenPhoto = _toOpen.IsChecked == true;
            SaveNow = _saveNow.IsChecked == true;
            MinRating = _minRating.SelectedIndex;
            base.OnOk();
        }
    }

    /// <summary>Size, file names and watermark of the photos saved by the editing folder.</summary>
    public sealed class ExportOptionsDialog : DialogWindow
    {
        readonly CheckBox _resize, _rename, _watermark;
        readonly TextBox _longSide, _prefix, _start, _digits, _text, _size, _opacity;
        readonly ComboBox _position;
        readonly TextBlock _example;
        readonly string _sampleName;

        public ExportOptionsDialog(Window owner, ExportOptions o, string sampleName) : base(owner, "Opzioni di esportazione")
        {
            _sampleName = string.IsNullOrEmpty(sampleName) ? "IMG_0001" : sampleName;

            _resize = new CheckBox { Content = "Ridimensiona", IsChecked = o.Resize, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
            Body.Children.Add(_resize);
            _longSide = NumberBox(o.LongSide);
            var sizeRow = Row(Label("Lato lungo:"), _longSide, Hint(" pixel (le foto più piccole non vengono ingrandite)"));
            Body.Children.Add(sizeRow);
            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(118, -4, 0, 12) };
            foreach (var n in new[] { 1080, 2048, 3000, 4096 })
            {
                var b = new Button { Content = n.ToString(), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += (s, e) => { _longSide.Text = n.ToString(); _resize.IsChecked = true; };
                presets.Children.Add(b);
            }
            Body.Children.Add(presets);

            _rename = new CheckBox { Content = "Rinomina i file", IsChecked = o.Rename, Margin = new Thickness(0, 4, 0, 8), FontWeight = FontWeights.SemiBold };
            Body.Children.Add(_rename);
            _prefix = new TextBox { Text = o.Prefix, Width = 200, Height = 24 };
            Body.Children.Add(Row(Label("Nome:"), _prefix));
            _start = NumberBox(o.StartNumber);
            _digits = NumberBox(o.Digits, 40);
            Body.Children.Add(Row(Label("Parti da numero:"), _start, Hint("   cifre: "), _digits));
            _example = new TextBlock { Foreground = (Brush)Application.Current.FindResource("TextDimBrush"), Margin = new Thickness(118, -2, 0, 12) };
            Body.Children.Add(_example);

            _watermark = new CheckBox { Content = "Filigrana (testo)", IsChecked = o.Watermark, Margin = new Thickness(0, 4, 0, 8), FontWeight = FontWeights.SemiBold };
            Body.Children.Add(_watermark);
            _text = new TextBox { Text = o.WatermarkText, Width = 260, Height = 24 };
            Body.Children.Add(Row(Label("Testo:"), _text));
            _position = new ComboBox
            {
                Width = 160, ItemsSource = new[] { "In basso a destra", "In basso a sinistra", "In alto a destra", "In alto a sinistra", "Al centro" },
                SelectedIndex = (int)o.Position,
            };
            Body.Children.Add(Row(Label("Posizione:"), _position));
            _size = NumberBox(o.WatermarkSize, 50);
            _opacity = NumberBox(o.WatermarkOpacity, 50);
            Body.Children.Add(Row(Label("Dimensione:"), _size, Hint(" % del lato lungo      Opacità: "), _opacity, Hint(" %")));

            Body.Children.Add(new TextBlock
            {
                Text = "Formato e qualità si scelgono nella barra della cartella di modifica. Gli originali non vengono mai sovrascritti.",
                Foreground = (Brush)Application.Current.FindResource("TextDimBrush"), TextWrapping = TextWrapping.Wrap, Width = 440, Margin = new Thickness(0, 6, 0, 0),
            });

            foreach (var tb in new[] { _prefix, _start, _digits }) tb.TextChanged += (s, e) => UpdateExample();
            _rename.Checked += (s, e) => UpdateExample();
            _rename.Unchecked += (s, e) => UpdateExample();
            UpdateExample();
        }

        public ExportOptions Options { get; private set; }

        static TextBlock Hint(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

        void UpdateExample()
        {
            var o = new ExportOptions { Rename = _rename.IsChecked == true, Prefix = _prefix.Text };
            if (int.TryParse(_start.Text.Trim(), out int st)) o.StartNumber = st;
            if (int.TryParse(_digits.Text.Trim(), out int d)) o.Digits = d;
            _example.Text = "Esempio: " + o.FileName(_sampleName, 0) + ".jpg, " + o.FileName(_sampleName, 1) + ".jpg...";
        }

        protected override void OnOk()
        {
            var o = new ExportOptions
            {
                Resize = _resize.IsChecked == true, Rename = _rename.IsChecked == true, Watermark = _watermark.IsChecked == true,
                Prefix = _prefix.Text.Trim(), WatermarkText = _text.Text, Position = (WatermarkPosition)Math.Max(0, _position.SelectedIndex),
            };
            if (!int.TryParse(_longSide.Text.Trim(), out int ls) || ls < 16 || ls > 30000) { Error("Lato lungo: inserisci un valore tra 16 e 30000 pixel."); return; }
            if (!int.TryParse(_start.Text.Trim(), out int st) || st < 0) { Error("Numero iniziale non valido."); return; }
            if (!int.TryParse(_digits.Text.Trim(), out int dg) || dg < 1 || dg > 6) { Error("Cifre: da 1 a 6."); return; }
            if (!TryParse(_size.Text, out double sz) || sz < 0.5 || sz > 20) { Error("Dimensione della filigrana: da 0,5 a 20 %."); return; }
            if (!TryParse(_opacity.Text, out double op) || op < 1 || op > 100) { Error("Opacità della filigrana: da 1 a 100 %."); return; }
            if (o.Rename && o.Prefix.Length == 0) { Error("Scrivi il nome da usare per i file."); return; }
            o.LongSide = ls; o.StartNumber = st; o.Digits = dg; o.WatermarkSize = sz; o.WatermarkOpacity = op;
            Options = o;
            base.OnOk();
        }
    }

    /// <summary>Modeless progress window with a Cancel button.</summary>
    public sealed class ProgressWindow : Window
    {
        readonly TextBlock _text;
        readonly ProgressBar _bar;
        bool _done;

        public ProgressWindow(Window owner, string title, int total)
        {
            Owner = owner;
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = (Brush)Application.Current.FindResource("PanelBg");
            Foreground = (Brush)Application.Current.FindResource("TextBrush");
            DarkTitleBar.Apply(this);
            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            _text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 10) };
            _bar = new ProgressBar { Height = 14, Maximum = Math.Max(1, total) };
            var cancel = new Button { Content = "Annulla", MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            cancel.Click += (s, e) => { Cancelled = true; _text.Text = "Annullamento dopo la foto in corso..."; };
            root.Children.Add(_text);
            root.Children.Add(_bar);
            root.Children.Add(cancel);
            Content = root;
            Closing += (s, e) => { if (!_done) { e.Cancel = true; Cancelled = true; } };
        }

        public bool Cancelled { get; private set; }

        public void Report(int done, string text)
        {
            _bar.Value = done;
            if (!Cancelled) _text.Text = text;
        }

        public void Finish()
        {
            _done = true;
            Close();
        }
    }
}
