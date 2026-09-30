using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    // Ritaglio tab: straighten and crop, without touching the original (as in Lightroom).
    public sealed partial class CameraRawDialog
    {
        enum CropDrag { None, Move, TL, TR, BL, BR, L, R, T, B }

        static readonly (string Name, double Ratio)[] Aspects =
        {
            (T("Originale"), 0), (T("Libero"), -1), (T("1:1 quadrato"), 1), (T("4:5 verticale"), 0.8), ("5:4", 1.25),
            ("3:2", 1.5), (T("2:3 verticale"), 2 / 3.0), ("16:9", 16 / 9.0), (T("9:16 verticale"), 9 / 16.0),
        };

        ComboBox _aspect;
        CropDrag _cropDrag;
        Point _cropStart;
        double _cl0, _ct0, _cr0, _cb0;

        Panel BuildCropTab()
        {
            var p = TabPanel();
            Section(p, T("RADDRIZZA"));
            Row(p, T("Angolo"), -45, 45, 1, x => x.Angle, (x, v) => x.Angle = v,
                changed: () => { FitAspect(false); UpdateOverlay(); },
                tip: T("Ruota la foto per raddrizzare l'orizzonte; il ritaglio si stringe da solo per non lasciare angoli vuoti"));
            var quick = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var d in new[] { -1.0, -0.5, 0.5, 1.0 })
            {
                var b = new Button { Content = (d > 0 ? "+" : "") + d.ToString("0.0") + "°", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += (s, e) =>
                {
                    _s.Angle = Math.Clamp(Math.Round(_s.Angle + d, 1), -45, 45);
                    FitAspect(false);
                    RefreshSliders();
                    UpdateOverlay();
                    Edited();
                };
                quick.Children.Add(b);
            }
            p.Children.Add(quick);

            Section(p, T("RITAGLIO"));
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = T("Proporzioni:"), VerticalAlignment = VerticalAlignment.Center, Width = 96 });
            _aspect = new ComboBox();
            foreach (var a in Aspects) _aspect.Items.Add(a.Name);
            _aspect.SelectedIndex = 0;
            _aspect.SelectionChanged += (s, e) =>
            {
                if (_s == null) return;
                FitAspect(true);
                UpdateOverlay();
                Edited();
            };
            row.Children.Add(_aspect);
            p.Children.Add(row);

            var buttons = new WrapPanel();
            var resetCrop = new Button { Content = T("Ritaglio completo"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("La cornice torna grande quanto la foto") };
            resetCrop.Click += (s, e) =>
            {
                _s.CropL = _s.CropT = 0; _s.CropR = _s.CropB = 1;
                FitAspect(true);
                UpdateOverlay();
                Edited();
            };
            var resetAll = new Button { Content = T("Azzera raddrizza e ritaglio"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6) };
            resetAll.Click += (s, e) =>
            {
                StripGeometry(_s);
                _aspect.SelectedIndex = 0;
                RefreshSliders();
                UpdateOverlay();
                Edited();
            };
            buttons.Children.Add(resetCrop);
            buttons.Children.Add(resetAll);
            p.Children.Add(buttons);
            p.Children.Add(Hint(T("Trascina gli angoli o i lati della cornice sulla foto; trascina all'interno per spostarla. In questa scheda vedi la foto intera: nelle altre schede vedi il risultato ritagliato. L'originale non viene mai tagliato.")));

            _overlay.MouseLeftButtonDown += Crop_Down;
            _overlay.MouseMove += Crop_Move;
            _overlay.MouseLeftButtonUp += Crop_Up;
            return p;
        }

        /// <summary>Crop ratio in normalised units ((R-L)/(B-T)), or -1 when free.</summary>
        double TargetRatio()
        {
            if (_aspect == null) return -1;
            double a = Aspects[Math.Max(0, _aspect.SelectedIndex)].Ratio;
            if (a < 0) return -1;
            double pixels = a == 0 ? (double)_preview.Width / _preview.Height : a;
            return pixels * _preview.Height / _preview.Width;
        }

        /// <summary>Keeps the crop inside the straightened photo and with the chosen proportions.</summary>
        void FitAspect(bool largest)
        {
            int w = _preview.Width, h = _preview.Height;
            double k = RawDevelop.InscribedScale(w, h, _s.Angle), lo = 0.5 - k / 2, hi = 0.5 + k / 2, span = hi - lo;
            double rn = TargetRatio();
            if (rn < 0)
            {
                if (largest) { _s.CropL = _s.CropT = lo; _s.CropR = _s.CropB = hi; }
                RawDevelop.ConstrainCrop(_s, w, h);
                return;
            }
            double cx = largest ? 0.5 : (_s.CropL + _s.CropR) / 2, cy = largest ? 0.5 : (_s.CropT + _s.CropB) / 2;
            double cw = largest ? span : _s.CropR - _s.CropL, ch = largest ? span : _s.CropB - _s.CropT;
            double nw = Math.Min(cw, ch * rn), nh = nw / rn;
            double fit = Math.Min(1, Math.Min(span / nw, span / nh));
            nw *= fit; nh *= fit;
            cx = Math.Clamp(cx, lo + nw / 2, hi - nw / 2);
            cy = Math.Clamp(cy, lo + nh / 2, hi - nh / 2);
            _s.CropL = cx - nw / 2; _s.CropR = cx + nw / 2;
            _s.CropT = cy - nh / 2; _s.CropB = cy + nh / 2;
        }

        void DrawCropOverlay(Rect r)
        {
            _overlay.Background = Brushes.Transparent;   // every click belongs to the crop frame
            double x0 = r.X + _s.CropL * r.Width, x1 = r.X + _s.CropR * r.Width;
            double y0 = r.Y + _s.CropT * r.Height, y1 = r.Y + _s.CropB * r.Height;
            var dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            void Box(double x, double y, double w, double h)
            {
                if (w <= 0 || h <= 0) return;
                var rc = new Rectangle { Width = w, Height = h, Fill = dim, IsHitTestVisible = false };
                Canvas.SetLeft(rc, x); Canvas.SetTop(rc, y);
                _overlay.Children.Add(rc);
            }
            Box(r.X, r.Y, r.Width, y0 - r.Y);
            Box(r.X, y1, r.Width, r.Bottom - y1);
            Box(r.X, y0, x0 - r.X, y1 - y0);
            Box(x1, y0, r.Right - x1, y1 - y0);
            var third = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            for (int i = 1; i < 3; i++)
            {
                _overlay.Children.Add(new Line { X1 = x0 + (x1 - x0) * i / 3, X2 = x0 + (x1 - x0) * i / 3, Y1 = y0, Y2 = y1, Stroke = third, IsHitTestVisible = false });
                _overlay.Children.Add(new Line { X1 = x0, X2 = x1, Y1 = y0 + (y1 - y0) * i / 3, Y2 = y0 + (y1 - y0) * i / 3, Stroke = third, IsHitTestVisible = false });
            }
            var frame = new Rectangle { Width = Math.Max(1, x1 - x0), Height = Math.Max(1, y1 - y0), Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(frame, x0); Canvas.SetTop(frame, y0);
            _overlay.Children.Add(frame);
            foreach (var (hx, hy) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1), ((x0 + x1) / 2, y0), ((x0 + x1) / 2, y1), (x0, (y0 + y1) / 2), (x1, (y0 + y1) / 2) })
            {
                var hnd = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(hnd, hx - 5); Canvas.SetTop(hnd, hy - 5);
                _overlay.Children.Add(hnd);
            }
        }

        CropDrag HitCrop(Point p, Rect r)
        {
            double x0 = r.X + _s.CropL * r.Width, x1 = r.X + _s.CropR * r.Width;
            double y0 = r.Y + _s.CropT * r.Height, y1 = r.Y + _s.CropB * r.Height;
            const double tol = 10;
            bool nl = Math.Abs(p.X - x0) < tol, nr = Math.Abs(p.X - x1) < tol, nt = Math.Abs(p.Y - y0) < tol, nb = Math.Abs(p.Y - y1) < tol;
            bool inX = p.X > x0 - tol && p.X < x1 + tol, inY = p.Y > y0 - tol && p.Y < y1 + tol;
            if (nl && nt) return CropDrag.TL;
            if (nr && nt) return CropDrag.TR;
            if (nl && nb) return CropDrag.BL;
            if (nr && nb) return CropDrag.BR;
            if (nl && inY) return CropDrag.L;
            if (nr && inY) return CropDrag.R;
            if (nt && inX) return CropDrag.T;
            if (nb && inX) return CropDrag.B;
            if (p.X > x0 && p.X < x1 && p.Y > y0 && p.Y < y1) return CropDrag.Move;
            return CropDrag.None;
        }

        void Crop_Down(object sender, MouseButtonEventArgs e)
        {
            if (_tab != "Ritaglio") return;
            var r = ImageRect();
            if (r.IsEmpty) return;
            _cropDrag = HitCrop(e.GetPosition(_overlay), r);
            if (_cropDrag == CropDrag.None) return;
            _cropStart = e.GetPosition(_overlay);
            _cl0 = _s.CropL; _ct0 = _s.CropT; _cr0 = _s.CropR; _cb0 = _s.CropB;
            _overlay.CaptureMouse();
            e.Handled = true;
        }

        void Crop_Move(object sender, MouseEventArgs e)
        {
            if (_tab != "Ritaglio") return;
            var r = ImageRect();
            if (r.IsEmpty) return;
            var p = e.GetPosition(_overlay);
            if (_cropDrag == CropDrag.None)
            {
                _overlay.Cursor = HitCrop(p, r) switch
                {
                    CropDrag.TL or CropDrag.BR => Cursors.SizeNWSE,
                    CropDrag.TR or CropDrag.BL => Cursors.SizeNESW,
                    CropDrag.L or CropDrag.R => Cursors.SizeWE,
                    CropDrag.T or CropDrag.B => Cursors.SizeNS,
                    CropDrag.Move => Cursors.SizeAll,
                    _ => Cursors.Arrow,
                };
                return;
            }
            DragCrop((p.X - _cropStart.X) / r.Width, (p.Y - _cropStart.Y) / r.Height);
            UpdateOverlay();
        }

        void Crop_Up(object sender, MouseButtonEventArgs e)
        {
            if (_cropDrag == CropDrag.None) return;
            _cropDrag = CropDrag.None;
            _overlay.ReleaseMouseCapture();
            Edited();
        }

        void DragCrop(double du, double dv)
        {
            double k = RawDevelop.InscribedScale(_preview.Width, _preview.Height, _s.Angle), lo = 0.5 - k / 2, hi = 0.5 + k / 2;
            const double min = 0.03;
            double l = _cl0, t = _ct0, r = _cr0, b = _cb0;
            if (_cropDrag == CropDrag.Move)
            {
                du = Math.Clamp(du, lo - l, hi - r);
                dv = Math.Clamp(dv, lo - t, hi - b);
                _s.CropL = l + du; _s.CropR = r + du; _s.CropT = t + dv; _s.CropB = b + dv;
                return;
            }
            bool left = _cropDrag is CropDrag.TL or CropDrag.BL or CropDrag.L;
            bool right = _cropDrag is CropDrag.TR or CropDrag.BR or CropDrag.R;
            bool top = _cropDrag is CropDrag.TL or CropDrag.TR or CropDrag.T;
            bool bottom = _cropDrag is CropDrag.BL or CropDrag.BR or CropDrag.B;
            if (left) l = Math.Clamp(l + du, lo, r - min);
            if (right) r = Math.Clamp(r + du, l + min, hi);
            if (top) t = Math.Clamp(t + dv, lo, b - min);
            if (bottom) b = Math.Clamp(b + dv, t + min, hi);
            double rn = TargetRatio();
            if (rn > 0)
            {
                double w = r - l, h = b - t;
                if (_cropDrag is CropDrag.L or CropDrag.R) h = w / rn;
                else if (_cropDrag is CropDrag.T or CropDrag.B) w = h * rn;
                else if (w / h > rn) w = h * rn;
                else h = w / rn;
                // The side opposite to the one dragged stays still; an untouched axis stays centred.
                double ax = right ? l : left ? r : (l + r) / 2, ay = bottom ? t : top ? b : (t + b) / 2;
                double maxW = right ? hi - ax : left ? ax - lo : 2 * Math.Min(ax - lo, hi - ax);
                double maxH = bottom ? hi - ay : top ? ay - lo : 2 * Math.Min(ay - lo, hi - ay);
                double scale = Math.Min(1, Math.Min(maxW / w, maxH / h));
                w *= scale; h *= scale;
                l = right ? ax : left ? ax - w : ax - w / 2; r = l + w;
                t = bottom ? ay : top ? ay - h : ay - h / 2; b = t + h;
            }
            _s.CropL = l; _s.CropT = t; _s.CropR = r; _s.CropB = b;
        }
    }
}
