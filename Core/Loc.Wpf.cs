using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PhotoStudio.Core
{
    public static partial class Loc
    {
        /// <summary>
        /// Translates the texts written in XAML under <paramref name="root"/>: menu headers and shortcuts,
        /// contents, text blocks and tooltips. Call it once, after InitializeComponent.
        /// </summary>
        public static void TranslateTree(DependencyObject root)
        {
            if (Language != "it") Translate(root, new HashSet<DependencyObject>());
        }

        static void Translate(DependencyObject o, HashSet<DependencyObject> seen)
        {
            if (o == null || !seen.Add(o)) return;

            if (o is HeaderedItemsControl items && items.Header is string header) items.Header = T(header);
            if (o is HeaderedContentControl headered && headered.Header is string header2) headered.Header = T(header2);
            if (o is MenuItem menu && !string.IsNullOrEmpty(menu.InputGestureText)) menu.InputGestureText = Keys(menu.InputGestureText);
            if (o is ContentControl content && content.Content is string text) content.Content = T(text);
            if (o is TextBlock block && !string.IsNullOrEmpty(block.Text) && BindingOperations.GetBindingExpression(block, TextBlock.TextProperty) == null)
                block.Text = T(block.Text);
            if (o is FrameworkElement fe)
            {
                if (fe.ToolTip is string tip) fe.ToolTip = T(tip);
                if (fe.ContextMenu != null) Translate(fe.ContextMenu, seen);
            }

            foreach (var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())
                Translate(child, seen);
        }

        /// <summary>Key names of a shortcut ("Alt+Maiusc+C", "Ctrl+Invio") in the interface language.</summary>
        public static string Keys(string gesture)
        {
            if (Language == "it") return gesture;
            return string.Join("+", gesture.Split('+').Select(k => (Language, k) switch
            {
                (_, "Maiusc" or "Shift") => Language switch { "de" => "Umschalt", "fr" => "Maj", "es" => "Mayús", _ => "Shift" },
                (_, "Invio") => Language switch { "de" => "Eingabe", "fr" => "Entrée", "es" => "Intro", _ => "Enter" },
                (_, "Canc") => Language switch { "de" => "Entf", "fr" => "Suppr", "es" => "Supr", _ => "Del" },
                ("de", "Ctrl") => "Strg",
                (_, "Backspace") => Language switch { "de" => "Rücktaste", "fr" => "Retour arrière", "es" => "Retroceso", _ => "Backspace" },
                _ => k,
            }));
        }
    }
}
