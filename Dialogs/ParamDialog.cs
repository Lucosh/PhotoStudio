using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    public sealed class ParamSpec
    {
        public ParamSpec(string label, double min, double max, double value, int decimals = 0)
        {
            Label = label; Min = min; Max = max; Value = value; Decimals = decimals;
        }

        public string Label { get; }
        public double Min { get; }
        public double Max { get; }
        public double Value { get; }
        public int Decimals { get; }
        public string[] Options { get; private init; }
        public bool IsCheck { get; private init; }

        public static ParamSpec Choice(string label, int value, params string[] options) =>
            new ParamSpec(label, 0, options.Length - 1, value) { Options = options };

        public static ParamSpec Check(string label, bool value) =>
            new ParamSpec(label, 0, 1, value ? 1 : 0) { IsCheck = true };
    }

    /// <summary>Slider-based parameter dialog with debounced live preview (used by adjustments and filters).</summary>
    public sealed class ParamDialog : DialogWindow
    {
        readonly double[] _values;
        readonly Action<double[]> _preview;
        readonly List<Action> _resetters = new List<Action>();
        readonly DispatcherTimer _timer;
        readonly CheckBox _previewBox;

        public ParamDialog(Window owner, string title, ParamSpec[] specs, Action<double[]> preview) : base(owner, title)
        {
            _preview = preview;
            _values = new double[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                _values[i] = specs[i].Value;
                Body.Children.Add(BuildRow(specs[i], i));
            }

            var reset = new Button { Content = T("Ripristina"), MinWidth = 84 };
            reset.Click += (s, e) => { foreach (var r in _resetters) r(); Changed(); };
            ButtonBar.Children.Insert(0, reset);

            if (preview != null)
            {
                _previewBox = new CheckBox { Content = T("Anteprima"), IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
                _previewBox.Checked += (s, e) => Changed();
                _previewBox.Unchecked += (s, e) => _preview(null);
                Body.Children.Add(_previewBox);
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                _timer.Tick += (s, e) =>
                {
                    _timer.Stop();
                    if (_previewBox.IsChecked == true) _preview(Values);
                };
                Loaded += (s, e) => Changed();
            }
        }

        public double[] Values => (double[])_values.Clone();

        void Changed()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Start();
        }

        UIElement BuildRow(ParamSpec spec, int index)
        {
            if (spec.IsCheck)
            {
                var cb = new CheckBox { Content = spec.Label, IsChecked = spec.Value > 0, Margin = new Thickness(0, 0, 0, 10) };
                cb.Checked += (s, e) => { _values[index] = 1; Changed(); };
                cb.Unchecked += (s, e) => { _values[index] = 0; Changed(); };
                _resetters.Add(() => cb.IsChecked = spec.Value > 0);
                return cb;
            }

            if (spec.Options != null)
            {
                var combo = new ComboBox { Width = 220, ItemsSource = spec.Options, SelectedIndex = (int)spec.Value };
                combo.SelectionChanged += (s, e) => { _values[index] = combo.SelectedIndex; Changed(); };
                _resetters.Add(() => combo.SelectedIndex = (int)spec.Value);
                return Row(Label(spec.Label, 130), combo);
            }

            string fmt = "F" + spec.Decimals;
            var slider = new Slider { Minimum = spec.Min, Maximum = spec.Max, Value = spec.Value, Width = 230 };
            var box = new TextBox { Width = 60, Height = 24, Margin = new Thickness(10, 0, 0, 0), Text = spec.Value.ToString(fmt, CultureInfo.CurrentCulture) };
            bool updating = false;
            slider.ValueChanged += (s, e) =>
            {
                double v = Math.Round(slider.Value, spec.Decimals);
                _values[index] = v;
                if (!updating) box.Text = v.ToString(fmt, CultureInfo.CurrentCulture);
                Changed();
            };
            void Commit()
            {
                if (!TryParse(box.Text, out double v)) { box.Text = _values[index].ToString(fmt, CultureInfo.CurrentCulture); return; }
                v = Math.Clamp(v, spec.Min, spec.Max);
                updating = true;
                slider.Value = v;
                updating = false;
                box.Text = v.ToString(fmt, CultureInfo.CurrentCulture);
            }
            box.LostKeyboardFocus += (s, e) => Commit();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
            _resetters.Add(() => slider.Value = spec.Value);
            return Row(Label(spec.Label, 130), slider, box);
        }
    }
}
