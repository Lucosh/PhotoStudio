using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Polygon = System.Windows.Shapes.Polygon;
using System.Windows.Threading;
using Microsoft.Win32;
using PhotoStudio.Core;

namespace PhotoStudio.Dialogs
{
    /// <summary>
    /// "Preselezione": browse all the photos of a folder (thumbnails on the left, large preview on the right),
    /// rate them with stars and color labels, compare similar shots side by side, move the rejects to the
    /// Recycle Bin with Canc, then hand the photos that match the filter over for editing.
    /// </summary>
    public sealed class CullingWindow : Window
    {
        const int CacheSize = 12;

        const string ItemTemplateXaml =
@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <StackPanel Margin='4,6,4,6'>
        <Grid Height='150' Background='#FF1A1A1A'>
            <TextBlock Text='…' Foreground='#FF666666' FontSize='18' HorizontalAlignment='Center' VerticalAlignment='Center'/>
            <Image Source='{Binding Thumbnail}' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/>
            <Border x:Name='Burst' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='4' Padding='5,1,5,2' CornerRadius='2' Background='#D0101010'>
                <TextBlock Text='{Binding BurstText}' FontSize='10' Foreground='#FFDDDDDD'/>
            </Border>
        </Grid>
        <Border Height='3' Background='{Binding LabelBrush}'/>
        <DockPanel Margin='2,4,2,0'>
            <TextBlock DockPanel.Dock='Right' Text='{Binding Badge}' Foreground='#FF9A9A9A' FontSize='10' VerticalAlignment='Center' Margin='6,0,0,0'/>
            <TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis'/>
        </DockPanel>
        <TextBlock Text='{Binding RatingText}' Foreground='#FFF2C94C' Margin='2,1,2,0' FontSize='11'/>
    </StackPanel>
    <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding BurstText}' Value=''>
            <Setter TargetName='Burst' Property='Visibility' Value='Collapsed'/>
        </DataTrigger>
    </DataTemplate.Triggers>
</DataTemplate>";

        /// <summary>One photo slot of the stage: 1 in normal view, 2-4 when comparing.</summary>
        sealed class Pane
        {
            public Border Frame;
            public ScrollViewer Viewer;
            public Grid Host;
            public Image Image, Clipping;
            public Border CaptionBox;
            public TextBlock Caption, Loading;
            public PhotoItem Item;
            public BitmapSource Full;
            public PhotoItem FullItem;
        }

        readonly string _folder;
        readonly FolderState _state;
        readonly List<PhotoItem> _all;
        readonly ObservableCollection<PhotoItem> _items;
        readonly ThumbnailLoader _thumbs;
        readonly ListBox _list;
        readonly UniformGrid _paneGrid;
        readonly List<Pane> _panes = new List<Pane>();
        readonly TextBlock _counter, _fileName, _fileInfo, _exifInfo, _toast, _empty, _overlayTitle, _overlaySummary, _histText;
        readonly Border _toastBox, _histBox, _labelDot;
        readonly Polygon _hR = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0xE0, 0x45, 0x3E)) };
        readonly Polygon _hG = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x40, 0xC0, 0x50)) };
        readonly Polygon _hB = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x4A, 0x7F, 0xE8)) };
        readonly ComboBox _ratingFilter, _labelFilter;
        readonly Button _compareButton;
        readonly Grid _overlay;
        readonly TextBox _output;
        readonly Button _openButton;
        readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        readonly Stack<(PhotoItem Item, int Index)> _deleted = new Stack<(PhotoItem, int)>();
        readonly Dictionary<PhotoItem, BitmapSource> _cache = new Dictionary<PhotoItem, BitmapSource>();
        readonly Dictionary<PhotoItem, PhotoAnalysis> _analysis = new Dictionary<PhotoItem, PhotoAnalysis>();
        readonly LinkedList<PhotoItem> _lru = new LinkedList<PhotoItem>();
        readonly HashSet<PhotoItem> _pending = new HashSet<PhotoItem>();
        readonly CancellationTokenSource _metaCts = new CancellationTokenSource();
        int _index, _base, _paneCount = 1, _deletedCount, _previewSize = 2560;
        bool _zoom, _syncing, _closed, _showHistogram = true, _showClipping, _burstsReady;
        Point _zoomAnchor = new Point(0.5, 0.5);
        Pane _dragPane;
        Point _dragStart;
        Vector _dragOffset;
        bool _dragging, _dragMoved;

        /// <summary>The photos to edit: those not deleted that match the filter, in order.</summary>
        public List<PhotoItem> Kept => _items.ToList();
        public string OutputFolder { get; private set; }
        public int DeletedCount => _deletedCount;

        public CullingWindow(Window owner, string folder, IEnumerable<PhotoItem> photos, FolderState state)
        {
            Owner = owner;
            _folder = folder;
            _state = state ?? FolderState.Load(folder);
            _all = photos.ToList();
            _items = new ObservableCollection<PhotoItem>(_all);
            _thumbs = new ThumbnailLoader(_items);

            Title = "Preselezione — " + folder;
            Width = 1400;
            Height = 900;
            MinWidth = 1000;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowState = WindowState.Maximized;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            UseLayoutRounding = true;
            Background = Res("CanvasBg");
            Foreground = Res("TextBrush");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            DarkTitleBar.Apply(this);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Top bar
            var top = new DockPanel { Background = Res("PanelHeaderBg"), Height = 48, LastChildFill = true };
            var close = new Button { Content = "Chiudi", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            top.Children.Add(close);
            var finish = new Button
            {
                Content = "Fine selezione: modifica le foto  ▶", Padding = new Thickness(14, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center, Focusable = false,
                Background = Res("AccentBrush"), BorderBrush = Res("AccentBrush"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
                ToolTip = "Apre le foto rimaste (quelle che corrispondono al filtro) per modificarle una alla volta (Invio)",
            };
            finish.Click += (s, e) => ShowFinish(false);
            DockPanel.SetDock(finish, Dock.Right);
            top.Children.Add(finish);

            var filters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            filters.Children.Add(new TextBlock { Text = "Mostra:", Foreground = Res("TextDimBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _ratingFilter = new ComboBox
            {
                Width = 120, Focusable = false, SelectedIndex = 0,
                ItemsSource = new[] { "Tutte le stelle", "★ o più", "★★ o più", "★★★ o più", "★★★★ o più", "★★★★★" },
                ToolTip = "Mostra solo le foto con almeno queste stelle (0-5 per assegnarle)",
            };
            _labelFilter = new ComboBox
            {
                Width = 120, Focusable = false, SelectedIndex = 0, Margin = new Thickness(6, 0, 0, 0),
                ItemsSource = new[] { "Tutti i colori", "Rosso", "Giallo", "Verde", "Blu", "Viola", "Senza colore" },
                ToolTip = "Mostra solo le foto con questa etichetta colore (6 rosso, 7 giallo, 8 verde, 9 blu)",
            };
            _ratingFilter.SelectionChanged += (s, e) => RebuildView(null);
            _labelFilter.SelectionChanged += (s, e) => RebuildView(null);
            filters.Children.Add(_ratingFilter);
            filters.Children.Add(_labelFilter);
            _compareButton = new Button { Content = "Confronta", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12, 0, 0, 0), Focusable = false, ToolTip = "Foto affiancate: 1 → 2 → 4 (C). Con B confronti la raffica." };
            _compareButton.Click += (s, e) => CycleCompare();
            filters.Children.Add(_compareButton);
            DockPanel.SetDock(filters, Dock.Right);
            top.Children.Add(filters);

            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            titles.Children.Add(new TextBlock { Text = "PRESELEZIONE", FontWeight = FontWeights.SemiBold, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            titles.Children.Add(new TextBlock { Text = Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : folder, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            _counter = new TextBlock { Foreground = Res("TextDimBrush"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(_counter);
            top.Children.Add(titles);
            root.Children.Add(top);

            // ---- Content: thumbnails | splitter | stage
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 150, MaxWidth = 520 });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(content, 1);
            root.Children.Add(content);

            _list = new ListBox
            {
                ItemsSource = _items,
                ItemTemplate = (DataTemplate)XamlReader.Parse(ItemTemplateXaml),
                Background = Res("PanelBg"),
            };
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            _list.SelectionChanged += (s, e) =>
            {
                if (_syncing || _list.SelectedIndex < 0) return;
                _index = _list.SelectedIndex;
                ShowCurrent();
            };
            content.Children.Add(_list);

            var splitter = new GridSplitter { Width = 4, Background = Res("DividerBrush"), HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Focusable = false };
            Grid.SetColumn(splitter, 1);
            content.Children.Add(splitter);

            var stage = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)), ClipToBounds = true };
            Grid.SetColumn(stage, 2);
            content.Children.Add(stage);

            _paneGrid = new UniformGrid { Rows = 1, Columns = 1 };
            stage.Children.Add(_paneGrid);

            _empty = new TextBlock
            {
                Text = "Nessuna foto da mostrare.", FontSize = 16, Foreground = Res("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            };
            stage.Children.Add(_empty);

            // Info box: name, stars, label, position and shooting data.
            var info = new StackPanel();
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            _labelDot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _fileName = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            nameRow.Children.Add(_labelDot);
            nameRow.Children.Add(_fileName);
            _fileInfo = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), Margin = new Thickness(0, 2, 0, 0) };
            _exifInfo = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), Margin = new Thickness(0, 2, 0, 0) };
            info.Children.Add(nameRow);
            info.Children.Add(_fileInfo);
            info.Children.Add(_exifInfo);
            stage.Children.Add(new Border
            {
                Child = info, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(16), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            });

            // Histogram with clipping warnings (H to hide, J to show the clipped areas).
            var hist = new StackPanel { Width = 240 };
            hist.Children.Add(new Viewbox { Stretch = Stretch.Fill, Height = 70, Child = new Canvas { Width = 256, Height = 100, Children = { _hR, _hG, _hB } } });
            _histText = new TextBlock { Margin = new Thickness(0, 5, 0, 0), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            hist.Children.Add(_histText);
            _histBox = new Border
            {
                Child = hist, Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 8, 8, 7), Margin = new Thickness(16), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            stage.Children.Add(_histBox);

            _toast = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            _toastBox = new Border
            {
                Child = _toast, Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x2A, 0x2A, 0x2A)), CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)), BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 9), Margin = new Thickness(0, 0, 0, 20), MaxWidth = 700, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed,
            };
            stage.Children.Add(_toastBox);
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); _toastBox.Visibility = Visibility.Collapsed; };

            // ---- Hint bar
            var hints = new Border
            {
                Background = Res("PanelHeaderBg"), Height = 30, BorderBrush = Res("DividerBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = "← →  scorri   ·   0-5  stelle   ·   6-9  colore   ·   C  confronta   ·   B  raffica   ·   Ctrl+← →  raffica prec./succ.   ·   " +
                           "Z o clic  zoom 100%   ·   H  istogramma   ·   J  bruciati   ·   Canc  Cestino   ·   Ctrl+Z  ripristina   ·   Invio  fine",
                    Foreground = Res("TextDimBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 10, 0),
                },
            };
            Grid.SetRow(hints, 2);
            root.Children.Add(hints);

            // ---- "Finished" overlay
            _overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(_overlay, 3);
            var card = new StackPanel { Width = 560 };
            _overlayTitle = new TextBlock { FontSize = 20, FontWeight = FontWeights.Light, Foreground = Brushes.White };
            _overlaySummary = new TextBlock { FontSize = 14, Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap };
            card.Children.Add(_overlayTitle);
            card.Children.Add(_overlaySummary);
            card.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 0, 0, 10),
                Text = "Le foto verranno aperte una alla volta nella cartella di modifica, in basso nella finestra principale. " +
                       "Scegli dove salvare le foto modificate (gli originali non vengono mai sovrascritti):",
            });
            var outRow = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            var browse = new Button { Content = "Sfoglia...", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
            browse.Click += (s, e) => BrowseOutput();
            DockPanel.SetDock(browse, Dock.Right);
            outRow.Children.Add(browse);
            _output = new TextBox { Height = 26, Text = _state.Batch?.OutputFolder ?? Path.Combine(folder, "Modificate") };
            outRow.Children.Add(_output);
            card.Children.Add(outRow);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var back = new Button { Content = "Continua la preselezione", Padding = new Thickness(14, 5, 14, 5) };
            back.Click += (s, e) => HideFinish();
            _openButton = new Button
            {
                Content = "Apri per la modifica", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(8, 0, 0, 0),
                Background = Res("AccentBrush"), BorderBrush = Res("AccentBrush"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            };
            _openButton.Click += (s, e) => ConfirmFinish();
            buttons.Children.Add(back);
            buttons.Children.Add(_openButton);
            card.Children.Add(buttons);
            _overlay.Children.Add(new Border
            {
                Child = card, Background = Res("PanelBg"), BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                BorderThickness = new Thickness(1), Padding = new Thickness(26, 22, 26, 22),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            root.Children.Add(_overlay);

            Content = root;
            PreviewKeyDown += OnKey;
            Closing += OnClosing;
            Closed += (s, e) => { _closed = true; _thumbs.Stop(); _metaCts.Cancel(); };
            Loaded += (s, e) =>
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _previewSize = (int)Math.Clamp(Math.Max(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX, SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY), 1200, 3200);
                // Resume from the last photo viewed in this folder.
                var last = _state.LastPhoto == null ? null : _items.FirstOrDefault(p => string.Equals(p.Name, _state.LastPhoto, StringComparison.OrdinalIgnoreCase));
                if (last != null) _index = _items.IndexOf(last);
                _thumbs.Focus = _index;
                _thumbs.Kick();
                ShowCurrent();
                if (last != null && _index > 0) Toast($"Riprendi da dove eri rimasto: {last.Name}");
                LoadAllMetadata();
            };
        }

        static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        PhotoItem Current => _items.Count > 0 ? _items[Math.Clamp(_index, 0, _items.Count - 1)] : null;

        // ================= Filter =================

        bool FilterActive => _ratingFilter.SelectedIndex > 0 || _labelFilter.SelectedIndex > 0;

        bool Passes(PhotoItem p)
        {
            if (p.Rating < _ratingFilter.SelectedIndex) return false;
            int l = _labelFilter.SelectedIndex;
            if (l == 0) return true;
            return l == 6 ? p.Label == PhotoLabel.None : p.Label == (PhotoLabel)l;
        }

        string FilterDescription()
        {
            var parts = new List<string>();
            if (_ratingFilter.SelectedIndex > 0) parts.Add((string)_ratingFilter.SelectedItem);
            if (_labelFilter.SelectedIndex > 0) parts.Add("etichetta " + ((string)_labelFilter.SelectedItem).ToLowerInvariant());
            return string.Join(", ", parts);
        }

        /// <summary>Rebuilds the visible list from the filter, keeping (or getting close to) the given photo.</summary>
        void RebuildView(PhotoItem keep)
        {
            if (_list == null) return;
            var target = keep ?? Current;
            int targetAll = target == null ? 0 : _all.IndexOf(target);
            _syncing = true;
            _items.Clear();
            foreach (var p in _all) if (Passes(p)) _items.Add(p);
            _syncing = false;
            int i = target != null ? _items.IndexOf(target) : -1;
            if (i < 0)
            {
                i = 0;
                while (i < _items.Count && _all.IndexOf(_items[i]) < targetAll) i++;
                i = Math.Min(i, Math.Max(0, _items.Count - 1));
            }
            _index = i;
            _thumbs.Kick();
            ShowCurrent();
            if (FilterActive) Toast(_items.Count == 0 ? "Nessuna foto corrisponde al filtro." : $"{_items.Count} foto con {FilterDescription()}.");
        }

        // ================= Navigation =================

        void OnKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if (_overlay.Visibility == Visibility.Visible)
            {
                if (key == Key.Enter) { e.Handled = true; ConfirmFinish(); }
                else if (key == Key.Escape) { e.Handled = true; HideFinish(); }
                return;
            }
            if (Keyboard.FocusedElement is TextBox) return;

            bool handled = true;
            switch (key)
            {
                case Key.Right when mods == ModifierKeys.Control: JumpBurst(1); break;
                case Key.Left when mods == ModifierKeys.Control: JumpBurst(-1); break;
                case Key.Right: case Key.Down: case Key.PageDown: case Key.Space: Move(1); break;
                case Key.Left: case Key.Up: case Key.PageUp: Move(-1); break;
                case Key.Home: GoTo(0); break;
                case Key.End: GoTo(_items.Count - 1); break;
                case Key.Delete: DeleteCurrent(); break;
                case Key.Z when mods == ModifierKeys.Control: UndoDelete(); break;
                case Key.Z when mods == ModifierKeys.None: ToggleZoom(null); break;
                case >= Key.D0 and <= Key.D5 when mods == ModifierKeys.None: SetRating(key - Key.D0); break;
                case >= Key.NumPad0 and <= Key.NumPad5 when mods == ModifierKeys.None: SetRating(key - Key.NumPad0); break;
                case >= Key.D6 and <= Key.D9 when mods == ModifierKeys.None: ToggleLabel((PhotoLabel)(key - Key.D6 + 1)); break;
                case >= Key.NumPad6 and <= Key.NumPad9 when mods == ModifierKeys.None: ToggleLabel((PhotoLabel)(key - Key.NumPad6 + 1)); break;
                case Key.C when mods == ModifierKeys.None: CycleCompare(); break;
                case Key.B when mods == ModifierKeys.None: CompareBurst(); break;
                case Key.H when mods == ModifierKeys.None:
                    _showHistogram = !_showHistogram;
                    UpdateHistogram(Current);
                    break;
                case Key.J when mods == ModifierKeys.None:
                    _showClipping = !_showClipping;
                    foreach (var p in _panes) UpdateClipping(p);
                    Toast(_showClipping ? "Avviso bruciati attivo: rosso = luci bruciate, blu = ombre chiuse (J per nasconderlo)" : "Avviso bruciati nascosto.");
                    break;
                case Key.Enter: ShowFinish(false); break;
                case Key.Escape:
                    if (_zoom) ToggleZoom(null);
                    else if (_paneCount > 1) SetCompare(1);
                    else Close();
                    break;
                default: handled = false; break;
            }
            e.Handled = handled;
        }

        void Move(int delta)
        {
            if (_items.Count == 0) return;
            int next = _index + delta;
            if (next >= _items.Count) { ShowFinish(true); return; }
            GoTo(Math.Max(0, next));
        }

        void GoTo(int index)
        {
            if (_items.Count == 0) return;
            index = Math.Clamp(index, 0, _items.Count - 1);
            if (index == _index && _list.SelectedIndex == index) return;
            _index = index;
            ShowCurrent();
        }

        /// <summary>Next / previous photo that is not part of the current burst.</summary>
        void JumpBurst(int dir)
        {
            var item = Current;
            if (item == null) return;
            if (!_burstsReady) Toast("Sto ancora leggendo gli orari di scatto per trovare le raffiche...");
            int i = _index;
            if (dir > 0)
            {
                if (item.BurstId != 0) while (i < _items.Count && _items[i].BurstId == item.BurstId) i++;
                else i++;
                if (i >= _items.Count) { ShowFinish(true); return; }
            }
            else
            {
                i--;
                if (i < 0) return;
                int id = _items[i].BurstId;
                if (id != 0) while (i > 0 && _items[i - 1].BurstId == id) i--;
            }
            GoTo(i);
        }

        void ShowCurrent()
        {
            var item = Current;
            _empty.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
            _empty.Text = _all.Count == 0 ? "Nessuna foto rimasta in questa cartella." : "Nessuna foto corrisponde al filtro.";
            if (item == null)
            {
                _zoom = false;
                foreach (var p in _panes) { p.Item = null; p.Frame.Visibility = Visibility.Collapsed; }
                _fileName.Text = _fileInfo.Text = _exifInfo.Text = "";
                _labelDot.Visibility = Visibility.Collapsed;
                UpdateCounter();
                UpdateHistogram(null);
                return;
            }
            _index = _items.IndexOf(item);
            UpdateCounter();
            _syncing = true;
            _list.SelectedIndex = _index;
            _syncing = false;
            _list.ScrollIntoView(item);
            _thumbs.Focus = _index;
            if (_zoom) CaptureAnchor();

            int n = Math.Max(1, Math.Min(_paneCount, _items.Count));
            if (_index < _base) _base = _index;
            if (_index >= _base + n) _base = _index - n + 1;
            _base = Math.Clamp(_base, 0, _items.Count - n);
            EnsurePanes(n);
            for (int i = 0; i < n; i++)
            {
                _panes[i].Item = _items[_base + i];
                RenderPane(_panes[i]);
            }
            UpdateInfo(item);
            UpdateHistogram(item);
            foreach (int d in new[] { n, n + 1, -1 })
                if (_base + d >= 0 && _base + d < _items.Count) RequestPreview(_items[_base + d]);
            if (n == 1 && _index + 2 < _items.Count) RequestPreview(_items[_index + 2]);
            RequestMetadata(item);
        }

        void UpdateCounter()
        {
            string text = _items.Count == 0 ? "nessuna foto" : $"Foto {_index + 1} di {_items.Count}";
            if (FilterActive) text += $" (filtro: {FilterDescription()} · {_all.Count} in totale)";
            if (_deletedCount > 0) text += $"     ·     {_deletedCount} nel Cestino";
            _counter.Text = text;
            _compareButton.Content = _paneCount == 1 ? "Confronta" : $"Confronto: {_paneCount}";
        }

        void UpdateInfo(PhotoItem item)
        {
            _fileName.Text = item.Name + (item.Rating > 0 ? "    " + item.RatingText : "");
            _labelDot.Background = item.LabelBrush;
            _labelDot.Visibility = item.Label == PhotoLabel.None ? Visibility.Collapsed : Visibility.Visible;
            var parts = new List<string> { item.Badge, $"{_index + 1} di {_items.Count}" };
            if (item.BurstCount > 1) parts.Add($"raffica: foto {item.BurstIndex} di {item.BurstCount}");
            _fileInfo.Text = string.Join("   ·   ", parts);
            _exifInfo.Text = item.Metadata?.Summary() ?? "";
            _exifInfo.Visibility = _exifInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in _panes) UpdateCaption(p);
        }

        // ================= Stars and labels =================

        void SetRating(int stars)
        {
            var item = Current;
            if (item == null) return;
            item.Rating = stars;
            _state.Remember(item);
            _state.Save();
            UpdateInfo(item);
            Toast(stars == 0 ? $"{item.Name}: nessuna stella" : $"{item.Name}: {item.RatingText}");
        }

        void ToggleLabel(PhotoLabel label)
        {
            var item = Current;
            if (item == null) return;
            item.Label = item.Label == label ? PhotoLabel.None : label;
            _state.Remember(item);
            _state.Save();
            UpdateInfo(item);
            Toast(item.Label == PhotoLabel.None ? $"{item.Name}: etichetta rimossa" : $"{item.Name}: etichetta {LabelColors.Names[(int)item.Label].ToLowerInvariant()}");
        }

        // ================= Panes / compare =================

        void CycleCompare() => SetCompare(_paneCount == 1 ? 2 : _paneCount == 2 ? 4 : 1);

        void SetCompare(int count)
        {
            _paneCount = count;
            if (count > 1) _base = _index;   // the current photo and the next ones
            ShowCurrent();
            if (count > 1) Toast($"Confronto: {count} foto affiancate. Clic su una foto per sceglierla, Z per lo zoom sincronizzato, Esc per uscire.");
        }

        void CompareBurst()
        {
            var item = Current;
            if (item == null) return;
            if (!_burstsReady) { Toast("Sto ancora leggendo gli orari di scatto per trovare le raffiche..."); return; }
            if (item.BurstCount < 2) { Toast("Questa foto non fa parte di una raffica."); return; }
            var burst = _items.Where(p => p.BurstId == item.BurstId).ToList();
            if (burst.Count < 2) { Toast("Con il filtro attuale in questa raffica c'è una sola foto."); return; }
            _paneCount = Math.Clamp(burst.Count, 2, 4);
            _base = _items.IndexOf(burst[0]);
            ShowCurrent();
            Toast(burst.Count > 4
                ? $"Raffica di {burst.Count} foto: le confronti 4 alla volta, con le frecce scorri. Canc elimina quella evidenziata."
                : $"Raffica di {burst.Count} foto affiancate. Clic per scegliere, Canc elimina quella evidenziata.");
        }

        void EnsurePanes(int n)
        {
            if (_panes.Count == n) return;
            foreach (var p in _panes) p.Full = null;
            _panes.Clear();
            _paneGrid.Children.Clear();
            _paneGrid.Columns = n == 4 ? 2 : n;
            _paneGrid.Rows = n == 4 ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                var p = CreatePane(n > 1);
                _panes.Add(p);
                _paneGrid.Children.Add(p.Frame);
            }
        }

        Pane CreatePane(bool compare)
        {
            var p = new Pane();
            p.Image = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(p.Image, BitmapScalingMode.HighQuality);
            p.Clipping = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(p.Clipping, BitmapScalingMode.NearestNeighbor);
            p.Host = new Grid { Children = { p.Image, p.Clipping } };
            p.Viewer = new ScrollViewer
            {
                Content = p.Host, Focusable = false, Cursor = Cursors.Hand,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            p.Viewer.PreviewMouseLeftButtonDown += (s, e) => Viewer_MouseDown(p, e);
            p.Viewer.PreviewMouseMove += (s, e) => Viewer_MouseMove(p, e);
            p.Viewer.PreviewMouseLeftButtonUp += (s, e) => Viewer_MouseUp(p, e);
            p.Viewer.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true;
                if (_zoom)
                {
                    double v = p.Viewer.VerticalOffset - e.Delta;
                    p.Viewer.ScrollToVerticalOffset(v);
                    SyncScroll(p, p.Viewer.HorizontalOffset, v);
                }
                else Move(e.Delta < 0 ? 1 : -1);
            };
            p.Caption = new TextBlock { Foreground = Brushes.White };
            p.CaptionBox = new Border
            {
                Child = p.Caption, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(8), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Visibility = compare ? Visibility.Visible : Visibility.Collapsed,
            };
            p.Loading = new TextBlock
            {
                Text = "Caricamento...", Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 14, 0, 0), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
            };
            p.Frame = new Border
            {
                Child = new Grid { Children = { p.Viewer, p.CaptionBox, p.Loading } },
                BorderThickness = new Thickness(compare ? 2 : 0), BorderBrush = Brushes.Transparent, Margin = new Thickness(compare ? 2 : 0),
            };
            SetFit(p);
            return p;
        }

        void RenderPane(Pane p)
        {
            var item = p.Item;
            p.Frame.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            if (item == null) return;
            p.Frame.BorderBrush = _panes.Count > 1 && item == Current ? Res("AccentBrush") : Brushes.Transparent;
            UpdateCaption(p);
            UpdateClipping(p);
            if (_zoom)
            {
                ShowFull(p);
                return;
            }
            SetFit(p);
            if (_cache.TryGetValue(item, out var bmp))
            {
                Touch(item);
                p.Image.Source = bmp;
                p.Loading.Visibility = Visibility.Collapsed;
            }
            else
            {
                p.Image.Source = item.Thumbnail;   // blurry placeholder while the large preview loads
                p.Loading.Text = "Caricamento...";
                p.Loading.Visibility = Visibility.Visible;
                RequestPreview(item);
            }
        }

        void UpdateCaption(Pane p)
        {
            if (p.Item == null || _panes.Count < 2) return;
            p.Caption.Inlines.Clear();
            if (p.Item.Label != PhotoLabel.None) p.Caption.Inlines.Add(new Run("● ") { Foreground = p.Item.LabelBrush });
            p.Caption.Inlines.Add(new Run(p.Item.Name));
            if (p.Item.Rating > 0) p.Caption.Inlines.Add(new Run("   " + p.Item.RatingText) { Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)) });
        }

        void UpdateClipping(Pane p) =>
            p.Clipping.Source = _showClipping && p.Item != null && _analysis.TryGetValue(p.Item, out var a) ? a.Overlay : null;

        void SetFit(Pane p)
        {
            p.Viewer.HorizontalScrollBarVisibility = p.Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            p.Viewer.Cursor = Cursors.Hand;
            p.Host.Width = p.Host.Height = double.NaN;
            p.Host.Margin = new Thickness(_panes.Count > 1 ? 6 : 20);
            p.Image.Stretch = p.Clipping.Stretch = Stretch.Uniform;
            p.Full = null;
            p.FullItem = null;
        }

        // ================= Previews =================

        async void RequestPreview(PhotoItem item)
        {
            if (_cache.ContainsKey(item) || !_pending.Add(item)) return;
            BitmapSource bmp = null;
            PhotoAnalysis analysis = null;
            string error = null;
            int size = _previewSize;
            try
            {
                (bmp, analysis) = await Task.Run(() =>
                {
                    var b = PhotoLibrary.LoadPreview(item.PreviewPath, size);
                    PhotoAnalysis a = null;
                    try { a = PhotoLibrary.Analyze(b); } catch { }
                    return (b, a);
                });
            }
            catch (Exception ex) { error = ex.Message; }
            finally { _pending.Remove(item); }
            if (_closed) return;
            if (bmp != null)
            {
                _cache[item] = bmp;
                if (analysis != null) _analysis[item] = analysis;
                Touch(item);
                Evict();
                if (item.Thumbnail == null) item.Thumbnail = bmp;
            }
            foreach (var p in _panes.Where(p => p.Item == item))
            {
                if (bmp == null) { p.Loading.Text = "Impossibile leggere questa foto: " + error; p.Loading.Visibility = Visibility.Visible; continue; }
                UpdateClipping(p);
                if (_zoom) continue;
                p.Image.Source = bmp;
                p.Loading.Visibility = Visibility.Collapsed;
            }
            if (item == Current) UpdateHistogram(item);
        }

        void Touch(PhotoItem item)
        {
            _lru.Remove(item);
            _lru.AddFirst(item);
        }

        void Evict()
        {
            var node = _lru.Last;
            while (_lru.Count > CacheSize && node != null)
            {
                var prev = node.Previous;
                var old = node.Value;
                if (!_panes.Any(p => p.Item == old))
                {
                    _lru.Remove(node);
                    _cache.Remove(old);
                    _analysis.Remove(old);
                }
                node = prev;
            }
        }

        void Forget(PhotoItem item)
        {
            _cache.Remove(item);
            _analysis.Remove(item);
            _lru.Remove(item);
            foreach (var p in _panes.Where(p => p.FullItem == item)) { p.Full = null; p.FullItem = null; }
        }

        // ================= Histogram =================

        void UpdateHistogram(PhotoItem item)
        {
            _histBox.Visibility = _showHistogram && item != null ? Visibility.Visible : Visibility.Collapsed;
            if (item == null || !_showHistogram) return;
            _histText.Inlines.Clear();
            if (!_analysis.TryGetValue(item, out var a))
            {
                _hR.Points = _hG.Points = _hB.Points = null;
                _histText.Inlines.Add(new Run("Istogramma in caricamento...") { Foreground = Res("TextDimBrush") });
                return;
            }
            int max = 1;
            for (int i = 1; i < 255; i++) max = Math.Max(max, Math.Max(a.R[i], Math.Max(a.G[i], a.B[i])));
            _hR.Points = Poly(a.R, 96.0 / max);
            _hG.Points = Poly(a.G, 96.0 / max);
            _hB.Points = Poly(a.B, 96.0 / max);
            var ok = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
            _histText.Inlines.Add(new Run("Luci bruciate " + Percent(a.Highlights)) { Foreground = a.Highlights >= 0.005 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)) : ok });
            _histText.Inlines.Add(new Run("     "));
            _histText.Inlines.Add(new Run("Ombre chiuse " + Percent(a.Shadows)) { Foreground = a.Shadows >= 0.005 ? new SolidColorBrush(Color.FromRgb(0x6B, 0xA8, 0xFF)) : ok });
            if (!_showClipping && (a.Highlights >= 0.005 || a.Shadows >= 0.005))
                _histText.Inlines.Add(new Run("\nJ per vederle sulla foto") { Foreground = Res("TextDimBrush") });
        }

        static string Percent(double f) => (f * 100).ToString(f < 0.1 ? "0.0" : "0", CultureInfo.CurrentCulture) + " %";

        static PointCollection Poly(int[] h, double sc)
        {
            var pc = new PointCollection(260) { new Point(0, 100) };
            for (int i = 0; i < 256; i++) pc.Add(new Point(i, 100 - Math.Min(100, h[i] * sc)));
            pc.Add(new Point(255, 100));
            pc.Freeze();
            return pc;
        }

        // ================= Shooting data and bursts =================

        async void RequestMetadata(PhotoItem item)
        {
            if (item.MetadataRequested) return;
            item.MetadataRequested = true;
            PhotoMetadata md = null;
            try { md = await Task.Run(() => PhotoLibrary.ReadMetadata(item.PreviewPath)); } catch { }
            if (_closed) return;
            SetMetadata(item, md);
        }

        void SetMetadata(PhotoItem item, PhotoMetadata md)
        {
            item.Metadata = md;
            item.CaptureTime = md?.DateTaken;
            if (item == Current) UpdateInfo(item);
        }

        /// <summary>Reads the shooting data of every photo in the background, then finds the bursts.</summary>
        async void LoadAllMetadata()
        {
            // Also the photos being read on demand: the bursts need every capture time.
            var todo = _all.Where(p => p.Metadata == null).ToList();
            foreach (var p in todo) p.MetadataRequested = true;
            var token = _metaCts.Token;
            var results = new ConcurrentDictionary<PhotoItem, PhotoMetadata>();
            try
            {
                await Task.Run(() => Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, p =>
                {
                    results[p] = PhotoLibrary.ReadMetadata(p.PreviewPath);
                }), token);
            }
            catch (OperationCanceledException) { return; }
            catch { }
            if (_closed) return;
            foreach (var kv in results) SetMetadata(kv.Key, kv.Value);
            PhotoLibrary.MarkBursts(_all);
            _burstsReady = true;
            if (Current != null) UpdateInfo(Current);
        }

        // ================= Zoom 100% (synchronised between panes) =================

        void ToggleZoom(Point? anchor)
        {
            if (_zoom) { ExitZoom(); return; }
            if (Current == null) return;
            _zoom = true;
            _zoomAnchor = anchor ?? new Point(0.5, 0.5);
            foreach (var p in _panes.Where(p => p.Item != null)) ShowFull(p);
        }

        void ExitZoom()
        {
            _zoom = false;
            foreach (var p in _panes) SetFit(p);
            if (Current != null) ShowCurrent();
        }

        /// <summary>Keeps the same relative position when moving to the next photo (e.g. of a burst).</summary>
        void CaptureAnchor()
        {
            var p = _panes.FirstOrDefault(x => x.Full != null && x.Host.Width > 0 && !double.IsNaN(x.Host.Width));
            if (p == null) return;
            _zoomAnchor = new Point(
                Math.Clamp((p.Viewer.HorizontalOffset + p.Viewer.ViewportWidth / 2) / p.Host.Width, 0, 1),
                Math.Clamp((p.Viewer.VerticalOffset + p.Viewer.ViewportHeight / 2) / p.Host.Height, 0, 1));
        }

        async void ShowFull(Pane p)
        {
            var item = p.Item;
            if (item == null) return;
            if (p.FullItem != item)
            {
                p.Loading.Text = "Caricamento a piena risoluzione...";
                p.Loading.Visibility = Visibility.Visible;
                var shared = _panes.FirstOrDefault(x => x != p && x.FullItem == item);
                BitmapSource bmp = shared?.Full;
                string error = null;
                if (bmp == null)
                {
                    try { bmp = await Task.Run(() => PhotoLibrary.LoadPreview(item.PreviewPath, 0)); }
                    catch (Exception ex) { error = ex.Message; }
                }
                if (_closed || !_zoom || p.Item != item) return;
                if (bmp == null)
                {
                    p.Loading.Text = "Impossibile leggere questa foto: " + error;
                    return;
                }
                p.Full = bmp;
                p.FullItem = item;
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            p.Image.Source = p.Full;
            p.Image.Stretch = p.Clipping.Stretch = Stretch.Fill;
            UpdateClipping(p);
            p.Host.Margin = new Thickness(0);
            p.Host.Width = p.Full.PixelWidth / dpi.DpiScaleX;
            p.Host.Height = p.Full.PixelHeight / dpi.DpiScaleY;
            var bars = _panes.Count == 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            p.Viewer.HorizontalScrollBarVisibility = p.Viewer.VerticalScrollBarVisibility = bars;
            p.Viewer.Cursor = Cursors.SizeAll;
            p.Viewer.UpdateLayout();
            p.Viewer.ScrollToHorizontalOffset(_zoomAnchor.X * p.Host.Width - p.Viewer.ViewportWidth / 2);
            p.Viewer.ScrollToVerticalOffset(_zoomAnchor.Y * p.Host.Height - p.Viewer.ViewportHeight / 2);
            p.Loading.Visibility = Visibility.Collapsed;
        }

        /// <summary>Moves the other zoomed panes to the same relative point.</summary>
        void SyncScroll(Pane source, double h, double v)
        {
            if (source.Full == null || double.IsNaN(source.Host.Width) || source.Host.Width <= 0) return;
            _zoomAnchor = new Point(
                Math.Clamp((h + source.Viewer.ViewportWidth / 2) / source.Host.Width, 0, 1),
                Math.Clamp((v + source.Viewer.ViewportHeight / 2) / source.Host.Height, 0, 1));
            foreach (var p in _panes)
            {
                if (p == source || p.Full == null || double.IsNaN(p.Host.Width)) continue;
                p.Viewer.ScrollToHorizontalOffset(_zoomAnchor.X * p.Host.Width - p.Viewer.ViewportWidth / 2);
                p.Viewer.ScrollToVerticalOffset(_zoomAnchor.Y * p.Host.Height - p.Viewer.ViewportHeight / 2);
            }
        }

        void Viewer_MouseDown(Pane p, MouseButtonEventArgs e)
        {
            _dragPane = p;
            _dragStart = e.GetPosition(p.Viewer);
            _dragOffset = new Vector(p.Viewer.HorizontalOffset, p.Viewer.VerticalOffset);
            _dragging = true;
            _dragMoved = false;
            p.Viewer.CaptureMouse();
            e.Handled = true;
        }

        void Viewer_MouseMove(Pane p, MouseEventArgs e)
        {
            if (!_dragging || _dragPane != p) return;
            var d = e.GetPosition(p.Viewer) - _dragStart;
            if (Math.Abs(d.X) + Math.Abs(d.Y) > 4) _dragMoved = true;
            if (_zoom && _dragMoved)
            {
                double h = _dragOffset.X - d.X, v = _dragOffset.Y - d.Y;
                p.Viewer.ScrollToHorizontalOffset(h);
                p.Viewer.ScrollToVerticalOffset(v);
                SyncScroll(p, Math.Clamp(h, 0, Math.Max(0, p.Host.Width - p.Viewer.ViewportWidth)), Math.Clamp(v, 0, Math.Max(0, p.Host.Height - p.Viewer.ViewportHeight)));
            }
        }

        void Viewer_MouseUp(Pane p, MouseButtonEventArgs e)
        {
            if (!_dragging || _dragPane != p) return;
            _dragging = false;
            p.Viewer.ReleaseMouseCapture();
            e.Handled = true;
            if (_dragMoved || p.Item == null) return;
            if (p.Item != Current)
            {
                // Comparing: a click chooses the photo (stars, labels and Canc act on it).
                _index = _items.IndexOf(p.Item);
                ShowCurrent();
                return;
            }
            if (_zoom) { ExitZoom(); return; }
            var pos = e.GetPosition(p.Image);
            if (p.Image.ActualWidth <= 0 || pos.X < 0 || pos.Y < 0 || pos.X > p.Image.ActualWidth || pos.Y > p.Image.ActualHeight) return;
            // Image.Stretch=Uniform: the picture fills the element's box exactly along one axis and is centred.
            if (p.Image.Source is BitmapSource src)
            {
                double scale = Math.Min(p.Image.ActualWidth / src.Width, p.Image.ActualHeight / src.Height);
                double pw = src.Width * scale, ph = src.Height * scale;
                double ox = (p.Image.ActualWidth - pw) / 2, oy = (p.Image.ActualHeight - ph) / 2;
                ToggleZoom(new Point(Math.Clamp((pos.X - ox) / pw, 0, 1), Math.Clamp((pos.Y - oy) / ph, 0, 1)));
            }
        }

        // ================= Delete / restore =================

        void DeleteCurrent()
        {
            var item = Current;
            if (item == null) return;
            bool moved;
            try
            {
                moved = PhotoLibrary.MoveToRecycleBin(item.Files, new WindowInteropHelper(this).Handle);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossibile spostare la foto nel Cestino:\n" + ex.Message, "Preselezione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!moved) return;
            int allIndex = _all.IndexOf(item);
            _deleted.Push((item, allIndex));
            _deletedCount++;
            _all.RemoveAt(allIndex);
            Forget(item);
            int index = _items.IndexOf(item);
            _syncing = true;
            _items.RemoveAt(index);
            _syncing = false;
            _index = Math.Min(index, _items.Count - 1);
            if (_burstsReady) PhotoLibrary.MarkBursts(_all);
            ShowCurrent();
            string files = item.Files.Count > 1 ? $" ({item.Badge})" : "";
            Toast($"{item.Name}{files} spostata nel Cestino.   Ctrl+Z per ripristinarla");
        }

        void UndoDelete()
        {
            if (_deleted.Count == 0) { Toast("Nessuna foto da ripristinare."); return; }
            var (item, index) = _deleted.Peek();
            var failed = new List<string>();
            foreach (var f in item.Files)
            {
                try { if (!PhotoLibrary.RestoreFromRecycleBin(f)) failed.Add(f); }
                catch { failed.Add(f); }
            }
            if (!File.Exists(item.EditPath))
            {
                MessageBox.Show(this, $"Non riesco a ripristinare \"{item.Name}\": puoi recuperarla dal Cestino di Windows.",
                    "Preselezione", MessageBoxButton.OK, MessageBoxImage.Information);
                _deleted.Pop();
                return;
            }
            _deleted.Pop();
            _deletedCount--;
            _all.Insert(Math.Clamp(index, 0, _all.Count), item);
            if (_burstsReady) PhotoLibrary.MarkBursts(_all);
            RebuildView(item);
            Toast(failed.Count == 0 ? $"{item.Name} ripristinata." : $"{item.Name} ripristinata solo in parte: recupera {Path.GetFileName(failed[0])} dal Cestino.");
        }

        void Toast(string text)
        {
            _toast.Text = text;
            _toastBox.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        // ================= Finish =================

        void ShowFinish(bool reachedEnd)
        {
            if (_items.Count == 0)
            {
                if (_all.Count > 0) { Toast("Nessuna foto corrisponde al filtro: cambialo per scegliere quali foto modificare."); return; }
                if (MessageBox.Show(this, "Non è rimasta nessuna foto da modificare. Chiudere la preselezione?", "Preselezione",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    DialogResult = false;
                }
                return;
            }
            if (_zoom) ExitZoom();
            _overlayTitle.Text = reachedEnd ? "Hai visto tutte le foto" : "Fine della preselezione";
            string summary = $"{_items.Count} foto da modificare";
            if (FilterActive) summary += $" (solo {FilterDescription()}, su {_all.Count})";
            summary += _deletedCount == 1 ? "   ·   1 spostata nel Cestino" : _deletedCount > 1 ? $"   ·   {_deletedCount} spostate nel Cestino" : "";
            _overlaySummary.Text = summary;
            _overlay.Visibility = Visibility.Visible;
            _openButton.Focus();
        }

        void HideFinish()
        {
            _overlay.Visibility = Visibility.Collapsed;
            Focus();
        }

        void BrowseOutput()
        {
            var dlg = new OpenFolderDialog { Title = "Dove salvare le foto modificate" };
            string current = _output.Text.Trim();
            dlg.InitialDirectory = Directory.Exists(current) ? current : _folder;
            if (dlg.ShowDialog(this) == true) _output.Text = dlg.FolderName;
        }

        void ConfirmFinish()
        {
            string path = _output.Text.Trim();
            try
            {
                if (path.Length == 0 || !Path.IsPathRooted(path)) throw new ArgumentException();
                path = Path.GetFullPath(path);
            }
            catch
            {
                MessageBox.Show(this, "Indica una cartella valida in cui salvare le foto modificate (es. C:\\Foto\\Modificate).",
                    "Preselezione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            OutputFolder = path;
            DialogResult = true;
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (DialogResult != true && _items.Count > 0 &&
                MessageBox.Show(this,
                    "Uscire dalla preselezione senza aprire le foto per la modifica?\n\nStelle ed etichette restano salvate; le foto già eliminate restano nel Cestino di Windows.",
                    "Preselezione", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _state.LastPhoto = Current?.Name;
            _state.Save();
        }
    }
}
