using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    public partial class MainWindow
    {
        enum Tool { Move, RectSelect, EllipseSelect, Lasso, Wand, Crop, Eyedropper, Brush, Clone, Eraser, Bucket, Gradient, Dodge, Text, Shape, Hand, Zoom }

        static readonly Dictionary<Tool, string> ToolNames = new Dictionary<Tool, string>
        {
            [Tool.Move] = T("Sposta"), [Tool.RectSelect] = T("Selezione rettangolare"), [Tool.EllipseSelect] = T("Selezione ellittica"),
            [Tool.Lasso] = T("Lazo"), [Tool.Wand] = T("Bacchetta magica"), [Tool.Crop] = T("Taglierina"), [Tool.Eyedropper] = T("Contagocce"),
            [Tool.Brush] = T("Pennello"), [Tool.Clone] = T("Timbro clone"), [Tool.Eraser] = T("Gomma"), [Tool.Bucket] = T("Secchiello"),
            [Tool.Gradient] = TC("strumento", "Sfumatura"), [Tool.Dodge] = T("Scherma / Brucia"), [Tool.Text] = T("Testo"), [Tool.Shape] = T("Forme"),
            [Tool.Hand] = T("Mano"), [Tool.Zoom] = T("Zoom"),
        };

        Tool _tool;
        bool _dragging, _panning, _mouseOverCanvas;
        Point _dragStart, _lastPos, _panStart;
        double _panH, _panV;
        SelectionOp _selOp;
        StrokeEngine _stroke;
        Point? _lastStrokeEnd, _cloneSource;
        Vector? _cloneOffset;
        Layer _moveLayer;
        byte[] _moveBase, _moveFloat, _moveHole, _moveShift;
        int _moveDx, _moveDy;
        List<Point> _lasso;
        Rect? _cropRect;
        string _lastText = T("Testo");

        bool IsBrushTool => _tool is Tool.Brush or Tool.Eraser or Tool.Clone or Tool.Dodge;

        // ================= Tool selection =================

        void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && Enum.TryParse(rb.Tag as string, out Tool t) && t != _tool) SelectTool(t);
        }

        static void Show(UIElement e, bool visible) => e.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        void SelectTool(Tool t)
        {
            if (_dragging) return;
            if (_tool == Tool.Crop && t != Tool.Crop) CancelCrop();
            _tool = t;
            foreach (var rb in ToolPanel.Children.OfType<RadioButton>())
                if ((string)rb.Tag == t.ToString() && rb.IsChecked != true) rb.IsChecked = true;

            ToolNameText.Text = ToolNames[t];
            Show(OptBrush, IsBrushTool);
            Show(DodgeMode, t == Tool.Dodge);
            BrushOpacityLabel.Text = t == Tool.Dodge ? T("Esposizione:") : T("Opacità:");
            Show(OptSelect, t is Tool.RectSelect or Tool.EllipseSelect or Tool.Lasso or Tool.Wand);
            Show(OptTolerance, t is Tool.Wand or Tool.Bucket);
            Show(SampleAll, t == Tool.Wand);
            Show(OptFillOpacity, t is Tool.Bucket or Tool.Gradient);
            Show(OptGradient, t == Tool.Gradient);
            Show(OptShape, t == Tool.Shape);
            Show(OptText, t == Tool.Text);
            Show(OptCrop, t == Tool.Crop);
            Show(OptZoom, t is Tool.Zoom or Tool.Hand);
            OptHint.Text = t switch
            {
                Tool.Move => T("Trascina per spostare il livello attivo (o i pixel selezionati)"),
                Tool.RectSelect or Tool.EllipseSelect or Tool.Lasso or Tool.Wand => T("Maiusc: aggiungi · Alt: sottrai"),
                Tool.Crop => T("Trascina l'area, poi Invio o doppio clic per applicare"),
                Tool.Eyedropper => T("Clic: primo piano · Alt+clic: sfondo"),
                Tool.Brush => T("Alt+clic: preleva colore · Maiusc+clic: linea retta · [ ]: dimensione"),
                Tool.Clone => T("Alt+clic per definire l'origine"),
                Tool.Eraser => T("Maiusc+clic: linea retta"),
                Tool.Gradient => T("Trascina per tracciare · Maiusc: angoli di 45°"),
                Tool.Text => T("Clic sull'immagine per inserire il testo"),
                Tool.Shape => T("Trascina per disegnare · Maiusc: proporzioni vincolate"),
                Tool.Hand => T("Trascina per scorrere (oppure tieni premuto Spazio)"),
                Tool.Zoom => T("Clic: zoom avanti · Alt+clic: zoom indietro"),
                _ => "",
            };
            CloneMarker.Visibility = Visibility.Collapsed;
            if (t == Tool.Clone && _cloneSource is Point src) ShowCloneMarker(src);
            UpdateCursor();
            UpdateBrushCursor(null);
        }

        bool _overScrollBar;

        void UpdateCursor()
        {
            if (CanvasArea == null) return;
            Cursor c;
            // No document: nothing draws a brush circle, so keep the normal arrow (a hidden pointer here looked like it vanished).
            if (S == null) c = null;
            else if (_spaceDown || _tool == Tool.Hand || _panning) c = _panning ? Cursors.ScrollAll : Cursors.Hand;
            else if (_tool == Tool.Move) c = Cursors.SizeAll;
            else if (_tool == Tool.Text) c = Cursors.IBeam;
            else if (IsBrushTool) c = BrushSize.Value * _zoom / _dpiScale >= 6 ? Cursors.None : Cursors.Cross;
            else c = Cursors.Cross;
            CanvasArea.Cursor = c;
        }

        void UpdateBrushCursor(Point? p)
        {
            if (BrushCursor == null) return;
            bool show = S != null && _mouseOverCanvas && !_overScrollBar && !_spaceDown && !_panning && IsBrushTool;
            if (!show)
            {
                BrushCursor.Visibility = BrushCursorOuter.Visibility = Visibility.Collapsed;
                return;
            }
            var pos = p ?? Mouse.GetPosition(CanvasImage);
            double d = BrushSize.Value, t = _dpiScale / _zoom;
            BrushCursor.Width = BrushCursor.Height = d;
            Canvas.SetLeft(BrushCursor, pos.X - d / 2);
            Canvas.SetTop(BrushCursor, pos.Y - d / 2);
            double d2 = d + 2 * t;
            BrushCursorOuter.Width = BrushCursorOuter.Height = d2;
            Canvas.SetLeft(BrushCursorOuter, pos.X - d2 / 2);
            Canvas.SetTop(BrushCursorOuter, pos.Y - d2 / 2);
            BrushCursor.Visibility = BrushCursorOuter.Visibility = Visibility.Visible;
        }

        void BrushSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateBrushCursor(null);
            UpdateCursor();
        }

        void ShowCloneMarker(Point p)
        {
            CloneMarkerPos.X = p.X;
            CloneMarkerPos.Y = p.Y;
            CloneMarker.Visibility = Visibility.Visible;
        }

        // ================= Mouse =================

        Point DocPos(MouseEventArgs e) => e.GetPosition(CanvasImage);

        static bool IsInScrollBar(object src)
        {
            for (var d = src as DependencyObject; d != null;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ScrollBar) return true;
            return false;
        }

        void CanvasArea_MouseEnter(object sender, MouseEventArgs e)
        {
            _mouseOverCanvas = true;
            UpdateBrushCursor(null);
        }

        void CanvasArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _mouseOverCanvas = false;
            UpdateBrushCursor(null);
            PosText.Text = ColorText.Text = "";
            PixelSwatch.Visibility = Visibility.Hidden;
        }

        void CanvasArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (S == null) return;
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt))
            {
                if (mods.HasFlag(ModifierKeys.Alt)) _altUsed = true;
                ZoomStep(e.Delta > 0 ? 1 : -1, e.GetPosition(Scroller));
                e.Handled = true;
            }
            else if (mods.HasFlag(ModifierKeys.Shift))
            {
                Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        void CanvasArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (S == null || IsInScrollBar(e.OriginalSource)) return;
            CanvasArea.Focus();
            var mods = Keyboard.Modifiers;
            bool alt = mods.HasFlag(ModifierKeys.Alt), shift = mods.HasFlag(ModifierKeys.Shift);
            if (alt) _altUsed = true;

            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && (_spaceDown || _tool == Tool.Hand)))
            {
                _panning = true;
                _panStart = e.GetPosition(Scroller);
                _panH = Scroller.HorizontalOffset;
                _panV = Scroller.VerticalOffset;
                CanvasArea.CaptureMouse();
                UpdateCursor();
                UpdateBrushCursor(null);
                e.Handled = true;
                return;
            }
            if (e.ChangedButton != MouseButton.Left || _dragging) return;
            e.Handled = true;

            var p = DocPos(e);
            _dragStart = _lastPos = p;
            var op = shift ? SelectionOp.Add : alt ? SelectionOp.Subtract : SelectionOp.Replace;

            switch (_tool)
            {
                case Tool.Zoom:
                    ZoomStep(alt ? -1 : 1, e.GetPosition(Scroller));
                    return;
                case Tool.Eyedropper:
                    PickColor(p, alt);
                    break;
                case Tool.Brush: case Tool.Eraser: case Tool.Clone: case Tool.Dodge:
                    if (!BeginStroke(p, alt, shift)) return;
                    break;
                case Tool.Move:
                    if (!BeginMove()) return;
                    break;
                case Tool.RectSelect: case Tool.EllipseSelect: case Tool.Lasso:
                    _selOp = op;
                    if (_tool == Tool.Lasso) _lasso = new List<Point> { p };
                    break;
                case Tool.Wand:
                    WandSelect(p, op);
                    return;
                case Tool.Crop:
                    if (e.ClickCount == 2 && _cropRect is Rect cr && cr.Contains(p)) { ApplyCrop(); return; }
                    _cropRect = null;
                    UpdateCropVisual();
                    break;
                case Tool.Bucket:
                    BucketFill(p);
                    return;
                case Tool.Gradient: case Tool.Shape:
                    break;
                case Tool.Text:
                    PlaceText(p);
                    return;
                default:
                    return;
            }
            _dragging = true;
            CanvasArea.CaptureMouse();
        }

        void CanvasArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (S == null) return;
            // The scroll bars show the normal arrow (see the Scroller style): hide the brush circle over them.
            _overScrollBar = !_dragging && !_panning && IsInScrollBar(e.OriginalSource);
            var p = DocPos(e);
            UpdatePointerStatus(p);
            if (_panning)
            {
                var cur = e.GetPosition(Scroller);
                Scroller.ScrollToHorizontalOffset(_panH - (cur.X - _panStart.X));
                Scroller.ScrollToVerticalOffset(_panV - (cur.Y - _panStart.Y));
                return;
            }
            UpdateBrushCursor(p);
            if (!_dragging) return;

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            switch (_tool)
            {
                case Tool.Eyedropper:
                    PickColor(p, alt);
                    break;
                case Tool.Brush: case Tool.Eraser: case Tool.Clone: case Tool.Dodge:
                    if (_stroke != null)
                    {
                        Recomposite(_stroke.LineTo(p));
                        _lastStrokeEnd = p;
                        if (_tool == Tool.Clone && _cloneOffset is Vector off) ShowCloneMarker(p + off);
                    }
                    break;
                case Tool.Move:
                    UpdateMove(p);
                    break;
                case Tool.RectSelect:
                    ShowPreview(new RectangleGeometry(DragRect(p, shift, true)));
                    break;
                case Tool.EllipseSelect:
                    ShowPreview(new EllipseGeometry(DragRect(p, shift, false)));
                    break;
                case Tool.Lasso:
                    if (_lasso != null && (p - _lasso[^1]).Length >= 1)
                    {
                        _lasso.Add(p);
                        ShowPreview(PolyGeometry(_lasso, false));
                    }
                    break;
                case Tool.Crop:
                    var r = Rect.Intersect(DragRect(p, shift, true), new Rect(0, 0, Doc.Width, Doc.Height));
                    _cropRect = r.IsEmpty ? null : r;
                    UpdateCropVisual();
                    break;
                case Tool.Gradient:
                    ShowPreview(new LineGeometry(_dragStart, shift ? SnapAngle(_dragStart, p) : p));
                    break;
                case Tool.Shape:
                    ShowShapePreview(p, shift);
                    break;
            }
            _lastPos = p;
        }

        void CanvasArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
            {
                _panning = false;
                CanvasArea.ReleaseMouseCapture();
                UpdateCursor();
                return;
            }
            if (!_dragging || e.ChangedButton != MouseButton.Left) return;
            FinishDrag(DocPos(e), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        void CanvasArea_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_panning) { _panning = false; UpdateCursor(); }
            if (_dragging) FinishDrag(_lastPos, false);
        }

        void FinishDrag(Point p, bool shift)
        {
            _dragging = false;
            if (CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
            if (S == null) return;
            switch (_tool)
            {
                case Tool.Brush: case Tool.Eraser: case Tool.Clone: case Tool.Dodge:
                    if (_stroke != null)
                    {
                        _stroke = null;
                        Commit(StrokeName());
                    }
                    if (_tool == Tool.Clone && _cloneSource is Point src) ShowCloneMarker(src);
                    break;
                case Tool.Move:
                    EndMove();
                    break;
                case Tool.RectSelect:
                {
                    ClearPreview();
                    var r = DragRect(p, shift && _selOp == SelectionOp.Replace, true);
                    if (r.Width < 1 || r.Height < 1) { if (_selOp == SelectionOp.Replace) Deselect(); }
                    else ApplySelection(Selection.FromRect(new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), Doc.Width, Doc.Height), _selOp);
                    break;
                }
                case Tool.EllipseSelect:
                {
                    ClearPreview();
                    var r = DragRect(p, shift && _selOp == SelectionOp.Replace, false);
                    if (r.Width < 1 || r.Height < 1) { if (_selOp == SelectionOp.Replace) Deselect(); }
                    else ApplySelection(Selection.FromGeometry(new EllipseGeometry(r), Doc.Width, Doc.Height), _selOp);
                    break;
                }
                case Tool.Lasso:
                    ClearPreview();
                    if (_lasso != null && _lasso.Count > 2)
                        ApplySelection(Selection.FromGeometry(PolyGeometry(_lasso, true), Doc.Width, Doc.Height), _selOp);
                    _lasso = null;
                    break;
                case Tool.Crop:
                    if (_cropRect is Rect cr && (cr.Width < 2 || cr.Height < 2)) CancelCrop();
                    else if (_cropRect != null) Status(T("Premi Invio (o doppio clic) per ritagliare, Esc per annullare."));
                    break;
                case Tool.Gradient:
                    ClearPreview();
                    ApplyGradient(shift ? SnapAngle(_dragStart, p) : p);
                    break;
                case Tool.Shape:
                    ShapePreview.Data = null;
                    DrawShape(_dragStart, p, shift);
                    break;
            }
        }

        /// <summary>Aborts any in-progress mouse operation (used when switching documents or undoing).</summary>
        void CancelInteraction()
        {
            if (_moveLayer != null && _moveBase != null) _moveLayer.Pixels = _moveBase;
            _moveLayer = null;
            _moveBase = _moveFloat = _moveHole = _moveShift = null;
            _dragging = false;
            _panning = false;
            _stroke = null;
            _lasso = null;
            if (CanvasArea != null && CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
            if (PreviewBlack != null) ClearPreview();
            if (ShapePreview != null) ShapePreview.Data = null;
            if (CropShade != null) CancelCrop();
        }

        void UpdatePointerStatus(Point p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= Doc.Width || y >= Doc.Height)
            {
                PosText.Text = ColorText.Text = "";
                PixelSwatch.Visibility = Visibility.Hidden;
                return;
            }
            int i = (y * Doc.Width + x) * 4;
            var c = Doc.Composite;
            PosText.Text = $"X: {x}   Y: {y}";
            ColorText.Text = $"R {c[i + 2]}   G {c[i + 1]}   B {c[i]}   A {c[i + 3]}";
            PixelSwatch.Background = new SolidColorBrush(Color.FromArgb(c[i + 3], c[i + 2], c[i + 1], c[i]));
            PixelSwatch.Visibility = Visibility.Visible;
        }

        // ================= Geometry helpers =================

        Rect DragRect(Point p, bool constrain, bool snap) => RectFrom(_dragStart, p, constrain, snap);

        static Rect RectFrom(Point a, Point b, bool constrain, bool snap)
        {
            double x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
            if (constrain)
            {
                double s = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                x1 = x0 + (x1 >= x0 ? s : -s);
                y1 = y0 + (y1 >= y0 ? s : -s);
            }
            if (snap)
            {
                x0 = Math.Round(x0); y0 = Math.Round(y0); x1 = Math.Round(x1); y1 = Math.Round(y1);
            }
            return new Rect(new Point(x0, y0), new Point(x1, y1));
        }

        static Point SnapAngle(Point a, Point b)
        {
            Vector v = b - a;
            double len = v.Length;
            if (len == 0) return b;
            double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
            return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
        }

        static Geometry PolyGeometry(List<Point> pts, bool closed)
        {
            var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(pts[0], true, closed);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, false);
            }
            sg.Freeze();
            return sg;
        }

        void ShowPreview(Geometry g) => PreviewBlack.Data = PreviewWhite.Data = g;
        void ClearPreview() => PreviewBlack.Data = PreviewWhite.Data = null;

        // ================= Tool operations =================

        void PickColor(Point p, bool secondary)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= Doc.Width || y >= Doc.Height) return;
            int i = (y * Doc.Width + x) * 4;
            var c = Doc.Composite;
            if (c[i + 3] == 0) return;
            var col = Color.FromRgb(c[i + 2], c[i + 1], c[i]);
            if (secondary) _secondary = col; else _primary = col;
            UpdateColorSwatches();
        }

        string StrokeName() => _tool switch
        {
            Tool.Eraser => T("Gomma"),
            Tool.Clone => T("Timbro clone"),
            Tool.Dodge => DodgeMode.SelectedIndex == 1 ? T("Brucia") : T("Scherma"),
            _ => T("Pennello"),
        };

        bool BeginStroke(Point p, bool alt, bool shift)
        {
            var layer = ActiveLayer;
            if (layer == null) return false;
            if (_tool == Tool.Brush && alt) { PickColor(p, false); return false; }
            if (_tool == Tool.Clone && alt)
            {
                _cloneSource = p;
                _cloneOffset = null;
                ShowCloneMarker(p);
                Status(T("Origine del timbro clone impostata."));
                return false;
            }
            if (!layer.Visible) { Status(T("Il livello attivo è nascosto: rendilo visibile per modificarlo.")); return false; }
            if (_tool == Tool.Clone && _cloneSource == null) { Status(T("Tieni premuto Alt e fai clic per definire l'origine del timbro clone.")); return false; }

            _stroke = new StrokeEngine(layer, S.Selection)
            {
                Size = BrushSize.Value,
                Hardness = BrushHardness.Value / 100,
                Opacity = BrushOpacity.Value / 100,
                Color = _primary,
                Mode = _tool switch
                {
                    Tool.Eraser => StrokeMode.Erase,
                    Tool.Clone => StrokeMode.Clone,
                    Tool.Dodge => DodgeMode.SelectedIndex == 1 ? StrokeMode.Burn : StrokeMode.Dodge,
                    _ => StrokeMode.Paint,
                },
            };
            if (_tool == Tool.Clone)
            {
                _cloneOffset ??= _cloneSource.Value - p;
                _stroke.CloneDx = (int)Math.Round(_cloneOffset.Value.X);
                _stroke.CloneDy = (int)Math.Round(_cloneOffset.Value.Y);
                ShowCloneMarker(p + _cloneOffset.Value);
            }
            Int32Rect r;
            if (shift && _lastStrokeEnd is Point last) r = ImageOps.Union(_stroke.Begin(last), _stroke.LineTo(p));
            else r = _stroke.Begin(p);
            _lastStrokeEnd = p;
            Recomposite(r);
            return true;
        }

        bool BeginMove()
        {
            var layer = ActiveLayer;
            if (layer == null) return false;
            _moveLayer = layer;
            _moveDx = _moveDy = 0;
            _moveBase = (byte[])layer.Pixels.Clone();
            layer.MarkDirty();
            var sel = S.Selection;
            if (sel == null)
            {
                _moveFloat = _moveHole = _moveShift = null;
                return true;
            }
            _moveFloat = new byte[_moveBase.Length];
            _moveHole = (byte[])_moveBase.Clone();
            _moveShift = new byte[_moveBase.Length];
            var m = sel.Mask;
            for (int p = 0; p < m.Length; p++)
            {
                int mv = m[p];
                if (mv == 0) continue;
                int i = p * 4;
                _moveFloat[i] = _moveBase[i];
                _moveFloat[i + 1] = _moveBase[i + 1];
                _moveFloat[i + 2] = _moveBase[i + 2];
                _moveFloat[i + 3] = (byte)(_moveBase[i + 3] * mv / 255);
                _moveHole[i + 3] = (byte)(_moveBase[i + 3] * (255 - mv) / 255);
            }
            return true;
        }

        void UpdateMove(Point p)
        {
            if (_moveLayer == null) return;
            int dx = (int)Math.Round(p.X - _dragStart.X), dy = (int)Math.Round(p.Y - _dragStart.Y);
            if (dx == _moveDx && dy == _moveDy) return;
            _moveDx = dx;
            _moveDy = dy;
            int w = Doc.Width, h = Doc.Height;
            var px = _moveLayer.Pixels;
            if (_moveFloat == null)
            {
                ImageOps.Offset(_moveBase, px, w, h, dx, dy);
            }
            else
            {
                Buffer.BlockCopy(_moveHole, 0, px, 0, px.Length);
                ImageOps.Offset(_moveFloat, _moveShift, w, h, dx, dy);
                ImageOps.BlendOver(px, _moveShift, w, h);
                var tt = new TranslateTransform(dx, dy);
                SelPathBlack.RenderTransform = SelPathWhite.RenderTransform = tt;
            }
            Recomposite();
        }

        void EndMove()
        {
            if (_moveLayer != null && (_moveDx != 0 || _moveDy != 0))
            {
                if (_moveFloat != null && S.Selection != null) S.Selection = S.Selection.Translated(_moveDx, _moveDy);
                UpdateSelectionVisual();
                Commit(T("Sposta"));
            }
            _moveLayer = null;
            _moveBase = _moveFloat = _moveHole = _moveShift = null;
        }

        void ApplySelection(Selection sel, SelectionOp op)
        {
            var cur = S.Selection;
            if (op != SelectionOp.Replace)
            {
                if (cur != null) sel = cur.Combine(sel, op);
                else if (op != SelectionOp.Add) return;
            }
            SetSelection(sel.IsEmpty ? null : sel, T("Selezione"));
        }

        void SetSelection(Selection sel, string historyName)
        {
            S.Selection = sel;
            UpdateSelectionVisual();
            if (historyName != null) Commit(historyName);
        }

        void Deselect()
        {
            if (S?.Selection == null) return;
            SetSelection(null, T("Deseleziona"));
        }

        void WandSelect(Point p, SelectionOp op)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= Doc.Width || y >= Doc.Height) return;
            var src = SampleAll.IsChecked == true ? Doc.Composite : ActiveLayer?.Pixels;
            if (src == null) return;
            byte[] mask;
            using (Busy()) mask = Painting.Region(src, Doc.Width, Doc.Height, x, y, (int)Tolerance.Value, Contiguous.IsChecked == true);
            ApplySelection(Selection.FromMask(mask, Doc.Width, Doc.Height), op);
        }

        void UpdateCropVisual()
        {
            if (S != null && _cropRect is Rect r && r.Width > 0 && r.Height > 0)
            {
                CropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, Doc.Width, Doc.Height)), new RectangleGeometry(r));
                var g = new GeometryGroup();
                g.Children.Add(new RectangleGeometry(r));
                for (int i = 1; i <= 2; i++)
                {
                    double x = r.X + r.Width * i / 3, y = r.Y + r.Height * i / 3;
                    g.Children.Add(new LineGeometry(new Point(x, r.Y), new Point(x, r.Bottom)));
                    g.Children.Add(new LineGeometry(new Point(r.X, y), new Point(r.Right, y)));
                }
                CropFrame.Data = g;
            }
            else
            {
                CropShade.Data = CropFrame.Data = null;
            }
        }

        void CancelCrop()
        {
            _cropRect = null;
            UpdateCropVisual();
        }

        void ApplyCrop()
        {
            if (S == null || _cropRect is not Rect r || r.Width < 1 || r.Height < 1) { CancelCrop(); return; }
            var ir = new Int32Rect((int)Math.Round(r.X), (int)Math.Round(r.Y),
                Math.Max(1, (int)Math.Round(r.Width)), Math.Max(1, (int)Math.Round(r.Height)));
            CancelCrop();
            CropTo(ir, T("Ritaglia"));
        }

        void BucketFill(Point p)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            Int32Rect r;
            using (Busy())
                r = Painting.FloodFill(layer, x, y, _primary, (int)Tolerance.Value, Contiguous.IsChecked == true, FillOpacity.Value / 100, S.Selection);
            if (!ImageOps.IsEmpty(r))
            {
                Recomposite(r);
                Commit(T("Secchiello"));
            }
        }

        void ApplyGradient(Point end)
        {
            var layer = ActiveLayer;
            if (layer == null || (end - _dragStart).Length < 2) return;
            var c1 = GradientColors.SelectedIndex == 0 ? _secondary : Color.FromArgb(0, _primary.R, _primary.G, _primary.B);
            using (Busy())
                Painting.Gradient(layer, _dragStart, end, _primary, c1, GradientType.SelectedIndex == 1, FillOpacity.Value / 100, S.Selection);
            Recomposite();
            Commit(TC("strumento", "Sfumatura"));
        }

        Geometry MakeShapeGeometry(Point a, Point b, bool constrain)
        {
            int kind = ShapeKind.SelectedIndex;
            if (kind == 3) return new LineGeometry(a, constrain ? SnapAngle(a, b) : b);
            var r = RectFrom(a, b, constrain, false);
            double rad = Math.Min(r.Width, r.Height) * 0.15;
            return kind switch
            {
                1 => new RectangleGeometry(r, rad, rad),
                2 => new EllipseGeometry(r),
                _ => new RectangleGeometry(r),
            };
        }

        bool ShapeIsStroked => ShapeKind.SelectedIndex == 3 || ShapeStyle.SelectedIndex == 1;

        void ShowShapePreview(Point p, bool constrain)
        {
            var brush = new SolidColorBrush(_primary);
            bool stroke = ShapeIsStroked;
            ShapePreview.Data = MakeShapeGeometry(_dragStart, p, constrain);
            ShapePreview.Fill = stroke ? null : brush;
            ShapePreview.Stroke = stroke ? brush : null;
            ShapePreview.StrokeThickness = ShapeStroke.Value;
            ShapePreview.StrokeStartLineCap = ShapePreview.StrokeEndLineCap = PenLineCap.Round;
        }

        void DrawShape(Point a, Point b, bool constrain)
        {
            if ((b - a).Length < 2) return;
            var g = MakeShapeGeometry(a, b, constrain);
            g.Freeze();
            bool stroke = ShapeIsStroked;
            var brush = new SolidColorBrush(_primary);
            brush.Freeze();
            var pen = stroke ? new Pen(brush, ShapeStroke.Value) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round } : null;
            var bounds = stroke ? g.GetRenderBounds(pen) : g.Bounds;
            if (bounds.IsEmpty) return;
            var layer = new Layer(NextLayerName(T("Forma")), Doc.Width, Doc.Height);
            Painting.RenderDrawing(layer, bounds, dc => dc.DrawGeometry(stroke ? null : brush, pen, g), null);
            InsertLayer(layer, T("Forma"));
        }

        void PlaceText(Point p)
        {
            var family = new FontFamily(FontCombo.SelectedItem as string ?? "Segoe UI");
            var dlg = new TextDialog(this, _lastText, family);
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Text)) return;
            _lastText = dlg.Text;
            double size = DialogWindow.TryParse(TextSizeBox.Text, out var sz) ? Math.Clamp(sz, 1, 3000) : 72;
            var tf = new Typeface(family,
                TextItalic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal,
                TextBold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            var brush = new SolidColorBrush(_primary);
            brush.Freeze();
            var ft = new FormattedText(dlg.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, size, brush, 1.0);
            var bounds = new Rect(p.X - size * 0.5, p.Y - size * 0.3, ft.WidthIncludingTrailingWhitespace + size, ft.Height + size * 0.6);
            string name = dlg.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (name.Length > 24) name = name.Substring(0, 24) + "…";
            var layer = new Layer(name, Doc.Width, Doc.Height);
            Painting.RenderDrawing(layer, bounds, dc => dc.DrawText(ft, p), null);
            InsertLayer(layer, T("Testo"));
        }
    }
}
