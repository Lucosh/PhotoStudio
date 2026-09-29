using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;

namespace PhotoStudio
{
    public partial class MainWindow
    {
        // ================= Adjustments & filters =================

        /// <summary>Shows a parameter dialog with asynchronous live preview, then applies fn to the active layer.</summary>
        void RunFilter(string title, ParamSpec[] specs, Func<byte[], int, int, double[], byte[]> fn, bool useSelection = true)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            int w = Doc.Width, h = Doc.Height;
            var original = (byte[])layer.Pixels.Clone();
            var sel = useSelection ? S.Selection : null;
            int ticket = 0;

            async void Preview(double[] v)
            {
                int my = ++ticket;
                if (v == null)
                {
                    layer.Pixels = original;
                    Recomposite();
                    return;
                }
                byte[] res;
                try { res = await Task.Run(() => ImageOps.ApplyMask(original, fn(original, w, h, v), sel)); }
                catch { return; }
                if (my != ticket) return;
                layer.Pixels = res;
                Recomposite();
            }

            var dlg = new ParamDialog(this, title, specs, Preview);
            bool ok = dlg.ShowDialog() == true;
            ticket++; // discard previews still running
            if (ok)
            {
                using (Busy()) layer.Pixels = ImageOps.ApplyMask(original, fn(original, w, h, dlg.Values), sel);
                Recomposite();
                Commit(title);
            }
            else
            {
                layer.Pixels = original;
                Recomposite();
            }
        }

        void ApplyInstant(string name, Func<byte[], int, int, byte[]> fn)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            var original = layer.Pixels;
            using (Busy()) layer.Pixels = ImageOps.ApplyMask(original, fn(original, Doc.Width, Doc.Height), S.Selection);
            Recomposite();
            Commit(name);
        }

        void RunCurves()
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            var original = (byte[])layer.Pixels.Clone();
            var sel = S.Selection;

            void Preview(byte[][] l)
            {
                layer.Pixels = ImageOps.ApplyMask(original, Adjustments.ApplyLut(original, l[0], l[1], l[2]), sel);
                Recomposite();
            }

            var dlg = new CurvesDialog(this, original, Preview);
            if (dlg.ShowDialog() == true)
            {
                Preview(dlg.Luts);
                Commit("Curve");
            }
            else
            {
                layer.Pixels = original;
                Recomposite();
            }
        }

        // ================= RAW / automatic enhancement =================

        /// <summary>The original photo as linear data: the RAW, or the 8-bit image (orientation applied, as when opening).</summary>
        static RawImage LoadSourceLinear(string path)
        {
            if (RawImage.IsRawFile(path)) return RawImage.Load(path);
            var (w, h, px) = ImageIO.LoadBitmap(path);
            return RawImage.FromBgra(px, w, h);
        }

        /// <summary>Marks the current state as "just developed from the original with these settings".</summary>
        static void MarkDeveloped(Session s, RawSettings settings) => s.MarkDeveloped(settings);

        /// <summary>Replaces the (single-layer) document with a new development; the size changes with the crop.</summary>
        void ReplaceDeveloped(int w, int h, byte[] px, string name)
        {
            if (w == Doc.Width && h == Doc.Height)
            {
                Doc.Layers[0].Pixels = px;
                Recomposite();
                Commit(name);
                return;
            }
            TransformImage(_ => px, w, h, name);
            Dispatcher.BeginInvoke(new Action(() => FitToScreen(true)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>The original of an untouched photo as linear data (for JPEG never developed, the layer is the original).</summary>
        RawImage OriginalOf(Session s) =>
            s.RawSettings == null && !RawImage.IsRawFile(s.SourcePath) && s.Doc.Layers.Count == 1
                ? RawImage.FromBgra(s.Doc.Layers[0].Pixels, s.Doc.Width, s.Doc.Height)
                : LoadSourceLinear(s.SourcePath);

        /// <summary>
        /// Applies development settings to an open photo: from the original file when the document has not been
        /// edited since it was developed (best quality), otherwise to the active layer as a filter.
        /// </summary>
        bool ApplySettingsToSession(Session s, RawSettings pasted, SettingsGroups groups, bool quiet = false)
        {
            if (s == null) return false;
            if (s != S) ActivateSession(s);
            var baseSettings = s.RawSettings ?? RawSettings.Default(s.SourcePath != null && RawImage.IsRawFile(s.SourcePath));
            var settings = baseSettings.MergeFrom(pasted, groups);
            try
            {
                if (s.IsAsDeveloped && s.SourcePath != null && File.Exists(s.SourcePath))
                {
                    (int Width, int Height, byte[] Pixels) result;
                    using (Busy()) result = RawDevelop.Develop(OriginalOf(s), settings);
                    ReplaceDeveloped(result.Width, result.Height, result.Pixels, "Applica impostazioni");
                    MarkDeveloped(s, settings);
                }
                else
                {
                    var layer = ActiveLayer;
                    if (layer == null) return false;
                    var original = layer.Pixels;
                    // The pixels are already developed: start from neutral values and add only the chosen groups
                    // (no crop: the layer must keep its size).
                    var filter = RawSettings.Default(false).MergeFrom(pasted, groups & ~SettingsGroups.Geometry);
                    using (Busy()) layer.Pixels = ImageOps.ApplyMask(original, RawDevelop.Render(RawImage.FromBgra(original, Doc.Width, Doc.Height), filter), S.Selection);
                    Recomposite();
                    Commit("Applica impostazioni");
                    if (!quiet) Status("La foto aveva già altre modifiche: impostazioni applicate al livello attivo come filtro (Ctrl+Z per annullare).");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show(this, "Impossibile applicare le impostazioni:\n" + ex.Message, "Applica impostazioni", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            var item = BatchItemOf(s);
            if (item != null) { item.Settings = settings.Clone(); SaveBatchState(); }
            if (!quiet) Status("Impostazioni applicate: " + settings.Describe() + "   (Ctrl+Z per annullare)");
            return true;
        }

        Session OpenRaw(string path, RawSettings initial = null)
        {
            RawImage raw;
            try
            {
                Status("Decodifica del file RAW in corso: " + System.IO.Path.GetFileName(path) + "...");
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
                using (Busy()) raw = RawImage.Load(path);
            }
            catch (Exception ex)
            {
                Status("");
                MessageBox.Show(this, $"Impossibile aprire il file RAW:\n{path}\n\n{ex.Message}", "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            Status("");
            var dlg = new CameraRawDialog(this, raw, System.IO.Path.GetFileName(path), "Apri immagine", initial: initial);
            if (dlg.ShowDialog() != true || dlg.Result == null) return null;

            var doc = new Document(dlg.ResultWidth, dlg.ResultHeight);
            var layer = new Layer("Sfondo", dlg.ResultWidth, dlg.ResultHeight, dlg.Result);
            layer.UpdateThumbnail();
            doc.Layers.Add(layer);
            // FilePath stays empty on purpose: "Salva" must never overwrite the original RAW file.
            var s = new Session(doc, System.IO.Path.GetFileName(path)) { SourcePath = path };
            s.History.Push("Apri (Camera Raw)", doc.Snapshot(0, null));
            MarkDeveloped(s, dlg.Settings);
            AddSession(s);
            Status($"Sviluppato {System.IO.Path.GetFileName(path)}: {dlg.Settings.Describe()}");
            return s;
        }

        void AutoEnhance()
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            var original = layer.Pixels;
            byte[] result;
            RawSettings settings;
            using (Busy())
            {
                var linear = RawImage.FromBgra(original, Doc.Width, Doc.Height);
                settings = RawDevelop.AutoTone(linear.Downscale(1200), RawSettings.Default(false));
                result = RawDevelop.Render(linear, settings);
            }
            layer.Pixels = ImageOps.ApplyMask(original, result, S.Selection);
            Recomposite();
            Commit("Miglioramento automatico");
            Status("Miglioramento automatico: " + settings.Describe() + "   (Ctrl+Z per annullare)");
        }

        /// <param name="startTab">Camera Raw tab to open (e.g. "Maschere").</param>
        void CameraRawFilter(bool focusAi, string startTab = null)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            if (S.Selection == null && S.IsAsDeveloped && S.SourcePath != null && File.Exists(S.SourcePath))
            {
                RedevelopFromOriginal(focusAi, startTab);
                return;
            }
            var original = layer.Pixels;
            RawImage linear;
            using (Busy()) linear = RawImage.FromBgra(original, Doc.Width, Doc.Height);
            // On a layer the size cannot change: no straighten/crop here.
            var dlg = new CameraRawDialog(this, linear, (focusAi ? "Modifica con AI — " : "Filtro — ") + layer.Name, "OK", focusAi: focusAi,
                                          allowGeometry: false, startTab: startTab);
            if (dlg.ShowDialog() != true || dlg.Result == null) return;
            layer.Pixels = ImageOps.ApplyMask(original, dlg.Result, S.Selection);
            Recomposite();
            Commit(focusAi ? "Modifica con AI" : "Filtro Camera Raw");
        }

        /// <summary>
        /// Camera Raw on a photo not edited since it was opened: develops the original file again, starting from
        /// the previous settings (for a RAW this keeps the full 16-bit quality, and the settings can be copied).
        /// </summary>
        void RedevelopFromOriginal(bool focusAi, string startTab = null)
        {
            var s = S;
            RawImage src;
            try
            {
                using (Busy()) src = OriginalOf(s);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossibile rileggere l'originale:\n" + ex.Message, "Camera Raw", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string name = System.IO.Path.GetFileName(s.SourcePath);
            var dlg = new CameraRawDialog(this, src, (focusAi ? "Modifica con AI — " : "") + name, "OK", focusAi: focusAi, initial: s.RawSettings, startTab: startTab);
            if (dlg.ShowDialog() != true || dlg.Result == null) return;
            ReplaceDeveloped(dlg.ResultWidth, dlg.ResultHeight, dlg.Result, focusAi ? "Modifica con AI" : "Camera Raw");
            MarkDeveloped(s, dlg.Settings);
            var item = BatchItemOf(s);
            if (item != null) { item.Settings = dlg.Settings; SaveBatchState(); }
            Status($"Sviluppato {name}: {dlg.Settings.Describe()}");
        }

        void TransformLayerDialog()
        {
            RunFilter("Trasforma livello",
                new[]
                {
                    new ParamSpec("Scala %", 1, 400, 100, 1),
                    new ParamSpec("Rotazione °", -180, 180, 0, 1),
                    new ParamSpec("Sposta X (px)", -Doc.Width, Doc.Width, 0),
                    new ParamSpec("Sposta Y (px)", -Doc.Height, Doc.Height, 0),
                },
                (s, w, h, v) => ImageOps.TransformLayer(s, w, h, v[0] / 100, v[1], v[2], v[3]),
                useSelection: false);
        }

        void TransformActiveLayer(Func<byte[], byte[]> f, string name)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            using (Busy()) layer.Pixels = f(layer.Pixels);
            Recomposite();
            Commit(name);
        }

        // ================= Image =================

        void TransformImage(Func<byte[], byte[]> f, int nw, int nh, string name)
        {
            CancelCrop();
            using (Busy()) Doc.Transform(f, nw, nh);
            S.Selection = null;
            InitDisplay();
            UpdateSelectionVisual();
            Commit(name);
        }

        void ResizeImage()
        {
            var d = new ResizeDialog(this, Doc.Width, Doc.Height);
            if (d.ShowDialog() != true) return;
            int ow = Doc.Width, oh = Doc.Height, nw = d.NewWidth, nh = d.NewHeight;
            if (nw == ow && nh == oh) return;
            TransformImage(px => ImageOps.Resize(px, ow, oh, nw, nh), nw, nh, "Dimensione immagine");
        }

        void CanvasSize()
        {
            var d = new CanvasSizeDialog(this, Doc.Width, Doc.Height);
            if (d.ShowDialog() != true) return;
            int ow = Doc.Width, oh = Doc.Height, nw = d.NewWidth, nh = d.NewHeight;
            if (nw == ow && nh == oh) return;
            int ox = (nw - ow) * d.AnchorX / 2, oy = (nh - oh) * d.AnchorY / 2;
            var r = new Int32Rect(-ox, -oy, nw, nh);
            TransformImage(px => ImageOps.Crop(px, ow, oh, r), nw, nh, "Dimensione quadro");
        }

        void CropTo(Int32Rect r, string name)
        {
            r = ImageOps.Intersect(r, Doc.Bounds);
            if (ImageOps.IsEmpty(r)) return;
            int ow = Doc.Width, oh = Doc.Height;
            TransformImage(px => ImageOps.Crop(px, ow, oh, r), r.Width, r.Height, name);
            Dispatcher.BeginInvoke(new Action(() => FitToScreen(true)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ================= Clipboard / fill =================

        (int w, int h, byte[] px) ExtractSelection(byte[] src)
        {
            var sel = S.Selection;
            if (sel == null) return (Doc.Width, Doc.Height, (byte[])src.Clone());
            var b = sel.Bounds;
            var buf = ImageOps.Crop(src, Doc.Width, Doc.Height, b);
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    int m = sel.Mask[(b.Y + y) * Doc.Width + b.X + x];
                    int i = (y * b.Width + x) * 4 + 3;
                    buf[i] = (byte)(buf[i] * m / 255);
                }
            return (b.Width, b.Height, buf);
        }

        void Copy(bool merged)
        {
            var src = merged ? Doc.Render() : ActiveLayer?.Pixels;
            if (src == null) return;
            var (w, h, px) = ExtractSelection(src);
            try
            {
                ImageIO.CopyToClipboard(w, h, px);
                Status(merged ? "Copia unita negli appunti." : "Copiato negli appunti.");
            }
            catch (Exception ex)
            {
                Status("Impossibile copiare: " + ex.Message);
            }
        }

        void Cut()
        {
            if (S.Selection == null) return;
            Copy(false);
            ClearPixels("Taglia");
        }

        void Paste()
        {
            var img = ImageIO.GetClipboardImage();
            if (img == null) { Status("Gli appunti non contengono un'immagine."); return; }
            var (w, h, px) = img.Value;
            if (S == null)
            {
                var doc = new Document(w, h);
                var layer = new Layer("Livello 1", w, h, px);
                layer.UpdateThumbnail();
                doc.Layers.Add(layer);
                var s = new Session(doc, $"Senza titolo-{_untitled++}");
                s.History.Push("Incolla", doc.Snapshot(0, null));
                AddSession(s);
                return;
            }
            PlaceImage(w, h, px, NextLayerName("Livello"), "Incolla", false);
        }

        void ClearSelectionPixels() => ClearPixels("Cancella");

        void ClearPixels(string name)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            if (S.Selection == null) { Status("Nessuna selezione: seleziona prima l'area da cancellare."); return; }
            Painting.ClearArea(layer, S.Selection);
            Recomposite();
            Commit(name);
        }

        void FillSelection(Color c)
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            using (Busy()) Painting.FillArea(layer, c, S.Selection);
            Recomposite();
            Commit("Riempi");
        }

        void FeatherSelection()
        {
            if (S.Selection == null) { Status("Nessuna selezione attiva."); return; }
            var d = new ParamDialog(this, "Sfuma selezione", new[] { new ParamSpec("Raggio sfumatura (pixel)", 0.5, 250, 5, 1) }, null);
            if (d.ShowDialog() != true) return;
            Selection s;
            using (Busy()) s = S.Selection.Feathered(d.Values[0]);
            SetSelection(NullIfEmpty(s), "Sfuma");
        }

        // ================= Layers =================

        string NextLayerName(string prefix)
        {
            int n = 1;
            foreach (var l in Doc.Layers)
                if (l.Name.StartsWith(prefix + " ") && int.TryParse(l.Name.Substring(prefix.Length + 1), out int k))
                    n = Math.Max(n, k + 1);
            return $"{prefix} {n}";
        }

        void InsertLayer(Layer layer, string historyName)
        {
            int idx = Math.Max(0, ActiveIndex);
            layer.UpdateThumbnail();
            Doc.Layers.Insert(idx, layer);
            SelectLayer(idx);
            Recomposite();
            Commit(historyName);
        }

        void NewLayer() => InsertLayer(new Layer(NextLayerName("Livello"), Doc.Width, Doc.Height), "Nuovo livello");

        void DuplicateLayer()
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            if (S.Selection != null)
            {
                var px = (byte[])layer.Pixels.Clone();
                var m = S.Selection.Mask;
                for (int p = 0; p < m.Length; p++) px[p * 4 + 3] = (byte)(px[p * 4 + 3] * m[p] / 255);
                InsertLayer(new Layer(NextLayerName("Livello"), Doc.Width, Doc.Height, px), "Livello tramite copia");
            }
            else
            {
                InsertLayer(layer.Clone(layer.Name + " copia"), "Duplica livello");
            }
        }

        void DeleteLayer()
        {
            if (Doc.Layers.Count <= 1) { Status("Impossibile eliminare l'unico livello del documento."); return; }
            int idx = ActiveIndex;
            Doc.Layers.RemoveAt(idx);
            SelectLayer(Math.Min(idx, Doc.Layers.Count - 1));
            Recomposite();
            Commit("Elimina livello");
        }

        void RenameLayer()
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            var d = new InputDialog(this, "Rinomina livello", "Nome:", layer.Name);
            if (d.ShowDialog() != true) return;
            string name = d.Value.Trim();
            if (name.Length == 0 || name == layer.Name) return;
            layer.Name = name;
            Commit("Rinomina livello");
        }

        void LayersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            for (var d = e.OriginalSource as DependencyObject; d != null && d != LayersList;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is CheckBox) return; // double click on the eye icon
                if (d is ListBoxItem) { RenameLayer(); return; }
            }
        }

        void MoveLayer(int dir)
        {
            int idx = ActiveIndex, ni = idx + dir;
            if (idx < 0 || ni < 0 || ni >= Doc.Layers.Count) return;
            Doc.Layers.Move(idx, ni);
            SelectLayer(ni);
            Recomposite();
            Commit(dir < 0 ? "Porta avanti" : "Porta indietro");
        }

        void MergeDown()
        {
            int idx = ActiveIndex;
            if (idx < 0 || idx >= Doc.Layers.Count - 1) return;
            using (Busy()) idx = Doc.MergeDown(idx);
            SelectLayer(idx);
            Recomposite();
            Commit("Unisci sotto");
        }

        void MergeVisible()
        {
            var visible = Doc.Layers.Where(l => l.Visible).ToList();
            if (visible.Count < 2) return;
            byte[] px;
            using (Busy()) px = Doc.Render();
            var bottom = visible[visible.Count - 1];
            int newIdx = Doc.Layers.IndexOf(bottom) - (visible.Count - 1);
            var merged = new Layer(bottom.Name, Doc.Width, Doc.Height, px);
            foreach (var l in visible) Doc.Layers.Remove(l);
            newIdx = Math.Clamp(newIdx, 0, Doc.Layers.Count);
            merged.UpdateThumbnail();
            Doc.Layers.Insert(newIdx, merged);
            SelectLayer(newIdx);
            Recomposite();
            Commit("Unisci visibili");
        }

        void Flatten()
        {
            using (Busy()) Doc.Flatten(Colors.White);
            SelectLayer(0);
            Recomposite();
            Commit("Appiattisci");
        }
    }
}
