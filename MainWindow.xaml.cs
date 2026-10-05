using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    public partial class MainWindow : Window
    {
        static readonly double[] ZoomSteps =
            { 0.01, 0.02, 0.03, 0.05, 0.0667, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667, 1, 1.5, 2, 3, 4, 5, 6, 8, 12, 16, 24, 32 };

        readonly ObservableCollection<Session> _sessions = new ObservableCollection<Session>();
        readonly DispatcherTimer _histTimer, _opacityCommitTimer;
        readonly DrawingBrush _checker;
        Session S;
        WriteableBitmap _display;
        bool _restoring, _syncingHistory, _spaceDown, _altUsed;
        Color _primary = Colors.Black, _secondary = Colors.White;
        double _zoom = 1, _dpiScale = 1;
        int _untitled = 1;

        Document Doc => S?.Doc;
        int ActiveIndex => Doc == null || Doc.Layers.Count == 0 ? -1 : Math.Clamp(LayersList.SelectedIndex, 0, Doc.Layers.Count - 1);
        Layer ActiveLayer => ActiveIndex >= 0 ? Doc.Layers[ActiveIndex] : null;

        public MainWindow()
        {
            InitializeComponent();
            Loc.TranslateTree(this);
            Resources["Loc.ToggleLayer"] = T("Mostra/nascondi livello");   // used inside a template, which TranslateTree cannot reach
            BuildLanguageMenu();
            DarkTitleBar.Apply(this);

            BlendCombo.ItemsSource = BlendItem.All;
            TabStrip.ItemsSource = _sessions;
            _checker = CreateChecker();
            CheckerRect.Fill = _checker;

            _histTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _histTimer.Tick += (s, e) => { _histTimer.Stop(); UpdateHistogram(); };
            _opacityCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _opacityCommitTimer.Tick += (s, e) => { _opacityCommitTimer.Stop(); Commit(T("Opacità livello")); };

            var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
            FontCombo.ItemsSource = fonts;
            FontCombo.SelectedItem = fonts.Contains("Segoe UI") ? "Segoe UI" : fonts.FirstOrDefault();

            RegisterActions();
            StartMarchingAnts();
            UpdateColorSwatches();
            SelectTool(Tool.Brush);
            ActivateSession(null);

            Loaded += (s, e) => _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            DpiChanged += (s, e) => { _dpiScale = e.NewDpi.DpiScaleX; if (S != null) SetZoom(_zoom); };
        }

        static DrawingBrush CreateChecker()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
            var dark = new GeometryGroup();
            dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, 1)));
            dark.Children.Add(new RectangleGeometry(new Rect(1, 1, 1, 1)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(204, 204, 204)), null, dark));
            return new DrawingBrush(g)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 16, 16),
                Stretch = Stretch.Fill,
            };
        }

        void StartMarchingAnts()
        {
            var anim = new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.5)) { RepeatBehavior = RepeatBehavior.Forever };
            SelPathWhite.BeginAnimation(Shape.StrokeDashOffsetProperty, anim);
            PreviewWhite.BeginAnimation(Shape.StrokeDashOffsetProperty, anim);
        }

        sealed class BusyScope : IDisposable
        {
            public BusyScope() => Mouse.OverrideCursor = Cursors.Wait;
            public void Dispose() => Mouse.OverrideCursor = null;
        }

        static IDisposable Busy() => new BusyScope();

        void Status(string message) => StatusText.Text = message;

        // ================= Sessions (tabs) =================

        void AddSession(Session s)
        {
            _sessions.Add(s);
            ActivateSession(s);
        }

        void ActivateSession(Session s)
        {
            if (S != null)
            {
                S.Zoom = _zoom;
                S.ScrollX = Scroller.HorizontalOffset;
                S.ScrollY = Scroller.VerticalOffset;
                if (ActiveIndex >= 0) S.ActiveIndex = ActiveIndex;
                S.IsActive = false;
                S.Doc.Layers.CollectionChanged -= Layers_CollectionChanged;
            }
            CancelInteraction();
            if (S != null && S != s) S.Doc.Park();
            S = s;
            SyncBatchSelection();
            UpdateCursor();

            bool has = s != null;
            WelcomePanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            Scroller.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            TabStrip.Visibility = _sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (!has)
            {
                _display = null;
                CanvasImage.Source = null;
                LayersList.ItemsSource = null;
                HistoryList.ItemsSource = null;
                SizeText.Text = ZoomText.Text = "";
                UpdateSelectionVisual();
                UpdateHistogram();
                UpdateTitle();
                return;
            }

            s.IsActive = true;
            foreach (var l in s.Doc.Layers)
            {
                l.PropertyChanged -= Layer_PropertyChanged;
                l.PropertyChanged += Layer_PropertyChanged;
            }
            s.Doc.Layers.CollectionChanged += Layers_CollectionChanged;
            LayersList.ItemsSource = s.Doc.Layers;
            LayersList.SelectedIndex = Math.Clamp(s.ActiveIndex, 0, s.Doc.Layers.Count - 1);
            HistoryList.ItemsSource = s.History.Entries;
            SyncHistorySelection();
            InitDisplay();
            UpdateSelectionVisual();
            UpdateTitle();

            double zoom = s.Zoom, sx = s.ScrollX, sy = s.ScrollY;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (S != s) return;
                if (zoom <= 0) FitToScreen(true);
                else
                {
                    SetZoom(zoom);
                    Scroller.UpdateLayout();
                    Scroller.ScrollToHorizontalOffset(sx);
                    Scroller.ScrollToVerticalOffset(sy);
                }
            }));
        }

        void CloseSession(Session s)
        {
            if (s.Modified)
            {
                if (s != S) ActivateSession(s);
                if (!ConfirmClose(s)) return;
            }
            int idx = _sessions.IndexOf(s);
            if (s == S)
            {
                s.Doc.Layers.CollectionChanged -= Layers_CollectionChanged;
                CancelInteraction();
                S = null;
                _sessions.Remove(s);
                OnSessionClosed(s);
                ActivateSession(_sessions.Count > 0 ? _sessions[Math.Min(idx, _sessions.Count - 1)] : null);
            }
            else
            {
                _sessions.Remove(s);
                OnSessionClosed(s);
            }
        }

        bool ConfirmClose(Session s)
        {
            var r = MessageBox.Show(this, T("Salvare le modifiche apportate a \"{0}\" prima di chiudere?", s.Title), "PhotoStudio",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save(false);
            return true;
        }

        void Tab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is Session s && s != S) ActivateSession(s);
        }

        void Tab_MiddleDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is Session s) CloseSession(s);
        }

        void TabClose_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is Session s) CloseSession(s);
        }

        // ================= Layers events =================

        void Layers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null) return;
            foreach (Layer l in e.NewItems)
            {
                l.PropertyChanged -= Layer_PropertyChanged;
                l.PropertyChanged += Layer_PropertyChanged;
            }
        }

        void Layer_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_restoring || S == null || !S.Doc.Layers.Contains((Layer)sender)) return;
            switch (e.PropertyName)
            {
                case nameof(Layer.Visible):
                    Recomposite();
                    Commit(((Layer)sender).Visible ? T("Mostra livello") : T("Nascondi livello"));
                    break;
                case nameof(Layer.Blend):
                    Recomposite();
                    Commit(T("Metodo di fusione"));
                    break;
                case nameof(Layer.Opacity):
                    Recomposite();
                    _opacityCommitTimer.Stop();
                    _opacityCommitTimer.Start();
                    break;
            }
        }

        void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (S == null || _restoring) return;
            if (LayersList.SelectedIndex < 0 && Doc.Layers.Count > 0)
            {
                LayersList.SelectedIndex = Math.Clamp(S.ActiveIndex, 0, Doc.Layers.Count - 1);
                return;
            }
            if (LayersList.SelectedIndex >= 0) S.ActiveIndex = LayersList.SelectedIndex;
            UpdateTitle();
        }

        void SelectLayer(int index)
        {
            if (Doc == null || Doc.Layers.Count == 0) return;
            index = Math.Clamp(index, 0, Doc.Layers.Count - 1);
            S.ActiveIndex = index;
            LayersList.SelectedIndex = index;
            LayersList.ScrollIntoView(Doc.Layers[index]);
        }

        // ================= Display =================

        void InitDisplay()
        {
            int w = Doc.Width, h = Doc.Height;
            _display = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            CanvasImage.Source = _display;
            CanvasImage.Width = CheckerRect.Width = Overlay.Width = w;
            CanvasImage.Height = CheckerRect.Height = Overlay.Height = h;
            Doc.CompositeRegion(Doc.Bounds);
            _display.WritePixels(Doc.Bounds, Doc.Composite, w * 4, 0, 0);
            SizeText.Text = $"{w} × {h} px";
            ScheduleHistogram();
        }

        void Recomposite(Int32Rect? region = null)
        {
            if (S == null || _display == null) return;
            var r = ImageOps.Intersect(region ?? Doc.Bounds, Doc.Bounds);
            if (ImageOps.IsEmpty(r)) return;
            Doc.CompositeRegion(r);
            _display.WritePixels(r, Doc.Composite, Doc.Width * 4, r.X, r.Y);
            ScheduleHistogram();
        }

        void UpdateSelectionVisual()
        {
            var sel = S?.Selection;
            var g = sel == null || sel.IsEmpty ? null : sel.Outline;
            SelPathBlack.Data = SelPathWhite.Data = g;
            SelPathBlack.RenderTransform = SelPathWhite.RenderTransform = null;
        }

        void UpdateTitle()
        {
            AutoEnhanceButton.IsEnabled = AiButton.IsEnabled = S != null;
            if (S == null)
            {
                Title = "PhotoStudio";
                UndoMenuItem.Header = T("Annulla");
                RedoMenuItem.Header = T("Ripeti");
                return;
            }
            Title = $"{S.DisplayTitle} @ {_zoom * 100:0.#}% ({ActiveLayer?.Name}, RGB/8) — PhotoStudio";
            var h = S.History;
            UndoMenuItem.Header = h.CanUndo ? T("Annulla {0}", h.Entries[h.Index].Name) : T("Annulla");
            RedoMenuItem.Header = h.CanRedo ? T("Ripeti {0}", h.Entries[h.Index + 1].Name) : T("Ripeti");
        }

        // ================= Zoom =================

        void SetZoom(double z, Point? anchor = null)
        {
            if (S == null) return;
            z = Math.Clamp(z, 0.01, 32);
            var a = anchor ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
            Point docPt = Scroller.TranslatePoint(a, CanvasImage);
            _zoom = z;
            double scale = z / _dpiScale, t = 1 / scale;
            ZoomTransform.ScaleX = ZoomTransform.ScaleY = scale;
            foreach (var shape in new Shape[] { SelPathBlack, SelPathWhite, PreviewBlack, PreviewWhite, CropFrame, BrushCursor, BrushCursorOuter })
                shape.StrokeThickness = t;
            CloneMarkerScale.ScaleX = CloneMarkerScale.ScaleY = t;
            _checker.Viewport = new Rect(0, 0, 16 * t, 16 * t);
            RenderOptions.SetBitmapScalingMode(CanvasImage, z >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

            Scroller.UpdateLayout();
            Point np = CanvasImage.TranslatePoint(docPt, Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + np.X - a.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + np.Y - a.Y);
            ZoomText.Text = $"{z * 100:0.##}%";
            UpdateTitle();
            UpdateBrushCursor(null);
        }

        void ZoomStep(int dir, Point? anchor = null)
        {
            double next = _zoom;
            if (dir > 0)
            {
                foreach (var s in ZoomSteps) if (s > _zoom * 1.001) { next = s; break; }
            }
            else
            {
                for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < _zoom * 0.999) { next = ZoomSteps[i]; break; }
            }
            SetZoom(next, anchor);
        }

        void FitToScreen(bool limitTo100 = false)
        {
            if (S == null) return;
            double availW = Math.Max(50, CanvasArea.ActualWidth - 176), availH = Math.Max(50, CanvasArea.ActualHeight - 176);
            double z = Math.Min(availW / Doc.Width, availH / Doc.Height) * _dpiScale;
            if (limitTo100) z = Math.Min(z, 1);
            SetZoom(z);
            Scroller.UpdateLayout();
            Scroller.ScrollToHorizontalOffset((Scroller.ExtentWidth - Scroller.ViewportWidth) / 2);
            Scroller.ScrollToVerticalOffset((Scroller.ExtentHeight - Scroller.ViewportHeight) / 2);
        }

        // ================= History =================

        void Commit(string name)
        {
            if (S == null) return;
            _opacityCommitTimer.Stop();
            foreach (var l in Doc.Layers) if (l.Dirty) l.UpdateThumbnail();
            S.History.Push(name, Doc.Snapshot(Math.Max(0, ActiveIndex), S.Selection));
            S.Modified = true;
            SyncHistorySelection();
            UpdateTitle();
        }

        void RestoreState(DocState st)
        {
            CancelInteraction();
            _restoring = true;
            try
            {
                bool sizeChanged = st.Width != Doc.Width || st.Height != Doc.Height;
                Doc.Restore(st);
                S.Selection = st.Selection;
                S.ActiveIndex = Math.Clamp(st.ActiveIndex, 0, Doc.Layers.Count - 1);
                LayersList.SelectedIndex = S.ActiveIndex;
                if (sizeChanged) InitDisplay(); else Recomposite();
            }
            finally
            {
                _restoring = false;
            }
            UpdateSelectionVisual();
            SyncHistorySelection();
            S.Modified = true;
            UpdateTitle();
        }

        void Undo()
        {
            var st = S.History.Undo();
            if (st != null) RestoreState(st);
        }

        void Redo()
        {
            var st = S.History.Redo();
            if (st != null) RestoreState(st);
        }

        void SyncHistorySelection()
        {
            _syncingHistory = true;
            HistoryList.SelectedIndex = S?.History.Index ?? -1;
            if (HistoryList.SelectedItem != null) HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            _syncingHistory = false;
        }

        void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingHistory || S == null || HistoryList.SelectedIndex < 0) return;
            var st = S.History.JumpTo(HistoryList.SelectedIndex);
            if (st != null) RestoreState(st);
        }

        // ================= Histogram =================

        void ScheduleHistogram()
        {
            _histTimer.Stop();
            _histTimer.Start();
        }

        void UpdateHistogram()
        {
            if (S == null)
            {
                HistR.Points = new PointCollection(); HistG.Points = new PointCollection();
                HistB.Points = new PointCollection(); HistL.Points = new PointCollection();
                return;
            }
            var px = Doc.Composite;
            int n = px.Length / 4, step = Math.Max(1, n / 400000);
            var hr = new int[256]; var hg = new int[256]; var hb = new int[256]; var hl = new int[256];
            for (int p = 0; p < n; p += step)
            {
                int i = p * 4;
                if (px[i + 3] == 0) continue;
                hb[px[i]]++; hg[px[i + 1]]++; hr[px[i + 2]]++;
                hl[(px[i + 2] * 77 + px[i + 1] * 150 + px[i] * 29) >> 8]++;
            }
            int max = 1;
            for (int i = 1; i < 255; i++) max = Math.Max(max, Math.Max(hl[i], Math.Max(hr[i], Math.Max(hg[i], hb[i]))));
            double sc = 96.0 / max;
            HistR.Points = Poly(hr, sc, true);
            HistG.Points = Poly(hg, sc, true);
            HistB.Points = Poly(hb, sc, true);
            HistL.Points = Poly(hl, sc, false);
        }

        static PointCollection Poly(int[] h, double sc, bool closed)
        {
            var pc = new PointCollection(260);
            if (closed) pc.Add(new Point(0, 100));
            for (int i = 0; i < 256; i++) pc.Add(new Point(i, 100 - Math.Min(100, h[i] * sc)));
            if (closed) pc.Add(new Point(255, 100));
            pc.Freeze();
            return pc;
        }

        // ================= Colors =================

        void UpdateColorSwatches()
        {
            PrimarySwatch.Background = new SolidColorBrush(_primary);
            SecondarySwatch.Background = new SolidColorBrush(_secondary);
        }

        void PrimarySwatch_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var d = new ColorPickerDialog(this, T("Selettore colore (colore di primo piano)"), _primary);
            if (d.ShowDialog() == true) { _primary = d.SelectedColor; UpdateColorSwatches(); }
        }

        void SecondarySwatch_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var d = new ColorPickerDialog(this, T("Selettore colore (colore di sfondo)"), _secondary);
            if (d.ShowDialog() == true) { _secondary = d.SelectedColor; UpdateColorSwatches(); }
        }

        void SwapColors_Click(object sender, RoutedEventArgs e) => SwapColors();
        void ResetColors_Click(object sender, RoutedEventArgs e) => ResetColors();

        void SwapColors()
        {
            (_primary, _secondary) = (_secondary, _primary);
            UpdateColorSwatches();
        }

        void ResetColors()
        {
            _primary = Colors.Black;
            _secondary = Colors.White;
            UpdateColorSwatches();
        }

        // ================= Keyboard =================

        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Space)
            {
                if (!_spaceDown) { _spaceDown = true; UpdateCursor(); }
                e.Handled = true;
                return;
            }
            if (_dragging) return;
            if (HandleShortcut(key, Keyboard.Modifiers)) e.Handled = true;
        }

        void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Space)
            {
                _spaceDown = false;
                UpdateCursor();
                e.Handled = true;
            }
            else if ((key == Key.LeftAlt || key == Key.RightAlt) && _altUsed)
            {
                // Alt was used as a tool modifier: don't let it activate the menu bar.
                _altUsed = false;
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() => { if (MainMenu.IsKeyboardFocusWithin) CanvasArea.Focus(); }));
            }
        }

        // ================= Drag & drop / closing =================

        void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            // A folder dropped on the window starts the Preselezione.
            var folder = files.FirstOrDefault(System.IO.Directory.Exists);
            if (folder != null) { StartCulling(folder); return; }
            foreach (var f in files) OpenFile(f);
        }

        void Window_Closing(object sender, CancelEventArgs e)
        {
            foreach (var s in _sessions.ToList())
            {
                if (!s.Modified) continue;
                if (s != S) ActivateSession(s);
                if (!ConfirmClose(s)) { e.Cancel = true; return; }
            }
        }

        // ================= Language =================

        bool _restartOnClose;

        void BuildLanguageMenu()
        {
            // The English word stays next to the translated one, so the menu can be found in any language.
            LanguageMenu.Header = "🌐  " + T("Lingua") + (Loc.Language == "en" ? "" : "  (Language)");
            foreach (var (code, name) in Loc.Languages)
            {
                // No Tag: menu items with a Tag are commands, enabled or disabled by UpdateMenuState.
                var item = new MenuItem { Header = name, IsChecked = code == Loc.Language };
                item.Click += (s, e) => ChangeLanguage(code);
                LanguageMenu.Items.Add(item);
                _languageItems[code] = item;
            }
        }

        readonly System.Collections.Generic.Dictionary<string, MenuItem> _languageItems = new System.Collections.Generic.Dictionary<string, MenuItem>();

        void ChangeLanguage(string code)
        {
            if (code == Loc.Language) return;
            try { Loc.SaveLanguage(code); }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Impossibile salvare la lingua:\n{0}", ex.Message), "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            foreach (var kv in _languageItems) kv.Value.IsChecked = kv.Key == code;
            if (MessageBox.Show(this, T("La nuova lingua verrà usata al prossimo avvio di PhotoStudio.\n\nRiavviare adesso?"), "PhotoStudio",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _restartOnClose = true;
            Close();                                  // asks to save the open documents; the user may cancel
            if (IsVisible) _restartOnClose = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_restartOnClose && Environment.ProcessPath is string exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        }
    }
}
