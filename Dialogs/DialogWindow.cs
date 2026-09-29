using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PhotoStudio.Dialogs
{
    public static class DarkTitleBar
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Apply(Window w)
        {
            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    var h = new WindowInteropHelper(w).Handle;
                    int on = 1;
                    DwmSetWindowAttribute(h, 20, ref on, 4);      // immersive dark mode
                    int caption = 0x002A2A2A;
                    DwmSetWindowAttribute(h, 35, ref caption, 4); // caption color (Windows 11)
                    int text = 0x00DCDCDC;
                    DwmSetWindowAttribute(h, 36, ref text, 4);    // caption text color
                }
                catch { }
            };
        }
    }

    /// <summary>Base for the dark, code-built dialogs: a body panel plus OK / Cancel buttons.</summary>
    public class DialogWindow : Window
    {
        protected readonly StackPanel Body = new StackPanel { Margin = new Thickness(18, 16, 18, 6) };
        protected readonly StackPanel ButtonBar;
        protected readonly Button OkButton;
        protected readonly Button CancelButton;

        public DialogWindow(Window owner, string title)
        {
            Owner = owner;
            Title = title;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = (Brush)Application.Current.FindResource("PanelBg");
            Foreground = (Brush)Application.Current.FindResource("TextBrush");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            DarkTitleBar.Apply(this);

            OkButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
            OkButton.Click += (s, e) => OnOk();
            CancelButton = new Button { Content = "Annulla", IsCancel = true, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
            ButtonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 8, 18, 16) };
            ButtonBar.Children.Add(OkButton);
            ButtonBar.Children.Add(CancelButton);

            var root = new DockPanel();
            DockPanel.SetDock(ButtonBar, Dock.Bottom);
            root.Children.Add(ButtonBar);
            root.Children.Add(Body);
            Content = root;
        }

        protected virtual void OnOk() => DialogResult = true;

        protected static TextBlock Label(string text, double width = 110) =>
            new TextBlock { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

        protected static StackPanel Row(params UIElement[] children)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var c in children) sp.Children.Add(c);
            return sp;
        }

        protected static TextBox NumberBox(double value, double width = 70) =>
            new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = width, Height = 24 };

        public static bool TryParse(string s, out double v) =>
            double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        protected void Error(string message) =>
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
