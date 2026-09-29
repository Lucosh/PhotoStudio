using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PhotoStudio.Core;

namespace PhotoStudio.Dialogs
{
    /// <summary>Preselezione: choose which file formats of the folder to work on (e.g. only the RAW files).</summary>
    public sealed class FormatDialog : DialogWindow
    {
        // The last choice is proposed again for the next folder.
        static HashSet<string> _lastChoice;

        readonly List<(string Ext, CheckBox Box)> _boxes = new List<(string, CheckBox)>();

        public FormatDialog(Window owner, IDictionary<string, int> counts) : base(owner, "Formati da selezionare")
        {
            Body.Children.Add(new TextBlock
            {
                Text = "Le foto sono in più formati. Quali vuoi selezionare e modificare?",
                Margin = new Thickness(0, 0, 0, 12),
            });

            bool useLast = _lastChoice != null && counts.Keys.Any(_lastChoice.Contains);
            var raws = counts.Keys.Where(RawImage.IsRawFile).ToList();
            var others = counts.Keys.Where(e => !RawImage.IsRawFile(e)).ToList();

            if (raws.Count > 0) AddGroup("Camera RAW", raws, counts, useLast);
            if (others.Count > 0) AddGroup("Immagini", others, counts, useLast);

            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            if (raws.Count > 0 && others.Count > 0)
            {
                quick.Children.Add(QuickButton("Solo RAW", e => RawImage.IsRawFile(e)));
                quick.Children.Add(QuickButton("Solo immagini", e => !RawImage.IsRawFile(e)));
            }
            quick.Children.Add(QuickButton("Tutti", e => true));
            Body.Children.Add(quick);
        }

        /// <summary>The chosen extensions (lower case, e.g. ".cr2").</summary>
        public HashSet<string> Extensions { get; private set; }

        void AddGroup(string title, List<string> exts, IDictionary<string, int> counts, bool useLast)
        {
            Body.Children.Add(new TextBlock
            {
                Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4),
                Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            });
            var panel = new WrapPanel { MaxWidth = 420, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var ext in exts)
            {
                var box = new CheckBox
                {
                    Content = $"{ext.TrimStart('.').ToUpperInvariant()}  ({counts[ext]})",
                    IsChecked = !useLast || _lastChoice.Contains(ext),
                    MinWidth = 110, Margin = new Thickness(0, 0, 12, 6),
                };
                _boxes.Add((ext, box));
                panel.Children.Add(box);
            }
            Body.Children.Add(panel);
        }

        Button QuickButton(string text, Func<string, bool> pick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => { foreach (var (ext, box) in _boxes) box.IsChecked = pick(ext); };
            return b;
        }

        protected override void OnOk()
        {
            Extensions = new HashSet<string>(_boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Ext), StringComparer.OrdinalIgnoreCase);
            if (Extensions.Count == 0)
            {
                Error("Scegli almeno un formato.");
                return;
            }
            _lastChoice = Extensions;
            base.OnOk();
        }
    }
}
