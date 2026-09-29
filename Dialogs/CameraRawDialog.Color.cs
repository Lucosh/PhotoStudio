using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PhotoStudio.Core;

namespace PhotoStudio.Dialogs
{
    // Curva and Colore tabs: tone curve, colour mixer (HSL) and colour grading.
    public sealed partial class CameraRawDialog
    {
        static readonly double[] MixCenters = { 0, 30, 60, 120, 180, 240, 270, 300 };

        CurveEditor _curve;
        ComboBox _curveChannel;
        readonly Panel[] _mixPanels = new Panel[3];
        readonly Button[] _mixButtons = new Button[3];
        ColorWheel _wheelS, _wheelM, _wheelH;
        Slider _lumS, _lumM, _lumH;
        bool _colorLoading;

        // ================= Curva =================

        Panel BuildCurveTab()
        {
            var p = TabPanel();
            Section(p, "CURVA DI TONO");
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = "Canale:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _curveChannel = new ComboBox { Width = 120, SelectedIndex = 0, ItemsSource = new[] { "RGB (luce)", "Rosso", "Verde", "Blu" }, HorizontalAlignment = HorizontalAlignment.Left };
            _curveChannel.SelectionChanged += (s, e) => RefreshCurve();
            row.Children.Add(_curveChannel);
            p.Children.Add(row);

            _curve = new CurveEditor();
            _curve.Changed += () =>
            {
                SetCurve(_s, _curveChannel.SelectedIndex, _curve.Points);
                Edited();
            };
            p.Children.Add(_curve);

            p.Children.Add(new TextBlock { Text = "Curve pronte:", Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 10, 0, 4) });
            var presets = new WrapPanel();
            void Preset(string name, double[] pts, string tip)
            {
                var b = new Button { Content = name, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
                b.Click += (s, e) =>
                {
                    SetCurve(_s, _curveChannel.SelectedIndex, pts == null ? null : (double[])pts.Clone());
                    RefreshCurve();
                    Edited();
                };
                presets.Children.Add(b);
            }
            Preset("Lineare", null, "Nessuna curva");
            Preset("Contrasto medio", new[] { 0, 0, 0.25, 0.21, 0.75, 0.79, 1, 1 }, "Curva a S leggera");
            Preset("Contrasto forte", new[] { 0, 0, 0.25, 0.16, 0.75, 0.85, 1, 1 }, "Curva a S marcata");
            Preset("Dissolvenza", new[] { 0, 0.08, 0.3, 0.3, 0.75, 0.77, 1, 0.96 }, "Neri sollevati e bianchi smorzati: look opaco da pellicola");
            Preset("Schiarisci", new[] { 0, 0, 0.5, 0.6, 1, 1 }, "Mezzitoni più chiari");
            Preset("Scurisci", new[] { 0, 0, 0.5, 0.4, 1, 1 }, "Mezzitoni più scuri");
            p.Children.Add(presets);

            var resetAll = new Button { Content = "Azzera tutte le curve", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            resetAll.Click += (s, e) =>
            {
                _s.CurveRgb = _s.CurveR = _s.CurveG = _s.CurveB = null;
                RefreshCurve();
                Edited();
            };
            p.Children.Add(resetAll);
            p.Children.Add(Hint("Clic sulla curva per aggiungere un punto, trascinalo per modificarlo, clic destro per eliminarlo. " +
                                "In alto a destra le luci, in basso a sinistra le ombre. I canali Rosso, Verde e Blu cambiano i colori."));
            return p;
        }

        static double[] GetCurve(RawSettings s, int channel) => channel switch { 1 => s.CurveR, 2 => s.CurveG, 3 => s.CurveB, _ => s.CurveRgb };

        static void SetCurve(RawSettings s, int channel, double[] pts)
        {
            switch (channel)
            {
                case 1: s.CurveR = pts; break;
                case 2: s.CurveG = pts; break;
                case 3: s.CurveB = pts; break;
                default: s.CurveRgb = pts; break;
            }
        }

        void RefreshCurve()
        {
            if (_curve == null) return;
            int c = _curveChannel.SelectedIndex;
            _curve.CurveColor = c switch { 1 => Colors.IndianRed, 2 => Colors.LimeGreen, 3 => Colors.CornflowerBlue, _ => Colors.WhiteSmoke };
            _curve.Points = GetCurve(_s, c);
        }

        void UpdateCurveHistogram() => _curve?.SetHistogram(_lumHistogram);

        // ================= Colore =================

        Panel BuildColorTab()
        {
            var p = TabPanel();
            Section(p, "MIX COLORI");
            var selector = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 6) };
            string[] titles = { "Tonalità", "Saturazione", "Luminanza" };
            for (int prop = 0; prop < 3; prop++)
            {
                int k = prop;
                _mixButtons[k] = new Button { Content = titles[k], Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(1, 0, 1, 0), Focusable = false };
                _mixButtons[k].Click += (s, e) => ShowMix(k);
                selector.Children.Add(_mixButtons[k]);

                var sp = new StackPanel();
                for (int c = 0; c < 8; c++)
                {
                    int ci = c;
                    Row(sp, RawSettings.MixNames[c], -100, 100, 0,
                        x => MixArray(x, k)[ci], (x, v) => MixArray(x, k)[ci] = v, MixTrack(k, ci),
                        tip: k switch
                        {
                            0 => $"Sposta la tinta dei {RawSettings.MixNames[c].ToLowerInvariant()} verso il colore vicino",
                            1 => $"Più o meno intensi i {RawSettings.MixNames[c].ToLowerInvariant()}",
                            _ => $"Più chiari o più scuri i {RawSettings.MixNames[c].ToLowerInvariant()} (es. Blu -40 scurisce il cielo)",
                        });
                }
                _mixPanels[k] = sp;
                sp.Visibility = k == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            p.Children.Add(selector);
            foreach (var sp in _mixPanels) p.Children.Add(sp);
            var resetMix = new Button { Content = "Azzera il mix colori", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            resetMix.Click += (s, e) =>
            {
                _s.MixHue = new double[8]; _s.MixSat = new double[8]; _s.MixLum = new double[8];
                RefreshSliders();
                Edited();
            };
            p.Children.Add(resetMix);

            Section(p, "COLOR GRADING");
            var wheels = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 6) };
            (_wheelS, _lumS) = Wheel(wheels, "Ombre", (x, h, sa) => { x.ShadowHue = h; x.ShadowSat = sa; }, (x, v) => x.ShadowLum = v);
            (_wheelM, _lumM) = Wheel(wheels, "Mezzitoni", (x, h, sa) => { x.MidHue = h; x.MidSat = sa; }, (x, v) => x.MidLum = v);
            (_wheelH, _lumH) = Wheel(wheels, "Luci", (x, h, sa) => { x.HighHue = h; x.HighSat = sa; }, (x, v) => x.HighLum = v);
            p.Children.Add(wheels);
            Row(p, "Fusione", 0, 100, 0, x => x.GradeBlending, (x, v) => x.GradeBlending = v, reset: 50, tip: "Quanto si sovrappongono le tre zone");
            Row(p, "Bilanciamento", -100, 100, 0, x => x.GradeBalance, (x, v) => x.GradeBalance = v, tip: "Sposta il confine tra ombre e luci");
            var looks = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            void Look(string name, double sh, double ss, double hh, double hs)
            {
                var b = new Button { Content = name, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (s, e) =>
                {
                    _s.ShadowHue = sh; _s.ShadowSat = ss; _s.HighHue = hh; _s.HighSat = hs;
                    _s.MidSat = 0;
                    RefreshColor();
                    Edited();
                };
                looks.Children.Add(b);
            }
            Look("Cinema (verde-azzurro / arancio)", 190, 30, 35, 25);
            Look("Caldo", 30, 12, 45, 22);
            Look("Freddo", 215, 25, 200, 10);
            Look("Nessuno", 0, 0, 0, 0);
            p.Children.Add(looks);
            p.Children.Add(Hint("Trascina il punto nella ruota per colorare ombre, mezzitoni o luci; più lontano dal centro = più intenso. " +
                                "Il cursore sotto ogni ruota schiarisce o scurisce quella zona. Doppio clic sulla ruota per azzerarla."));
            ShowMix(0);
            return p;
        }

        void ShowMix(int k)
        {
            for (int i = 0; i < 3; i++)
            {
                _mixPanels[i].Visibility = i == k ? Visibility.Visible : Visibility.Collapsed;
                if (i == k) { _mixButtons[i].Background = _mixButtons[i].BorderBrush = Res("AccentBrush"); _mixButtons[i].Foreground = Brushes.White; }
                else { _mixButtons[i].ClearValue(BackgroundProperty); _mixButtons[i].ClearValue(BorderBrushProperty); _mixButtons[i].ClearValue(ForegroundProperty); }
            }
        }

        static double[] MixArray(RawSettings s, int prop)
        {
            s.MixHue ??= new double[8]; s.MixSat ??= new double[8]; s.MixLum ??= new double[8];
            if (s.MixHue.Length < 8 || s.MixSat.Length < 8 || s.MixLum.Length < 8)
            {
                var fixedCopy = s.Clone();
                s.MixHue = fixedCopy.MixHue; s.MixSat = fixedCopy.MixSat; s.MixLum = fixedCopy.MixLum;
            }
            return prop switch { 1 => s.MixSat, 2 => s.MixLum, _ => s.MixHue };
        }

        static Brush MixTrack(int prop, int c)
        {
            double h = MixCenters[c];
            var color = ColorWheel.Hsv(h, 0.85, 0.9);
            switch (prop)
            {
                case 0:
                    return new LinearGradientBrush(ColorWheel.Hsv(h - 25, 0.85, 0.9), ColorWheel.Hsv(h + 25, 0.85, 0.9), 0);
                case 1:
                    return new LinearGradientBrush(Color.FromRgb(0x80, 0x80, 0x80), color, 0);
                default:
                    var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    b.GradientStops.Add(new GradientStop(ColorWheel.Hsv(h, 0.85, 0.25), 0));
                    b.GradientStops.Add(new GradientStop(color, 0.5));
                    b.GradientStops.Add(new GradientStop(ColorWheel.Hsv(h, 0.35, 1), 1));
                    return b;
            }
        }

        (ColorWheel, Slider) Wheel(Panel host, string title, Action<RawSettings, double, double> setColor, Action<RawSettings, double> setLum)
        {
            var sp = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            sp.Children.Add(new TextBlock { Text = title, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), Foreground = Res("TextDimBrush") });
            var wheel = new ColorWheel(98) { HorizontalAlignment = HorizontalAlignment.Center };
            wheel.Changed += () =>
            {
                if (_colorLoading) return;
                setColor(_s, Math.Round(wheel.Hue), wheel.Saturation);
                Edited();
            };
            sp.Children.Add(wheel);
            var lum = new Slider { Minimum = -100, Maximum = 100, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Luminanza: schiarisce o scurisce " + title.ToLowerInvariant() };
            lum.ValueChanged += (s, e) =>
            {
                if (_colorLoading) return;
                setLum(_s, Math.Round(lum.Value));
                Edited();
            };
            lum.MouseDoubleClick += (s, e) => lum.Value = 0;
            sp.Children.Add(lum);
            host.Children.Add(sp);
            return (wheel, lum);
        }

        void RefreshColor()
        {
            if (_wheelS == null) return;
            _colorLoading = true;
            _wheelS.Hue = _s.ShadowHue; _wheelS.Saturation = _s.ShadowSat; _lumS.Value = _s.ShadowLum;
            _wheelM.Hue = _s.MidHue; _wheelM.Saturation = _s.MidSat; _lumM.Value = _s.MidLum;
            _wheelH.Hue = _s.HighHue; _wheelH.Saturation = _s.HighSat; _lumH.Value = _s.HighLum;
            _colorLoading = false;
        }
    }
}
