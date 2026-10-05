using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>
    /// Adobe Camera Raw / Lightroom style develop window with live preview: Base, Curva, Colore, Dettagli,
    /// Ritaglio and Maschere tabs, "Automatico" and "Luce intelligente".
    /// </summary>
    public sealed partial class CameraRawDialog : Window
    {
        sealed class SliderDef
        {
            public string Label;
            public double Min, Max;
            public int Decimals;
            public Func<RawSettings, double> Get;
            public Action<RawSettings, double> Set;
            public double? Reset;
            public Action Changed;
            public Brush Track;
            public Slider Slider;
            public TextBox Box;
        }

        readonly RawImage _full, _preview;
        readonly RawSettings _defaults;
        RawSettings _s;
        readonly List<SliderDef> _defs = new List<SliderDef>();
        WriteableBitmap _bmp, _beforeBmp;
        readonly bool _allowGeometry;
        Image _image, _beforeImage;
        Canvas _overlay;
        ColumnDefinition _beforeColumn;
        Border _afterTag;
        ComboBox _view;
        ScrollViewer _scroll;
        readonly Dictionary<string, (Button Header, Panel Content)> _tabs = new Dictionary<string, (Button, Panel)>();
        string _tab = "Base";
        byte[] _lastPx;
        int _lastW, _lastH;
        int[] _lumHistogram;
        Slider _smartIntensity;
        readonly Polygon _hR = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xE0, 0x45, 0x3E)) };
        readonly Polygon _hG = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x40, 0xC0, 0x50)) };
        readonly Polygon _hB = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x4A, 0x7F, 0xE8)) };
        readonly CheckBox _showOriginal;
        readonly TextBlock _status;
        readonly Button _ok, _cancel;
        readonly DispatcherTimer _timer;
        int _ticket;
        bool _loading, _processing, _closed, _wbProgrammatic;
        ComboBox _wbCombo;

        // AI assistant (Gemini, Ollama or Claude): the AI proposes slider values, PhotoStudio renders the pixels.
        ComboBox _aiProvider;
        TextBox _aiInput;
        Button _aiButton, _aiUndo;
        TextBlock _aiText;
        CancellationTokenSource _aiCts, _okCts;
        readonly CancellationTokenSource _closeCts = new CancellationTokenSource();
        RawSettings _beforeAi;
        readonly DispatcherTimer _aiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        DateTime _aiStart;

        public byte[] Result { get; private set; }
        /// <summary>Size of Result: differs from the original when the photo is cropped.</summary>
        public int ResultWidth { get; private set; }
        public int ResultHeight { get; private set; }
        public RawSettings Settings => _s.Clone();

        ComboBox _presets;
        Button _presetDelete;
        bool _presetProgrammatic;

        // "Luce intelligente": re-applying (or changing the intensity) starts again from the values before it.
        RawSettings _beforeSmart;
        int _userEdits, _smartEdits = -1;
        readonly DispatcherTimer _smartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };

        /// <param name="initial">Starting slider values (e.g. the previous development, or pasted settings).</param>
        /// <param name="allowGeometry">False when developing a layer (filter): straighten and crop are not available.</param>
        /// <param name="startTab">Tab shown first: Base, Curva, Colore, Dettagli, Ritaglio or Maschere.</param>
        public CameraRawDialog(Window owner, RawImage raw, string title, string okText, bool autoOnOpen = false, bool focusAi = false,
                               RawSettings initial = null, bool allowGeometry = true, string startTab = null)
        {
            Owner = owner;
            Title = "Camera Raw — " + title;
            Width = 1400;
            Height = 900;
            MinWidth = 980;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            UseLayoutRounding = true;
            Background = Res("CanvasBg");
            Foreground = Res("TextBrush");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            DarkTitleBar.Apply(this);

            _full = raw;
            _preview = raw.Downscale(1600);
            _allowGeometry = allowGeometry;
            _defaults = RawSettings.Default(raw.SceneReferred);
            _s = initial?.Clone() ?? (autoOnOpen ? RawDevelop.AutoTone(_preview, _defaults) : _defaults.Clone());
            if (!allowGeometry) StripGeometry(_s);
            _bmp = new WriteableBitmap(_preview.Width, _preview.Height, 96, 96, PixelFormats.Bgra32, null);

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(372) });

            // ---- Preview side
            var left = new DockPanel();
            var info = new DockPanel { Background = Res("PanelHeaderBg"), Height = 34 };
            _showOriginal = new CheckBox { Content = T("Mostra originale"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            _showOriginal.Checked += (s, e) => Schedule();
            _showOriginal.Unchecked += (s, e) => Schedule();
            DockPanel.SetDock(_showOriginal, Dock.Right);
            info.Children.Add(_showOriginal);
            _view = new ComboBox
            {
                Width = 150, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false, SelectedIndex = 0,
                ItemsSource = new[] { T("Vista: Dopo"), T("Vista: Prima | Dopo") }, ToolTip = T("Confronta l'originale con il risultato, affiancati (Y)"),
            };
            _view.SelectionChanged += (s, e) => UpdateView();
            DockPanel.SetDock(_view, Dock.Right);
            info.Children.Add(_view);
            var detailParts = new List<string>();
            foreach (var part in new[] { raw.Camera, raw.Info, $"{raw.Width} × {raw.Height} px" })
                if (!string.IsNullOrWhiteSpace(part)) detailParts.Add(part);
            info.Children.Add(new TextBlock
            {
                Text = string.Join("   ·   ", detailParts), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
                Foreground = Res("TextDimBrush"), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            DockPanel.SetDock(info, Dock.Top);
            left.Children.Add(info);

            var viewGrid = new Grid { Margin = new Thickness(24) };
            _beforeColumn = new ColumnDefinition { Width = new GridLength(0) };
            viewGrid.ColumnDefinitions.Add(_beforeColumn);
            viewGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var beforeHost = new Grid { Margin = new Thickness(0, 0, 10, 0), ClipToBounds = true };
            _beforeImage = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(_beforeImage, BitmapScalingMode.HighQuality);
            beforeHost.Children.Add(_beforeImage);
            beforeHost.Children.Add(CornerTag(T("PRIMA")));
            viewGrid.Children.Add(beforeHost);

            var afterHost = new Grid();
            Grid.SetColumn(afterHost, 1);
            _image = new Image { Source = _bmp, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _image.MouseLeftButtonDown += (s, e) => { _showOriginal.IsChecked = true; _image.CaptureMouse(); };
            _image.MouseLeftButtonUp += (s, e) => { _showOriginal.IsChecked = false; _image.ReleaseMouseCapture(); };
            _image.ToolTip = T("Tieni premuto il mouse sull'immagine per vedere l'originale");
            _overlay = new Canvas();
            afterHost.Children.Add(_image);
            _afterTag = CornerTag(T("DOPO"));
            _afterTag.Visibility = Visibility.Collapsed;
            afterHost.Children.Add(_afterTag);
            afterHost.Children.Add(_overlay);
            afterHost.SizeChanged += (s, e) => UpdateOverlay();
            viewGrid.Children.Add(afterHost);
            left.Children.Add(viewGrid);
            root.Children.Add(left);

            // ---- Controls side
            var right = new DockPanel { Background = Res("PanelBg") };
            Grid.SetColumn(right, 1);

            var bottom = new StackPanel { Margin = new Thickness(14, 8, 14, 14) };
            _status = new TextBlock { Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, MaxHeight = 64 };
            bottom.Children.Add(_status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _cancel = new Button { Content = T("Annulla"), IsCancel = true, MinWidth = 96 };
            _ok = new Button { Content = okText, IsDefault = true, MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
            _ok.Click += Ok_Click;
            _cancel.Click += (s, e) => { if (_okCts != null) { _aiStopped = true; _okCts.Cancel(); } };   // while Ok works: stops it (OnClosing keeps the window open)
            buttons.Children.Add(_cancel);
            buttons.Children.Add(_ok);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            right.Children.Add(bottom);

            var histogram = new Border
            {
                Height = 96, Background = Res("InputBg"), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(1), Margin = new Thickness(14, 10, 14, 8),
                Child = new Viewbox { Stretch = Stretch.Fill, Child = new Canvas { Width = 256, Height = 100, Children = { _hR, _hG, _hB } } },
            };
            DockPanel.SetDock(histogram, Dock.Top);
            right.Children.Add(histogram);
            var tabStrip = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(12, 0, 12, 4) };
            DockPanel.SetDock(tabStrip, Dock.Top);
            right.Children.Add(tabStrip);
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            right.Children.Add(_scroll);

            var basePanel = TabPanel();
            BuildBaseTab(basePanel);
            AddTab(tabStrip, "Base", basePanel);
            AddTab(tabStrip, "Curva", BuildCurveTab());
            AddTab(tabStrip, "Colore", BuildColorTab());
            AddTab(tabStrip, "Dettagli", BuildDetailTab());
            if (allowGeometry) AddTab(tabStrip, "Ritaglio", BuildCropTab());
            AddTab(tabStrip, "Maschere", BuildMaskTab());
            root.Children.Add(right);
            Content = root;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            _timer.Tick += (s, e) => { _timer.Stop(); RenderPreview(); };
            _smartTimer.Tick += (s, e) => { _smartTimer.Stop(); RunSmartLight(); };
            SetSettings(_s);
            if (initial != null) _status.Text = T("Impostazioni applicate: {0}", _s.Describe());
            else if (autoOnOpen) _status.Text = T("Automatico: {0}", _s.Describe());
            Closing += OnClosing;
            PreviewKeyDown += OnKey;
            _aiTimer.Tick += (s, e) => UpdateAiBusy();
            ShowTab(startTab != null && _tabs.ContainsKey(startTab) ? startTab : "Base");
            if (focusAi) Loaded += (s, e) => { ShowTab("Base"); _aiInput.Focus(); };
        }

        static void StripGeometry(RawSettings s)
        {
            s.Angle = 0;
            s.CropL = s.CropT = 0;
            s.CropR = s.CropB = 1;
        }

        static Border CornerTag(string text) => new Border
        {
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(6), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.U && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (_wbCombo.SelectedIndex == 1) OnWhiteBalanceMode(); else _wbCombo.SelectedIndex = 1;
                }
                else RunAutoTone();
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                _view.SelectedIndex = _view.SelectedIndex == 0 ? 1 : 0;
            }
        }

        // ================= Tabs =================

        /// <summary>Button text that shrinks instead of being cut when a translation is longer than the button.</summary>
        static Viewbox FitText(string text) => new Viewbox { Child = new TextBlock { Text = text }, StretchDirection = StretchDirection.DownOnly };

        static StackPanel TabPanel() => new StackPanel { Margin = new Thickness(14, 6, 14, 10) };

        /// <param name="name">Italian tab name: the tab's identifier, translated only for display.</param>
        void AddTab(Panel strip, string name, Panel content)
        {
            var header = new Button { Content = FitText(T(name)), Padding = new Thickness(2, 5, 2, 5), Margin = new Thickness(1, 0, 1, 0), Focusable = false, FontSize = 11.5 };
            header.Click += (s, e) => ShowTab(name);
            strip.Children.Add(header);
            _tabs[name] = (header, content);
        }

        void ShowTab(string name)
        {
            bool cropChanged = (_tab == "Ritaglio") != (name == "Ritaglio");
            bool masksChanged = (_tab == "Maschere") != (name == "Maschere");   // the red mask preview appears / disappears
            _tab = name;
            foreach (var kv in _tabs)
            {
                var header = kv.Value.Header;
                if (kv.Key == name)
                {
                    header.Background = header.BorderBrush = Res("AccentBrush");
                    header.Foreground = Brushes.White;
                }
                else
                {
                    header.ClearValue(BackgroundProperty);
                    header.ClearValue(BorderBrushProperty);
                    header.ClearValue(ForegroundProperty);
                }
            }
            _scroll.Content = _tabs[name].Content;
            _scroll.ScrollToTop();
            if (name != "Maschere") _picking = false;
            if (cropChanged || masksChanged) Schedule();
            UpdateOverlay();
        }

        void UpdateView()
        {
            bool split = _view.SelectedIndex == 1;
            _beforeColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _afterTag.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            if (split && _beforeBmp == null) RenderBefore();
            Dispatcher.BeginInvoke(new Action(UpdateOverlay), DispatcherPriority.Loaded);
        }

        async void RenderBefore()
        {
            var s = _defaults.Clone();
            var r = await Task.Run(() => RawDevelop.Develop(_preview, s));
            if (_closed) return;
            _beforeBmp = new WriteableBitmap(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null);
            _beforeBmp.WritePixels(new Int32Rect(0, 0, r.Width, r.Height), r.Pixels, r.Width * 4, 0);
            _beforeImage.Source = _beforeBmp;
        }

        /// <summary>Where the photo is drawn inside the overlay canvas.</summary>
        Rect ImageRect()
        {
            if (_image.ActualWidth <= 0 || _image.ActualHeight <= 0 || !_image.IsLoaded) return Rect.Empty;
            var p = _image.TranslatePoint(new Point(0, 0), _overlay);
            return new Rect(p, new Size(_image.ActualWidth, _image.ActualHeight));
        }

        void UpdateOverlay()
        {
            if (_overlay == null) return;
            _overlay.Children.Clear();
            _overlay.Background = null;
            _overlay.Cursor = null;
            var rect = ImageRect();
            if (rect.IsEmpty || _showOriginal.IsChecked == true) return;
            if (_tab == "Ritaglio") DrawCropOverlay(rect);
            else if (_tab == "Maschere") DrawMaskOverlay(rect);
        }

        void Edited()
        {
            _userEdits++;
            Schedule();
        }

        // ================= Base tab =================

        void BuildBaseTab(Panel panel)
        {
            var autoRow = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            autoRow.ColumnDefinitions.Add(new ColumnDefinition());
            autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            autoRow.ColumnDefinitions.Add(new ColumnDefinition());
            var auto = new Button
            {
                Content = FitText(T("Automatico")), Padding = new Thickness(6, 6, 6, 6), Background = Res("AccentBrush"), BorderBrush = Res("AccentBrush"),
                ToolTip = T("Come in Camera Raw: regola Esposizione, Contrasto, Luci, Ombre, Bianchi, Neri, Vividezza e Saturazione (Ctrl+U).\nRispetta le foto scure per scelta (notturne, concerti) e pesa l'esposizione sui volti; agli alti ISO alza la riduzione del rumore.\nIl bilanciamento del bianco non viene toccato."),
            };
            auto.Click += (s, e) => RunAutoTone();
            var smart = new Button
            {
                Content = FitText(T("☀ Luce intelligente")), Padding = new Thickness(6, 6, 6, 6), FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x1E)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x1E)), Foreground = Brushes.White,
                ToolTip = T("Sistema la luce in un clic: esposizione e toni come Automatico, poi maschere di luce dove servono\n(schiarisce le zone scure, recupera luci e cielo, illumina i volti o il soggetto). Tutto resta modificabile nella scheda Maschere."),
            };
            smart.Click += (s, e) => RunSmartLight();
            var reset = new Button { Content = FitText(T("Predefinito")), Padding = new Thickness(6, 6, 6, 6), ToolTip = T("Riporta tutti i cursori ai valori predefiniti (anche curva, colore, ritaglio e maschere)") };
            reset.Click += (s, e) => { _beforeSmart = null; SetSettings(_defaults.Clone(), 0); _status.Text = ""; };
            Grid.SetColumn(smart, 2);
            Grid.SetColumn(reset, 4);
            autoRow.Children.Add(auto);
            autoRow.Children.Add(smart);
            autoRow.Children.Add(reset);
            panel.Children.Add(autoRow);

            var intensityRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var intensityValue = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("TextDimBrush") };
            DockPanel.SetDock(intensityValue, Dock.Right);
            intensityRow.Children.Add(intensityValue);
            intensityRow.Children.Add(new TextBlock { Text = T("Intensità luce"), Width = 92, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("TextDimBrush") });
            _smartIntensity = new Slider { Minimum = 0, Maximum = 150, Value = 100, SmallChange = 5, LargeChange = 25, ToolTip = T("Quanto deve intervenire la Luce intelligente (100% = normale)") };
            _smartIntensity.ValueChanged += (s, e) =>
            {
                intensityValue.Text = Math.Round(_smartIntensity.Value) + " %";
                if (_beforeSmart != null && _smartEdits == _userEdits) { _smartTimer.Stop(); _smartTimer.Start(); }
            };
            intensityValue.Text = "100 %";
            intensityRow.Children.Add(_smartIntensity);
            panel.Children.Add(intensityRow);

            BuildPresetRow(panel);
            BuildAiSection(panel);

            var wbBrush = new LinearGradientBrush(Color.FromRgb(0x3C, 0x6E, 0xD8), Color.FromRgb(0xE8, 0xC8, 0x30), 0);
            var tintBrush = new LinearGradientBrush(Color.FromRgb(0x3C, 0xB0, 0x4A), Color.FromRgb(0xC8, 0x3C, 0xC0), 0);
            Section(panel, T("BILANCIAMENTO DEL BIANCO"));
            _wbCombo = new ComboBox
            {
                ItemsSource = new[] { T("Come scattato"), T("Automatico"), T("Personalizzato") }, SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 6), ToolTip = T("Automatico: Ctrl+Maiusc+U"),
            };
            _wbCombo.SelectionChanged += (s, e) => OnWhiteBalanceMode();
            panel.Children.Add(_wbCombo);
            Row(panel, T("Temperatura"), -100, 100, 0, x => x.Temperature, (x, v) => x.Temperature = v, wbBrush, whiteBalance: true);
            Row(panel, T("Tinta"), -100, 100, 0, x => x.Tint, (x, v) => x.Tint = v, tintBrush, whiteBalance: true);
            Section(panel, T("TONO"));
            Row(panel, T("Esposizione"), -5, 5, 2, x => x.Exposure, (x, v) => x.Exposure = v);
            Row(panel, T("Contrasto"), -100, 100, 0, x => x.Contrast, (x, v) => x.Contrast = v);
            Row(panel, T("Luci"), -100, 100, 0, x => x.Highlights, (x, v) => x.Highlights = v);
            Row(panel, T("Ombre"), -100, 100, 0, x => x.Shadows, (x, v) => x.Shadows = v);
            Row(panel, T("Bianchi"), -100, 100, 0, x => x.Whites, (x, v) => x.Whites = v);
            Row(panel, T("Neri"), -100, 100, 0, x => x.Blacks, (x, v) => x.Blacks = v);
            Section(panel, T("PRESENZA"));
            Row(panel, T("Texture"), -100, 100, 0, x => x.Texture, (x, v) => x.Texture = v);
            Row(panel, T("Chiarezza"), -100, 100, 0, x => x.Clarity, (x, v) => x.Clarity = v);
            Row(panel, T("Foschia"), -100, 100, 0, x => x.Dehaze, (x, v) => x.Dehaze = v);
            Row(panel, T("Vividezza"), -100, 100, 0, x => x.Vibrance, (x, v) => x.Vibrance = v);
            Row(panel, T("Saturazione"), -100, 100, 0, x => x.Saturation, (x, v) => x.Saturation = v);
            panel.Children.Add(Hint(T("Texture: dettagli fini (positivo) o pelle più morbida (negativo). Foschia: positivo toglie la nebbia, negativo la aggiunge.\nDoppio clic sul nome di un cursore per azzerarlo. Y: confronto Prima / Dopo.")));
        }

        static TextBlock Hint(string text) => new TextBlock
        {
            Text = text, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11,
        };

        // ================= Detail and effects tab =================

        Panel BuildDetailTab()
        {
            var p = TabPanel();
            Section(p, T("NITIDEZZA"));
            Row(p, T("Nitidezza"), 0, 150, 0, x => x.Sharpening, (x, v) => x.Sharpening = v);
            Section(p, T("RIDUZIONE DEL RUMORE"));
            Row(p, T("Luminanza"), 0, 100, 0, x => x.NoiseLuma, (x, v) => x.NoiseLuma = v);
            Row(p, T("Colore"), 0, 100, 0, x => x.NoiseColor, (x, v) => x.NoiseColor = v);
            p.Children.Add(Hint(T("Luminanza leviga la grana delle foto ad alti ISO; Colore toglie le macchioline colorate. Controlla il risultato con lo zoom al 100% dopo l'apertura.")));
            BuildAiDetailSection(p);
            Section(p, T("VIGNETTATURA (DOPO IL RITAGLIO)"));
            Row(p, T("Fattore"), -100, 100, 0, x => x.VignetteAmount, (x, v) => x.VignetteAmount = v);
            Row(p, T("Punto medio"), 0, 100, 0, x => x.VignetteMidpoint, (x, v) => x.VignetteMidpoint = v, reset: 50);
            Row(p, T("Sfumatura"), 0, 100, 0, x => x.VignetteFeather, (x, v) => x.VignetteFeather = v, reset: 50);
            Section(p, T("GRANA"));
            Row(p, T("Fattore"), 0, 100, 0, x => x.GrainAmount, (x, v) => x.GrainAmount = v);
            Row(p, T("Dimensione"), 0, 100, 0, x => x.GrainSize, (x, v) => x.GrainSize = v, reset: 25);
            p.Children.Add(Hint(T("Vignettatura negativa scurisce gli angoli, positiva li schiarisce. La grana imita l'aspetto della pellicola.")));
            return p;
        }

        // ================= Luce intelligente =================

        async void RunSmartLight()
        {
            // Pressing it again (or moving the intensity) redoes it from the values before, instead of piling up.
            var start = _beforeSmart != null && _smartEdits == _userEdits ? _beforeSmart.Clone() : _s.Clone();
            _beforeSmart = start.Clone();
            double k = _smartIntensity.Value;
            _status.Text = T("Luce intelligente in corso...");
            RawSettings result;
            try { result = await Task.Run(() => SmartLight.Apply(_preview, start, k)); }
            catch (Exception ex) { _status.Text = T("Luce intelligente non riuscita: {0}", ex.Message); return; }
            if (_closed) return;
            SetSettings(result);
            _smartEdits = _userEdits;
            var masks = result.Masks.Where(SmartLight.IsAuto).Select(SmartLight.ZoneOf).ToList();
            _status.Text = T("Luce intelligente ({0:0} %): esposizione {1:+0.00;-0.00;0}, luci {2:+0;-0;0}, ombre {3:+0;-0;0}{4}", k, result.Exposure, result.Highlights, result.Shadows, (masks.Count > 0 ? T(". Maschere di luce: {0} (scheda Maschere per ritoccarle).", string.Join(", ", masks)) : "."));
        }

        // ================= Presets =================

        void BuildPresetRow(Panel panel)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _presets = new ComboBox { ToolTip = T("Applica un preset salvato: tutti i cursori prendono i valori del preset") };
            _presets.SelectionChanged += (s, e) =>
            {
                _presetDelete.IsEnabled = _presets.SelectedIndex > 0;
                if (_presetProgrammatic || _presets.SelectedItem is not RawPreset { Settings: not null } p) return;
                SetSettings(p.Settings);
                _status.Text = T("Preset \"{0}\": {1}", p.Name, p.Settings.Describe());
            };
            var save = new Button { Content = T("Salva..."), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Salva i valori attuali dei cursori come preset") };
            save.Click += (s, e) => SavePreset();
            _presetDelete = new Button { Content = T("Elimina"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false, ToolTip = T("Elimina il preset scelto") };
            _presetDelete.Click += (s, e) =>
            {
                if (_presets.SelectedItem is not RawPreset { Settings: not null } p) return;
                if (MessageBox.Show(this, T("Eliminare il preset \"{0}\"?", p.Name), T("Preset"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                try { RawPreset.Delete(p.Name); } catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Preset"), MessageBoxButton.OK, MessageBoxImage.Warning); }
                LoadPresets(null);
            };
            Grid.SetColumn(save, 1);
            Grid.SetColumn(_presetDelete, 2);
            row.Children.Add(_presets);
            row.Children.Add(save);
            row.Children.Add(_presetDelete);
            panel.Children.Add(row);
            LoadPresets(null);
        }

        void LoadPresets(string select)
        {
            _presetProgrammatic = true;
            var items = new List<RawPreset> { new RawPreset { Name = T("Preset: nessuno") } };   // placeholder, no settings
            items.AddRange(RawPreset.LoadAll());
            _presets.ItemsSource = items;
            _presets.DisplayMemberPath = nameof(RawPreset.Name);
            _presets.SelectedIndex = Math.Max(0, items.FindIndex(p => p.Settings != null && p.Name == select));
            _presetProgrammatic = false;
            _presetDelete.IsEnabled = _presets.SelectedIndex > 0;
        }

        void SavePreset()
        {
            string current = _presets.SelectedItem is RawPreset { Settings: not null } p ? p.Name : "";
            var dlg = new NameDialog(this, T("Salva preset"), T("Nome del preset (es. Ritratto caldo):"), current);
            if (dlg.ShowDialog() != true) return;
            try
            {
                RawPreset.Save(dlg.Value, _s);
                LoadPresets(dlg.Value);
                _status.Text = T("Preset \"{0}\" salvato.", dlg.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Impossibile salvare il preset:\n{0}", ex.Message), T("Preset"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ================= AI assistant =================

        static readonly AiProvider[] ProviderOrder = { AiProvider.Gemini, AiProvider.Ollama, AiProvider.Claude };

        void BuildAiSection(Panel panel)
        {
            var dim = Res("TextDimBrush");
            Section(panel, T("ASSISTENTE AI"));

            var providerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            providerRow.ColumnDefinitions.Add(new ColumnDefinition());
            providerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _aiProvider = new ComboBox
            {
                ItemsSource = new[] { "Gemini (Google)", T("Ollama (sul tuo PC)"), "Claude (Anthropic)" },
                SelectedIndex = Array.IndexOf(ProviderOrder, AiSettings.Load().Provider),
                ToolTip = T("Servizio AI che sceglie i valori dei cursori"),
            };
            _aiProvider.SelectionChanged += (s, e) =>
            {
                var settings = AiSettings.Load();
                settings.Provider = SelectedProvider;
                try { settings.Save(); } catch { }
                UpdateAiButton();
            };
            var gear = new Button
            {
                Content = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Impostazioni AI (chiavi e modelli)"),
            };
            gear.Click += (s, e) => OpenAiSettings(SelectedProvider);
            Grid.SetColumn(gear, 1);
            providerRow.Children.Add(_aiProvider);
            providerRow.Children.Add(gear);
            panel.Children.Add(providerRow);

            var host = new Grid();
            _aiInput = new TextBox
            {
                Height = 50, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(5, 4, 5, 4),
                ToolTip = T("Descrivi a parole il risultato che vuoi. L'AI sceglie i valori dei cursori, la foto viene elaborata da PhotoStudio."),
            };
            var placeholder = new TextBlock
            {
                Text = T("Cosa vuoi ottenere? Es.: più calda e luminosa, look cinematografico… (vuoto = la modifica migliore)"),
                Foreground = dim, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 5, 8, 0), IsHitTestVisible = false,
            };
            _aiInput.TextChanged += (s, e) =>
                placeholder.Visibility = _aiInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _aiInput.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;   // don't trigger the dialog's default button
                    if (_aiCts == null) AskAi();
                }
            };
            host.Children.Add(_aiInput);
            host.Children.Add(placeholder);
            panel.Children.Add(host);

            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _aiButton = new Button { Padding = new Thickness(8, 6, 8, 6) };
            _aiButton.Click += (s, e) =>
            {
                if (_aiCts != null) _aiCts.Cancel();
                else AskAi();
            };
            _aiUndo = new Button
            {
                Content = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                Padding = new Thickness(12, 6, 12, 6), IsEnabled = false,
                ToolTip = T("Ripristina i valori precedenti al suggerimento dell'AI"),
            };
            _aiUndo.Click += (s, e) =>
            {
                if (_beforeAi == null) return;
                SetSettings(_beforeAi);
                _beforeAi = null;
                _aiUndo.IsEnabled = false;
                _aiText.Foreground = Res("TextDimBrush");
                _aiText.Text = T("Valori precedenti ripristinati.");
            };
            Grid.SetColumn(_aiUndo, 2);
            row.Children.Add(_aiButton);
            row.Children.Add(_aiUndo);
            panel.Children.Add(row);

            _aiText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = dim,
                Text = T("L'AI riceve solo un'anteprima ridotta e risponde con i valori dei cursori: i pixel li elabora PhotoStudio."),
            };
            panel.Children.Add(_aiText);
            UpdateAiButton();
        }

        AiProvider SelectedProvider => ProviderOrder[Math.Max(0, _aiProvider.SelectedIndex)];

        void UpdateAiButton()
        {
            if (_aiCts == null) _aiButton.Content = T("✦  Chiedi a {0}", AiEditor.DisplayName(SelectedProvider));
        }

        bool OpenAiSettings(AiProvider provider)
        {
            bool ok = new AiSettingsDialog(this, provider).ShowDialog() == true;
            if (ok)
            {
                int index = Array.IndexOf(ProviderOrder, AiSettings.Load().Provider);
                if (index != _aiProvider.SelectedIndex) _aiProvider.SelectedIndex = index;
            }
            return ok;
        }

        string PhotoInfo()
        {
            var parts = new List<string>();
            foreach (var p in new[] { _full.Camera, _full.Info, $"{_full.Width} × {_full.Height} px" })
                if (!string.IsNullOrWhiteSpace(p)) parts.Add(p);
            return string.Join(", ", parts);
        }

        void UpdateAiBusy() =>
            _aiButton.Content = T("Annulla richiesta ({0} s)", (int)(DateTime.Now - _aiStart).TotalSeconds);

        async void AskAi()
        {
            var config = AiSettings.Load();
            config.Provider = SelectedProvider;
            if (AiEditor.MissingConfiguration(config) != null)
            {
                if (!OpenAiSettings(config.Provider)) return;
                config = AiSettings.Load();
                if (AiEditor.MissingConfiguration(config) != null) return;
            }
            string name = AiEditor.DisplayName(config.Provider);

            var settings = _s.Clone();
            string instruction = _aiInput.Text;
            _aiCts = new CancellationTokenSource();
            var token = _aiCts.Token;
            _aiStart = DateTime.Now;
            UpdateAiBusy();
            _aiTimer.Start();
            _aiText.Foreground = Res("TextDimBrush");
            _aiText.Text = config.Provider == AiProvider.Ollama
                ? T("Il modello {0} sta analizzando la foto sul tuo PC (può richiedere anche un minuto)...", config.OllamaModel)
                : T("{0} sta analizzando la foto...", name);
            try
            {
                var (jpeg, stats) = await Task.Run(() =>
                {
                    var small = _full.Downscale(1024);
                    var look = settings.Clone();
                    look.AiDenoise = look.AiRefocus = 0;   // the AI networks would take long, for a small preview
                    var r = RawDevelop.Develop(small, look);
                    return (ImageIO.EncodeJpeg(r.Width, r.Height, r.Pixels, 85), AiEditor.DescribeStatistics(r.Pixels));
                });
                var request = new AiRequest
                {
                    PreviewJpeg = jpeg, Current = settings, SceneReferred = _full.SceneReferred,
                    PhotoInfo = PhotoInfo(), Stats = stats, Instruction = instruction,
                };
                var suggestion = await AiEditor.SuggestAsync(config, request, token);
                if (_closed) return;
                _beforeAi = _s.Clone();
                SetSettings(suggestion.Settings);
                _aiUndo.IsEnabled = true;
                _aiText.Foreground = Res("TextBrush");
                _aiText.Text = name + ": " + (string.IsNullOrWhiteSpace(suggestion.Explanation) ? suggestion.Settings.Describe() : suggestion.Explanation) +
                               (string.IsNullOrWhiteSpace(suggestion.Note) ? "" : T("\n(Nota: {0})", suggestion.Note));
            }
            catch (OperationCanceledException)
            {
                if (!_closed) _aiText.Text = T("Richiesta annullata.");
            }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    _aiText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x86, 0x76));
                    _aiText.Text = ex.Message;
                }
            }
            finally
            {
                _aiTimer.Stop();
                _aiCts?.Dispose();
                _aiCts = null;
                if (!_closed) UpdateAiButton();
            }
        }

        static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        static void Section(Panel p, string title) =>
            p.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 12, 0, 6) });

        /// <param name="reset">Value restored by a double click on the label (default: the neutral value).</param>
        /// <param name="changed">Called after the user moves the slider.</param>
        /// <param name="whiteBalance">Moving it switches the white balance menu to "Personalizzato".</param>
        Grid Row(Panel p, string label, double min, double max, int decimals, Func<RawSettings, double> get, Action<RawSettings, double> set,
                 Brush track = null, double? reset = null, Action changed = null, bool whiteBalance = false, string tip = null)
        {
            var def = new SliderDef { Label = label, Min = min, Max = max, Decimals = decimals, Get = get, Set = set, Track = track, Reset = reset, Changed = changed };
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            if (tip != null) g.ToolTip = tip;

            var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, TextTrimming = TextTrimming.CharacterEllipsis };
            lbl.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) def.Slider.Value = def.Reset ?? def.Get(_defaults);
            };
            g.Children.Add(lbl);

            var sliderHost = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            if (track != null)
                sliderHost.Children.Add(new Rectangle { Height = 3, Fill = track, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 7, 6, 0) });
            def.Slider = new Slider { Minimum = min, Maximum = max, SmallChange = decimals > 0 ? 0.05 : 1, LargeChange = decimals > 0 ? 0.5 : 10 };
            sliderHost.Children.Add(def.Slider);
            Grid.SetColumn(sliderHost, 1);
            g.Children.Add(sliderHost);

            def.Box = new TextBox { Height = 22, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(def.Box, 2);
            g.Children.Add(def.Box);

            def.Slider.ValueChanged += (s, e) =>
            {
                double v = Math.Round(def.Slider.Value, decimals);
                def.Box.Text = Format(def, v);
                if (_loading) return;
                def.Set(_s, v);
                if (whiteBalance && _wbCombo.SelectedIndex != 2)
                {
                    _wbProgrammatic = true;
                    _wbCombo.SelectedIndex = 2;   // moved by hand: "Personalizzato"
                    _wbProgrammatic = false;
                }
                def.Changed?.Invoke();
                Edited();
            };
            void CommitBox()
            {
                if (DialogWindow.TryParse(def.Box.Text, out double v)) def.Slider.Value = Math.Clamp(v, min, max);
                def.Box.Text = Format(def, Math.Round(def.Slider.Value, decimals));
            }
            def.Box.LostKeyboardFocus += (s, e) => CommitBox();
            def.Box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitBox(); e.Handled = true; } };

            _defs.Add(def);
            p.Children.Add(g);
            return g;
        }

        /// <summary>Moves every slider to the current values (after loading settings or choosing another mask).</summary>
        void RefreshSliders()
        {
            _loading = true;
            foreach (var d in _defs)
            {
                double v = d.Get(_s);
                d.Slider.Value = v;
                d.Box.Text = Format(d, Math.Round(v, d.Decimals));
            }
            _loading = false;
        }

        static string Format(SliderDef d, double v) =>
            d.Decimals > 0 ? v.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                           : (d.Min < 0 ? v.ToString("+0;-0;0", CultureInfo.CurrentCulture) : v.ToString("0", CultureInfo.CurrentCulture));

        void SetSettings(RawSettings s, int? whiteBalanceMode = null)
        {
            _s = s.Clone();
            if (!_allowGeometry) StripGeometry(_s);
            RefreshSliders();
            RefreshCurve();
            RefreshColor();
            RefreshMasks();
            RefreshAiDetail();
            UpdateOverlay();
            if (_wbCombo != null)
            {
                _wbProgrammatic = true;
                _wbCombo.SelectedIndex = whiteBalanceMode
                    ?? (_s.Temperature == 0 && _s.Tint == 0 ? 0 : _wbCombo.SelectedIndex == 0 ? 2 : _wbCombo.SelectedIndex);
                _wbProgrammatic = false;
            }
            Schedule();
        }

        // ================= Automatic (Camera Raw style) =================

        async void RunAutoTone()
        {
            var current = _s.Clone();
            _status.Text = T("Automatico in corso...");
            var result = await Task.Run(() => RawDevelop.AutoTone(_preview, current));
            if (_closed) return;
            SetSettings(result);
            _status.Text = T("Automatico: esposizione {0:+0.00;-0.00;0}, contrasto {1:+0;-0;0}, luci {2:+0;-0;0}, ombre {3:+0;-0;0}, bianchi {4:+0;-0;0}, neri {5:+0;-0;0}, vividezza {6:+0;-0;0}, saturazione {7:+0;-0;0}", result.Exposure, result.Contrast, result.Highlights, result.Shadows, result.Whites, result.Blacks, result.Vibrance, result.Saturation)
                + (result.NoiseLuma > current.NoiseLuma || result.NoiseColor > current.NoiseColor
                    ? T(", riduzione rumore {0:0} / {1:0}", result.NoiseLuma, result.NoiseColor)
                    : "");
        }

        void OnWhiteBalanceMode()
        {
            if (_wbProgrammatic) return;
            var s = _s.Clone();
            switch (_wbCombo.SelectedIndex)
            {
                case 0:
                    s.Temperature = s.Tint = 0;
                    SetSettings(s, 0);
                    break;
                case 1:
                    (s.Temperature, s.Tint) = RawDevelop.AutoWhiteBalance(_preview);
                    SetSettings(s, 1);
                    _status.Text = T("Bilanciamento del bianco automatico: temperatura {0:+0;-0;0}, tinta {1:+0;-0;0}", s.Temperature, s.Tint);
                    break;
            }
        }

        void Schedule()
        {
            _timer.Stop();
            _timer.Start();
        }

        async void RenderPreview()
        {
            int my = ++_ticket;
            _aiStopped = false;
            bool original = _showOriginal.IsChecked == true;
            var s = original ? _defaults.Clone() : _s.Clone();
            // While choosing the crop the whole straightened photo is shown, with the crop frame on top.
            bool cropMode = _tab == "Ritaglio" && !original;
            int maskPreview = original ? -1 : MaskPreviewIndex;
            (int Width, int Height, byte[] Pixels) r;
            try
            {
                r = await Task.Run(() => cropMode
                    ? RawDevelop.ApplyGeometry(RawDevelop.Render(AiRestore.Apply(_preview, s), s), _preview.Width, _preview.Height, s, false)
                    : RawDevelop.Develop(_preview, s, maskPreview, _closeCts.Token));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _status.Text = T("Errore di anteprima: {0}", ex.Message); return; }
            if (my != _ticket || _closed) return;
            if (_bmp.PixelWidth != r.Width || _bmp.PixelHeight != r.Height)
            {
                _bmp = new WriteableBitmap(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null);
                _image.Source = _bmp;
            }
            _bmp.WritePixels(new Int32Rect(0, 0, r.Width, r.Height), r.Pixels, r.Width * 4, 0);
            _lastPx = r.Pixels;
            _lastW = r.Width;
            _lastH = r.Height;
            UpdateHistogram(r.Pixels);
            _ = Dispatcher.BeginInvoke(new Action(UpdateOverlay), DispatcherPriority.Loaded);
        }

        void UpdateHistogram(byte[] px)
        {
            var hr = new int[256]; var hg = new int[256]; var hb = new int[256]; var hl = new int[256];
            int step = Math.Max(1, px.Length / 4 / 200000) * 4;
            for (int i = 0; i < px.Length; i += step)
            {
                hb[px[i]]++; hg[px[i + 1]]++; hr[px[i + 2]]++;
                hl[(px[i + 2] * 54 + px[i + 1] * 183 + px[i] * 19) >> 8]++;
            }
            int max = 1;
            for (int i = 1; i < 255; i++) max = Math.Max(max, Math.Max(hr[i], Math.Max(hg[i], hb[i])));
            _hR.Points = Poly(hr, 96.0 / max);
            _hG.Points = Poly(hg, 96.0 / max);
            _hB.Points = Poly(hb, 96.0 / max);
            _lumHistogram = hl;
            UpdateCurveHistogram();
        }

        static PointCollection Poly(int[] h, double sc)
        {
            var pc = new PointCollection(260) { new Point(0, 100) };
            for (int i = 0; i < 256; i++) pc.Add(new Point(i, 100 - Math.Min(100, h[i] * sc)));
            pc.Add(new Point(255, 100));
            pc.Freeze();
            return pc;
        }

        async void Ok_Click(object sender, RoutedEventArgs e)
        {
            _processing = true;
            var s = _s.Clone();
            if (!_allowGeometry) StripGeometry(s);
            // The AI networks can take minutes at full size: meanwhile Annulla becomes "Interrompi".
            bool slow = AiRestore.Wanted(s) && !AiRestore.Ready(_full, s);
            _ok.IsEnabled = false;
            _cancel.IsEnabled = slow;
            if (slow) _cancel.Content = T("Interrompi");
            _okCts = new CancellationTokenSource();
            _aiStopped = false;
            var token = _okCts.Token;
            _status.Text = T("Elaborazione a piena risoluzione ({0} × {1})...", _full.Width, _full.Height);
            Cursor = Cursors.Wait;
            try
            {
                var r = await Task.Run(() =>
                {
                    try { return RawDevelop.Develop(_full, s, ct: token); }
                    catch (OutOfMemoryException)
                    {
                        // Often the memory is only held by buffers already freed: give it back to Windows and try once more.
                        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                        GC.WaitForPendingFinalizers();
                        return RawDevelop.Develop(_full, s, ct: token);
                    }
                });
                Result = r.Pixels;
                ResultWidth = r.Width;
                ResultHeight = r.Height;
            }
            catch (Exception ex)
            {
                _processing = false;
                _okCts = null;
                Cursor = null;
                _ok.IsEnabled = _cancel.IsEnabled = true;
                _cancel.Content = T("Annulla");
                if (ex is OperationCanceledException) { _status.Text = T("Elaborazione interrotta."); return; }
                _status.Text = "";
                string message = ex is OutOfMemoryException
                    ? T("Memoria insufficiente per sviluppare la foto a piena risoluzione.\nChiudi alcune schede o altri programmi e riprova.")
                    : T("Elaborazione non riuscita:\n{0}", ex.Message);
                MessageBox.Show(this, message, "Camera Raw", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _processing = false;
            _okCts = null;
            Cursor = null;
            DialogResult = true;
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (_processing) { e.Cancel = true; return; }
            _closed = true;
            _aiCts?.Cancel();
            _downloadCts?.Cancel();
            _closeCts.Cancel();
            _aiTimer.Stop();
        }
    }
}
