using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    // Maschere tab: local adjustments made simple. Ready-made "recipes", zones of light chosen with five
    // buttons (or by clicking on the photo), linear and radial gradients moved directly on the photo.
    public sealed partial class CameraRawDialog
    {
        enum MaskDrag { None, P1, P2, Center, RadiusX, RadiusY }

        static readonly (string Name, double Low, double High)[] Zones =
        {
            (T("Neri"), 0, 0.12), (T("Ombre"), 0.08, 0.35), (T("Mezzitoni"), 0.3, 0.7), (T("Luci"), 0.62, 0.9), (T("Bianchi"), 0.85, 1),
        };

        int _maskIndex = -1;
        ListBox _maskList;
        StackPanel _maskEditor, _zonePanel, _linearPanel, _radialPanel, _skyPanel;
        CheckBox _maskEnabled, _maskShow, _maskInvert, _skyInvert;
        TextBlock _maskTitle;
        RangeBar _rangeBar;
        readonly Button[] _zoneButtons = new Button[5];
        bool _picking, _maskFlash, _maskListLoading;
        readonly DispatcherTimer _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        MaskDrag _maskDrag;
        Point _maskStart;
        LocalMask _maskAtStart;

        LocalMask SelMask(RawSettings s) =>
            s?.Masks != null && _maskIndex >= 0 && _maskIndex < s.Masks.Count ? s.Masks[_maskIndex] : null;

        /// <summary>The mask to tint in red on the preview (-1 = none).</summary>
        int MaskPreviewIndex => _tab == "Maschere" && (_maskShow?.IsChecked == true || _maskFlash) && SelMask(_s) != null ? _maskIndex : -1;

        Panel BuildMaskTab()
        {
            var p = TabPanel();
            p.Children.Add(new TextBlock
            {
                Text = T("Ritocca solo una parte della foto. Scegli una ricetta pronta, oppure crea una maschera e usa i cursori qui sotto: cambiano solo la zona scelta."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });

            Section(p, T("RICETTE VELOCI (UN CLIC)"));
            var recipes = new UniformGrid { Columns = 2 };
            void Recipe(string name, string tip, Func<LocalMask> make)
            {
                var b = new Button { Content = name, Padding = new Thickness(4, 5, 4, 5), Margin = new Thickness(0, 0, 4, 4), ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Left };
                b.Click += (s, e) => AddMask(make(), T("Ricetta \"{0}\" applicata: la zona modificata lampeggia in rosso. Regola l'intensità con i cursori.", name.Substring(2)));
                recipes.Children.Add(b);
            }
            Recipe(T("☀ Schiarisci le ombre"), T("Più luce solo nelle zone scure, senza bruciare il resto"),
                () => new LocalMask { Name = T("Ombre più chiare"), Kind = MaskKind.Luminance, Low = 0, High = 0.3, Feather = 0.15, Exposure = 0.5, Shadows = 15, Clarity = 5 });
            Recipe(T("☁ Recupera le luci"), T("Riporta dettaglio e colore nelle zone troppo chiare"),
                () => new LocalMask { Name = T("Luci recuperate"), Kind = MaskKind.Luminance, Low = 0.7, High = 1, Feather = 0.12, Exposure = -0.45, Highlights = -25, Saturation = 8 });
            Recipe(T("◐ Ombre più profonde"), T("Neri più decisi: più profondità e contrasto"),
                () => new LocalMask { Name = T("Ombre profonde"), Kind = MaskKind.Luminance, Low = 0, High = 0.22, Feather = 0.12, Exposure = -0.3, Contrast = 15 });
            Recipe(T("🌅 Luci più calde"), T("Luci dorate, come al tramonto"),
                () => new LocalMask { Name = T("Luci calde"), Kind = MaskKind.Luminance, Low = 0.55, High = 1, Feather = 0.2, Temperature = 30, Tint = 5, Saturation = 15 });
            Recipe(T("⛅ Cielo più intenso"), T("Cielo più scuro e colorato: segue il profilo del cielo riconosciuto nella foto, altrimenti è una sfumatura dall'alto"),
                () => SceneAnalysis.Of(_preview).Sky != null
                    ? new LocalMask { Name = T("Cielo"), Kind = MaskKind.Sky, Exposure = -0.4, Saturation = 20, Contrast = 10, Temperature = -5 }
                    : new LocalMask { Name = T("Cielo"), Kind = MaskKind.Linear, X1 = (_s.CropL + _s.CropR) / 2, Y1 = _s.CropT, X2 = (_s.CropL + _s.CropR) / 2, Y2 = _s.CropT + (_s.CropB - _s.CropT) * 0.45, Exposure = -0.4, Saturation = 20, Contrast = 10, Temperature = -5 });
            Recipe(T("◎ Luce sul soggetto"), T("Un alone di luce al centro che attira lo sguardo"),
                () => new LocalMask { Name = T("Soggetto"), Kind = MaskKind.Radial, X1 = (_s.CropL + _s.CropR) / 2, Y1 = (_s.CropT + _s.CropB) / 2, RX = 0.28 * (_s.CropR - _s.CropL), RY = 0.34 * (_s.CropB - _s.CropT), Feather = 0.7, Exposure = 0.35, Clarity = 8 });
            Recipe(T("❄ Ombre più fredde"), T("Ombre bluastre: look cinematografico"),
                () => new LocalMask { Name = T("Ombre fredde"), Kind = MaskKind.Luminance, Low = 0, High = 0.35, Feather = 0.15, Temperature = -25, Saturation = 5 });
            Recipe(T("▣ Bordi più scuri"), T("Scurisce i bordi e lascia luminoso il centro"),
                () => new LocalMask { Name = T("Bordi"), Kind = MaskKind.Radial, X1 = (_s.CropL + _s.CropR) / 2, Y1 = (_s.CropT + _s.CropB) / 2, RX = 0.42 * (_s.CropR - _s.CropL), RY = 0.45 * (_s.CropB - _s.CropT), Feather = 0.8, Invert = true, Exposure = -0.45 });
            p.Children.Add(recipes);

            Section(p, T("CREA UNA MASCHERA"));
            var add = new UniformGrid { Rows = 1 };
            void Add(string name, string tip, MaskKind kind)
            {
                var b = new Button { Content = name, Padding = new Thickness(4, 5, 4, 5), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
                b.Click += (s, e) => AddMask(NewMask(kind), kind switch
                {
                    MaskKind.Luminance => T("Zona di luce creata: scegli qui sotto quale luce ritoccare (o clicca sulla foto con il contagocce)."),
                    MaskKind.Linear => T("Sfumatura lineare creata: trascina sulla foto il punto pieno (effetto massimo) e quello vuoto (fine della sfumatura)."),
                    _ => T("Sfumatura radiale creata: trascinala sulla foto e allarga o stringi il cerchio con le maniglie."),
                });
                add.Children.Add(b);
            }
            Add(T("+ Zona di luce"), T("Seleziona le zone per luminosità: solo le ombre, solo le luci..."), MaskKind.Luminance);
            Add(T("+ Lineare"), T("Sfumatura a partire da un lato: ideale per il cielo o il terreno"), MaskKind.Linear);
            Add(T("+ Radiale"), T("Cerchio o ellisse sfumati: un viso, un soggetto, un punto di luce"), MaskKind.Radial);
            p.Children.Add(add);

            Section(p, T("LE TUE MASCHERE"));
            _maskList = new ListBox { Height = 104, Background = Res("InputBg") };
            _maskList.SelectionChanged += (s, e) =>
            {
                if (_maskListLoading) return;
                _maskIndex = _maskList.SelectedIndex;
                _picking = false;
                RefreshMaskEditor();
                Schedule();
            };
            p.Children.Add(_maskList);
            var listButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var dup = new Button { Content = T("Duplica"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            dup.Click += (s, e) =>
            {
                var m = SelMask(_s);
                if (m == null) return;
                var c = m.Clone();
                c.Name = T("{0} (copia)", m.Name);
                c.Auto = false;   // a copy is the user's own: Luce intelligente must not replace it
                AddMask(c, null);
            };
            var rename = new Button { Content = T("Rinomina"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            rename.Click += (s, e) =>
            {
                var m = SelMask(_s);
                if (m == null) return;
                var dlg = new NameDialog(this, T("Rinomina maschera"), T("Nome della maschera:"), m.Name);
                if (dlg.ShowDialog() != true) return;
                m.Name = dlg.Value;
                RefreshMasks();
            };
            var del = new Button { Content = T("Elimina"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            del.Click += (s, e) =>
            {
                if (SelMask(_s) == null) return;
                _s.Masks.RemoveAt(_maskIndex);
                _maskIndex = Math.Min(_maskIndex, _s.Masks.Count - 1);
                RefreshMasks();
                Edited();
            };
            var clear = new Button { Content = T("Elimina tutte"), Padding = new Thickness(8, 2, 8, 2) };
            clear.Click += (s, e) =>
            {
                if (_s.Masks.Count == 0) return;
                if (MessageBox.Show(this, T("Eliminare tutte le maschere?"), T("Maschere"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _s.Masks.Clear();
                _maskIndex = -1;
                RefreshMasks();
                Edited();
            };
            listButtons.Children.Add(dup);
            listButtons.Children.Add(rename);
            listButtons.Children.Add(del);
            listButtons.Children.Add(clear);
            p.Children.Add(listButtons);

            // ---- editor of the selected mask
            _maskEditor = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var head = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
            _maskEnabled = new CheckBox { Content = T("Attiva"), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Spegni per vedere la foto senza questa maschera") };
            _maskEnabled.Click += (s, e) =>
            {
                var m = SelMask(_s);
                if (m == null) return;
                m.Enabled = _maskEnabled.IsChecked == true;
                RefreshMaskList();
                Edited();
            };
            DockPanel.SetDock(_maskEnabled, Dock.Right);
            head.Children.Add(_maskEnabled);
            _maskTitle = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            head.Children.Add(_maskTitle);
            _maskEditor.Children.Add(head);

            _zonePanel = new StackPanel();
            _zonePanel.Children.Add(new TextBlock { Text = T("Quale luce vuoi ritoccare?"), Margin = new Thickness(0, 0, 0, 4) });
            var zones = new UniformGrid { Rows = 1 };
            for (int i = 0; i < Zones.Length; i++)
            {
                int k = i;
                _zoneButtons[k] = new Button { Content = Zones[k].Name, Padding = new Thickness(1, 4, 1, 4), Margin = new Thickness(0, 0, 3, 0), Focusable = false, FontSize = 11 };
                _zoneButtons[k].Click += (s, e) =>
                {
                    var m = SelMask(_s);
                    if (m == null) return;
                    (m.Low, m.High, m.Feather) = (Zones[k].Low, Zones[k].High, 0.12);
                    AfterZoneChange(T("Zona \"{0}\": in rosso le parti della foto che verranno ritoccate.", Zones[k].Name));
                };
                zones.Children.Add(_zoneButtons[k]);
            }
            _zonePanel.Children.Add(zones);
            _rangeBar = new RangeBar();
            _zonePanel.Children.Add(_rangeBar);
            var pick = new Button { Content = T("🖉  Scegli il tono cliccando sulla foto"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 4) };
            pick.Click += (s, e) =>
            {
                _picking = !_picking;
                _status.Text = _picking ? T("Clicca sulla foto sul punto da ritoccare: verranno scelti tutti i toni simili.") : "";
                UpdateOverlay();
            };
            _zonePanel.Children.Add(pick);
            var fine = new Expander { Header = T("Regolazione fine della zona"), Foreground = Res("TextBrush"), Margin = new Thickness(0, 2, 0, 0) };
            var fineBody = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            Row(fineBody, T("Da"), 0, 100, 0, x => (SelMask(x)?.Low ?? 0) * 100, (x, v) => { var m = SelMask(x); if (m != null) m.Low = Math.Min(v / 100, m.High - 0.01); },
                changed: UpdateZoneUi, tip: T("Tono più scuro incluso (0 = nero)"));
            Row(fineBody, T("A"), 0, 100, 0, x => (SelMask(x)?.High ?? 1) * 100, (x, v) => { var m = SelMask(x); if (m != null) m.High = Math.Max(v / 100, m.Low + 0.01); },
                reset: 100, changed: UpdateZoneUi, tip: T("Tono più chiaro incluso (100 = bianco)"));
            Row(fineBody, T("Morbidezza"), 1, 50, 0, x => (SelMask(x)?.Feather ?? 0.15) * 100, (x, v) => { var m = SelMask(x); if (m != null) m.Feather = v / 100; },
                reset: 12, changed: UpdateZoneUi, tip: T("Quanto è graduale il passaggio ai toni esclusi"));
            fine.Content = fineBody;
            _zonePanel.Children.Add(fine);
            _maskEditor.Children.Add(_zonePanel);

            _linearPanel = new StackPanel();
            _linearPanel.Children.Add(Hint(T("Sulla foto: ● = effetto pieno, ○ = fine della sfumatura. Trascina i due punti; il quadratino al centro sposta tutta la sfumatura.")));
            _maskEditor.Children.Add(_linearPanel);

            _radialPanel = new StackPanel();
            _radialPanel.Children.Add(Hint(T("Sulla foto: trascina il cerchio per spostarlo, le maniglie bianche per allargarlo o stringerlo.")));
            Row(_radialPanel, T("Sfumatura"), 0, 100, 0, x => (SelMask(x)?.Feather ?? 0.5) * 100, (x, v) => { var m = SelMask(x); if (m != null) m.Feather = v / 100; },
                reset: 50, changed: UpdateOverlay, tip: T("0 = bordo netto, 100 = passaggio molto morbido"));
            _maskInvert = new CheckBox { Content = T("Modifica fuori dal cerchio (inverti)"), Margin = new Thickness(0, 4, 0, 0) };
            _maskInvert.Click += (s, e) =>
            {
                var m = SelMask(_s);
                if (m == null) return;
                m.Invert = _maskInvert.IsChecked == true;
                Edited();
            };
            _radialPanel.Children.Add(_maskInvert);
            _maskEditor.Children.Add(_radialPanel);

            _skyPanel = new StackPanel();
            _skyPanel.Children.Add(Hint(T("Il cielo è riconosciuto nella foto e la maschera ne segue il profilo (tetti, montagne, rami): non c'è niente da spostare. Attiva \"Mostra in rosso la zona modificata\" per vedere dove agisce.")));
            _skyInvert = new CheckBox { Content = T("Modifica tutto tranne il cielo (inverti)"), Margin = new Thickness(0, 4, 0, 0) };
            _skyInvert.Click += (s, e) =>
            {
                var m = SelMask(_s);
                if (m == null) return;
                m.Invert = _skyInvert.IsChecked == true;
                Edited();
            };
            _skyPanel.Children.Add(_skyInvert);
            _maskEditor.Children.Add(_skyPanel);

            _maskShow = new CheckBox { Content = T("Mostra in rosso la zona modificata"), Margin = new Thickness(0, 8, 0, 0) };
            _maskShow.Click += (s, e) => Schedule();
            _maskEditor.Children.Add(_maskShow);

            Section(_maskEditor, T("COSA CAMBIARE IN QUESTA ZONA"));
            LocalMask M(RawSettings x) => SelMask(x);
            Row(_maskEditor, T("Intensità"), 0, 100, 0, x => M(x)?.Amount ?? 100, (x, v) => { if (M(x) != null) M(x).Amount = v; }, reset: 100, tip: T("Quanto è forte tutta la maschera"));
            Row(_maskEditor, T("Luce"), -4, 4, 2, x => M(x)?.Exposure ?? 0, (x, v) => { if (M(x) != null) M(x).Exposure = v; }, tip: T("Più chiaro o più scuro (esposizione)"));
            Row(_maskEditor, T("Contrasto"), -100, 100, 0, x => M(x)?.Contrast ?? 0, (x, v) => { if (M(x) != null) M(x).Contrast = v; });
            Row(_maskEditor, T("Luci"), -100, 100, 0, x => M(x)?.Highlights ?? 0, (x, v) => { if (M(x) != null) M(x).Highlights = v; }, tip: T("Solo le parti chiare della zona"));
            Row(_maskEditor, T("Ombre"), -100, 100, 0, x => M(x)?.Shadows ?? 0, (x, v) => { if (M(x) != null) M(x).Shadows = v; }, tip: T("Solo le parti scure della zona"));
            Row(_maskEditor, T("Calore"), -100, 100, 0, x => M(x)?.Temperature ?? 0, (x, v) => { if (M(x) != null) M(x).Temperature = v; },
                new LinearGradientBrush(Color.FromRgb(0x3C, 0x6E, 0xD8), Color.FromRgb(0xE8, 0xC8, 0x30), 0), tip: T("Più freddo (blu) o più caldo (giallo)"));
            Row(_maskEditor, T("Verde / magenta"), -100, 100, 0, x => M(x)?.Tint ?? 0, (x, v) => { if (M(x) != null) M(x).Tint = v; },
                new LinearGradientBrush(Color.FromRgb(0x3C, 0xB0, 0x4A), Color.FromRgb(0xC8, 0x3C, 0xC0), 0));
            Row(_maskEditor, T("Colore"), -100, 100, 0, x => M(x)?.Saturation ?? 0, (x, v) => { if (M(x) != null) M(x).Saturation = v; }, tip: T("Colori più vivi o più spenti"));
            Row(_maskEditor, T("Dettaglio"), -100, 100, 0, x => M(x)?.Clarity ?? 0, (x, v) => { if (M(x) != null) M(x).Clarity = v; }, tip: T("Più incisivo (chiarezza) o più morbido"));
            p.Children.Add(_maskEditor);
            p.Children.Add(Hint(T("Le maschere si salvano con la foto: puoi copiarle su altre foto (Alt+Maiusc+C / V) o metterle in un preset.")));

            _flashTimer.Tick += (s, e) => { _flashTimer.Stop(); _maskFlash = false; Schedule(); };
            _overlay.MouseLeftButtonDown += Mask_Down;
            _overlay.MouseMove += Mask_Move;
            _overlay.MouseLeftButtonUp += Mask_Up;
            return p;
        }

        LocalMask NewMask(MaskKind kind)
        {
            int n = _s.Masks.Count(m => m.Kind == kind) + 1;
            double cx = (_s.CropL + _s.CropR) / 2, cy = (_s.CropT + _s.CropB) / 2, cw = _s.CropR - _s.CropL, ch = _s.CropB - _s.CropT;
            return kind switch
            {
                MaskKind.Luminance => new LocalMask { Name = T("Zona di luce {0}", n), Kind = kind, Low = 0.08, High = 0.35, Feather = 0.12 },
                MaskKind.Linear => new LocalMask { Name = T("Lineare {0}", n), Kind = kind, X1 = cx, Y1 = _s.CropT, X2 = cx, Y2 = _s.CropT + ch * 0.5 },
                _ => new LocalMask { Name = T("Radiale {0}", n), Kind = kind, X1 = cx, Y1 = cy, RX = 0.25 * cw, RY = 0.3 * ch, Feather = 0.5 },
            };
        }

        void AddMask(LocalMask m, string message)
        {
            _s.Masks.Add(m);
            _maskIndex = _s.Masks.Count - 1;
            RefreshMasks();
            Flash();
            if (message != null) _status.Text = message;
            Edited();
        }

        /// <summary>Shows the area of the selected mask in red for a moment.</summary>
        void Flash()
        {
            _maskFlash = true;
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        void AfterZoneChange(string message)
        {
            RefreshSliders();
            UpdateZoneUi();
            Flash();
            if (message != null) _status.Text = message;
            Edited();
        }

        void RefreshMasks()
        {
            if (_maskList == null) return;
            if (_maskIndex >= _s.Masks.Count) _maskIndex = _s.Masks.Count - 1;
            RefreshMaskList();
            RefreshMaskEditor();
        }

        void RefreshMaskList()
        {
            _maskListLoading = true;
            _maskList.ItemsSource = _s.Masks.Select(m =>
                $"{(m.Enabled ? "●" : "○")}  {m.Name}   —   {m.Kind switch { MaskKind.Luminance => T("zona di luce"), MaskKind.Linear => T("lineare"), MaskKind.Sky => T("cielo"), _ => T("radiale") }}").ToList();
            _maskList.SelectedIndex = _maskIndex;
            _maskListLoading = false;
        }

        void RefreshMaskEditor()
        {
            var m = SelMask(_s);
            _maskEditor.Visibility = m == null ? Visibility.Collapsed : Visibility.Visible;
            if (m == null) { UpdateOverlay(); return; }
            _maskTitle.Text = m.Name;
            _maskEnabled.IsChecked = m.Enabled;
            _maskInvert.IsChecked = _skyInvert.IsChecked = m.Invert;
            _zonePanel.Visibility = m.Kind == MaskKind.Luminance ? Visibility.Visible : Visibility.Collapsed;
            _linearPanel.Visibility = m.Kind == MaskKind.Linear ? Visibility.Visible : Visibility.Collapsed;
            _radialPanel.Visibility = m.Kind == MaskKind.Radial ? Visibility.Visible : Visibility.Collapsed;
            _skyPanel.Visibility = m.Kind == MaskKind.Sky ? Visibility.Visible : Visibility.Collapsed;
            RefreshSliders();
            UpdateZoneUi();
            UpdateOverlay();
        }

        void UpdateZoneUi()
        {
            var m = SelMask(_s);
            if (m == null || _rangeBar == null) return;
            _rangeBar.Set(m.Low, m.High, m.Feather);
            for (int i = 0; i < Zones.Length; i++)
            {
                bool on = m.Kind == MaskKind.Luminance && Math.Abs(m.Low - Zones[i].Low) < 0.005 && Math.Abs(m.High - Zones[i].High) < 0.005;
                if (on) { _zoneButtons[i].Background = _zoneButtons[i].BorderBrush = Res("AccentBrush"); _zoneButtons[i].Foreground = Brushes.White; }
                else { _zoneButtons[i].ClearValue(BackgroundProperty); _zoneButtons[i].ClearValue(BorderBrushProperty); _zoneButtons[i].ClearValue(ForegroundProperty); }
            }
        }

        // ================= Handles on the photo =================

        Point ToCanvas(Rect r, double sx, double sy) =>
            new Point(r.X + (sx - _s.CropL) / Math.Max(1e-4, _s.CropR - _s.CropL) * r.Width, r.Y + (sy - _s.CropT) / Math.Max(1e-4, _s.CropB - _s.CropT) * r.Height);

        void DrawMaskOverlay(Rect r)
        {
            if (_picking)
            {
                _overlay.Background = Brushes.Transparent;
                _overlay.Cursor = Cursors.Cross;
                return;
            }
            var m = SelMask(_s);
            if (m == null || m.Kind is MaskKind.Luminance or MaskKind.Sky) return;   // nothing to drag
            var white = Brushes.White;
            var shadow = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
            void Handle(Point c, bool filled, bool square = false)
            {
                Shape s = square ? new Rectangle() : new Ellipse();
                s.Width = s.Height = 12;
                s.Fill = filled ? white : shadow;
                s.Stroke = filled ? Brushes.Black : white;
                s.StrokeThickness = 2;
                s.Cursor = Cursors.SizeAll;
                Canvas.SetLeft(s, c.X - 6); Canvas.SetTop(s, c.Y - 6);
                _overlay.Children.Add(s);
            }
            if (m.Kind == MaskKind.Linear)
            {
                var a = ToCanvas(r, m.X1, m.Y1);
                var b = ToCanvas(r, m.X2, m.Y2);
                var d = b - a;
                double len = Math.Max(1, d.Length);
                var n = new Vector(-d.Y / len, d.X / len) * (r.Width + r.Height);
                _overlay.Children.Add(new Line { X1 = a.X - n.X, Y1 = a.Y - n.Y, X2 = a.X + n.X, Y2 = a.Y + n.Y, Stroke = white, StrokeThickness = 1.5, IsHitTestVisible = false });
                _overlay.Children.Add(new Line { X1 = b.X - n.X, Y1 = b.Y - n.Y, X2 = b.X + n.X, Y2 = b.Y + n.Y, Stroke = white, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false });
                _overlay.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = white, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 }, IsHitTestVisible = false });
                Handle(a, true);
                Handle(b, false);
                Handle(a + d / 2, true, true);
            }
            else
            {
                var c = ToCanvas(r, m.X1, m.Y1);
                double rx = m.RX * r.Width / Math.Max(1e-4, _s.CropR - _s.CropL), ry = m.RY * r.Height / Math.Max(1e-4, _s.CropB - _s.CropT);
                var outer = new Ellipse { Width = 2 * rx, Height = 2 * ry, Stroke = white, StrokeThickness = 1.5, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll };
                Canvas.SetLeft(outer, c.X - rx); Canvas.SetTop(outer, c.Y - ry);
                _overlay.Children.Add(outer);
                double inner = 1 - Math.Clamp(m.Feather, 0, 1);
                if (inner > 0.02)
                {
                    var ie = new Ellipse { Width = 2 * rx * inner, Height = 2 * ry * inner, Stroke = white, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
                    Canvas.SetLeft(ie, c.X - rx * inner); Canvas.SetTop(ie, c.Y - ry * inner);
                    _overlay.Children.Add(ie);
                }
                Handle(c, true, true);
                Handle(new Point(c.X + rx, c.Y), true);
                Handle(new Point(c.X, c.Y + ry), true);
            }
        }

        MaskDrag HitMask(Point p, Rect r, LocalMask m)
        {
            bool Near(Point a) => (a - p).Length < 11;
            if (m.Kind == MaskKind.Linear)
            {
                var a = ToCanvas(r, m.X1, m.Y1);
                var b = ToCanvas(r, m.X2, m.Y2);
                if (Near(a)) return MaskDrag.P1;
                if (Near(b)) return MaskDrag.P2;
                if (Near(a + (b - a) / 2)) return MaskDrag.Center;
                return MaskDrag.None;
            }
            if (m.Kind == MaskKind.Radial)
            {
                var c = ToCanvas(r, m.X1, m.Y1);
                double rx = m.RX * r.Width / Math.Max(1e-4, _s.CropR - _s.CropL), ry = m.RY * r.Height / Math.Max(1e-4, _s.CropB - _s.CropT);
                if (Near(new Point(c.X + rx, c.Y))) return MaskDrag.RadiusX;
                if (Near(new Point(c.X, c.Y + ry))) return MaskDrag.RadiusY;
                double ex = (p.X - c.X) / Math.Max(1, rx), ey = (p.Y - c.Y) / Math.Max(1, ry);
                if (ex * ex + ey * ey <= 1) return MaskDrag.Center;
            }
            return MaskDrag.None;
        }

        void Mask_Down(object sender, MouseButtonEventArgs e)
        {
            if (_tab != "Maschere") return;
            var r = ImageRect();
            if (r.IsEmpty) return;
            var p = e.GetPosition(_overlay);
            if (_picking)
            {
                PickTone(p, r);
                e.Handled = true;
                return;
            }
            var m = SelMask(_s);
            if (m == null) return;
            _maskDrag = HitMask(p, r, m);
            if (_maskDrag == MaskDrag.None) return;
            _maskStart = p;
            _maskAtStart = m.Clone();
            _overlay.CaptureMouse();
            e.Handled = true;
        }

        void Mask_Move(object sender, MouseEventArgs e)
        {
            if (_tab != "Maschere" || _maskDrag == MaskDrag.None) return;
            var m = SelMask(_s);
            var r = ImageRect();
            if (m == null || r.IsEmpty) return;
            var p = e.GetPosition(_overlay);
            double cw = _s.CropR - _s.CropL, ch = _s.CropB - _s.CropT;
            double du = (p.X - _maskStart.X) / r.Width * cw, dv = (p.Y - _maskStart.Y) / r.Height * ch;
            var o = _maskAtStart;
            switch (_maskDrag)
            {
                case MaskDrag.P1: m.X1 = o.X1 + du; m.Y1 = o.Y1 + dv; break;
                case MaskDrag.P2: m.X2 = o.X2 + du; m.Y2 = o.Y2 + dv; break;
                case MaskDrag.Center:
                    m.X1 = o.X1 + du; m.Y1 = o.Y1 + dv;
                    m.X2 = o.X2 + du; m.Y2 = o.Y2 + dv;
                    break;
                case MaskDrag.RadiusX:
                    var c = ToCanvas(r, m.X1, m.Y1);
                    m.RX = Math.Max(0.01, Math.Abs(p.X - c.X) / r.Width * cw);
                    break;
                case MaskDrag.RadiusY:
                    var c2 = ToCanvas(r, m.X1, m.Y1);
                    m.RY = Math.Max(0.01, Math.Abs(p.Y - c2.Y) / r.Height * ch);
                    break;
            }
            UpdateOverlay();
            Schedule();
        }

        void Mask_Up(object sender, MouseButtonEventArgs e)
        {
            if (_maskDrag == MaskDrag.None) return;
            _maskDrag = MaskDrag.None;
            _overlay.ReleaseMouseCapture();
            Edited();
        }

        /// <summary>Contagocce: selects the tones similar to the clicked point.</summary>
        void PickTone(Point p, Rect r)
        {
            _picking = false;
            var m = SelMask(_s);
            if (m == null || _lastPx == null) { UpdateOverlay(); return; }
            int x = (int)((p.X - r.X) / r.Width * _lastW), y = (int)((p.Y - r.Y) / r.Height * _lastH);
            if (x < 0 || y < 0 || x >= _lastW || y >= _lastH) { UpdateOverlay(); return; }
            // Average a small area, so a single noisy pixel does not decide.
            double sum = 0;
            int n = 0;
            for (int yy = Math.Max(0, y - 2); yy <= Math.Min(_lastH - 1, y + 2); yy++)
                for (int xx = Math.Max(0, x - 2); xx <= Math.Min(_lastW - 1, x + 2); xx++)
                {
                    int i = (yy * _lastW + xx) * 4;
                    sum += (0.2126 * _lastPx[i + 2] + 0.7152 * _lastPx[i + 1] + 0.0722 * _lastPx[i]) / 255;
                    n++;
                }
            double L = sum / Math.Max(1, n);
            m.Low = L < 0.15 ? 0 : Math.Max(0, L - 0.12);
            m.High = L > 0.85 ? 1 : Math.Min(1, L + 0.12);
            m.Feather = 0.12;
            AfterZoneChange(T("Scelti i toni simili al punto cliccato (luminosità {0:0}%). In rosso le zone che verranno ritoccate.", L * 100));
        }
    }
}
