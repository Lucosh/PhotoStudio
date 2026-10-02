using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    public partial class MainWindow
    {
        readonly Dictionary<string, (Action Run, bool NeedsDoc)> _actions = new Dictionary<string, (Action, bool)>();
        readonly Dictionary<(ModifierKeys, Key), string> _shortcuts = new Dictionary<(ModifierKeys, Key), string>();

        void A(string key, Action run, bool needsDoc = true) => _actions[key] = (run, needsDoc);

        void K(string action, ModifierKeys mods, params Key[] keys)
        {
            foreach (var k in keys) _shortcuts[(mods, k)] = action;
        }

        void RegisterActions()
        {
            // ---- File
            A("file.new", NewDocument, false);
            A("file.open", OpenDialog, false);
            A("file.place", PlaceFile);
            A("file.close", () => CloseSession(S));
            A("file.save", () => Save(false));
            A("file.saveas", () => Save(true));
            A("file.export", ExportImage);
            A("file.exit", Close, false);
            A("file.cull", () => StartCulling(), false);
            A("batch.savenext", BatchSaveNext);
            A("batch.saveall", BatchSaveAll, false);
            A("batch.prev", () => BatchMove(-1), false);
            A("batch.next", () => BatchMove(1), false);
            A("dev.copy", CopySettings);
            A("dev.paste", ApplySettings, false);
            A("batch.export", EditExportOptions, false);
            A("dev.smartlight", SmartLightCommand, false);
            A("dev.masks", LightMasksCommand, false);

            // ---- Edit
            A("edit.undo", Undo);
            A("edit.redo", Redo);
            A("edit.cut", Cut);
            A("edit.copy", () => Copy(false));
            A("edit.copymerged", () => Copy(true));
            A("edit.paste", Paste, false);
            A("edit.clear", ClearSelectionPixels);
            A("edit.fillfg", () => FillSelection(_primary));
            A("edit.fillbg", () => FillSelection(_secondary));
            A("edit.transform", TransformLayerDialog);
            A("edit.layer180", () => TransformActiveLayer(px => ImageOps.Rotate180(px, Doc.Width, Doc.Height), T("Ruota livello 180°")));
            A("edit.layerfliph", () => TransformActiveLayer(px => ImageOps.FlipH(px, Doc.Width, Doc.Height), T("Rifletti livello orizzontale")));
            A("edit.layerflipv", () => TransformActiveLayer(px => ImageOps.FlipV(px, Doc.Width, Doc.Height), T("Rifletti livello verticale")));

            // ---- Adjustments
            A("adj.brightness", () => RunFilter(T("Luminosità/Contrasto"),
                new[] { new ParamSpec(T("Luminosità"), -100, 100, 0), new ParamSpec(T("Contrasto"), -100, 100, 0) },
                (s, w, h, v) => Adjustments.BrightnessContrast(s, v[0], v[1])));
            A("adj.levels", () => RunFilter(T("Livelli"),
                new[]
                {
                    new ParamSpec(T("Input nero"), 0, 253, 0), new ParamSpec(T("Input bianco"), 2, 255, 255),
                    new ParamSpec(T("Gamma (mezzitoni)"), 0.1, 9.99, 1, 2),
                    new ParamSpec(T("Output nero"), 0, 255, 0), new ParamSpec(T("Output bianco"), 0, 255, 255),
                },
                (s, w, h, v) => Adjustments.Levels(s, v[0], Math.Max(v[0] + 2, v[1]), v[2], v[3], v[4])));
            A("adj.curves", RunCurves);
            A("adj.exposure", () => RunFilter(T("Esposizione"),
                new[] { new ParamSpec(T("Esposizione (EV)"), -5, 5, 0, 2), new ParamSpec(T("Scostamento"), -0.5, 0.5, 0, 3), new ParamSpec(T("Correzione gamma"), 0.1, 3, 1, 2) },
                (s, w, h, v) => Adjustments.Exposure(s, v[0], v[1], v[2])));
            A("adj.vibrance", () => RunFilter(T("Vividezza"),
                new[] { new ParamSpec(T("Vividezza"), -100, 100, 0), new ParamSpec(T("Saturazione"), -100, 100, 0) },
                (s, w, h, v) => Adjustments.Vibrance(s, v[0], v[1])));
            A("adj.huesat", () => RunFilter(T("Tonalità/Saturazione"),
                new[] { new ParamSpec(T("Tonalità"), -180, 180, 0), new ParamSpec(T("Saturazione"), -100, 100, 0), new ParamSpec(T("Luminosità"), -100, 100, 0) },
                (s, w, h, v) => Adjustments.HueSaturation(s, v[0], v[1], v[2])));
            A("adj.colorbalance", () => RunFilter(T("Bilanciamento colore"),
                new[]
                {
                    new ParamSpec(T("Ciano ↔ Rosso"), -100, 100, 0), new ParamSpec(T("Magenta ↔ Verde"), -100, 100, 0),
                    new ParamSpec(T("Giallo ↔ Blu"), -100, 100, 0), ParamSpec.Check(T("Mantieni luminosità"), true),
                },
                (s, w, h, v) => Adjustments.ColorBalance(s, v[0], v[1], v[2], v[3] > 0)));
            A("adj.temperature", () => RunFilter(T("Temperatura colore"),
                new[] { new ParamSpec(T("Temperatura"), -100, 100, 0), new ParamSpec(T("Tinta"), -100, 100, 0) },
                (s, w, h, v) => Adjustments.Temperature(s, v[0], v[1])));
            A("adj.bw", () => RunFilter(T("Bianco e nero"),
                new[] { new ParamSpec(T("Rossi %"), -200, 300, 30), new ParamSpec(T("Verdi %"), -200, 300, 59), new ParamSpec(T("Blu %"), -200, 300, 11) },
                (s, w, h, v) => Adjustments.BlackWhite(s, v[0], v[1], v[2])));
            A("adj.invert", () => ApplyInstant(T("Inverti"), (s, w, h) => Adjustments.Invert(s)));
            A("adj.posterize", () => RunFilter(T("Posterizza"), new[] { new ParamSpec(T("Numero di livelli"), 2, 64, 4) },
                (s, w, h, v) => Adjustments.Posterize(s, v[0])));
            A("adj.threshold", () => RunFilter(T("Soglia"), new[] { new ParamSpec(T("Livello soglia"), 1, 255, 128) },
                (s, w, h, v) => Adjustments.Threshold(s, v[0])));
            A("adj.shadows", () => RunFilter(T("Ombre/Luci"),
                new[] { new ParamSpec(T("Ombre"), 0, 100, 35), new ParamSpec(T("Luci"), 0, 100, 0), new ParamSpec(T("Raggio"), 2, 200, 30) },
                (s, w, h, v) => Adjustments.ShadowsHighlights(s, w, h, v[0], v[1], v[2])));
            A("adj.desaturate", () => ApplyInstant(T("Desatura"), (s, w, h) => Adjustments.Desaturate(s)));
            A("adj.sepia", () => ApplyInstant(T("Seppia"), (s, w, h) => Adjustments.Sepia(s)));
            A("adj.autoenhance", AutoEnhance);
            A("adj.autocolor", () => ApplyInstant(T("Colore automatico"), (s, w, h) => Adjustments.AutoColor(s)));
            A("flt.cameraraw", () => CameraRawFilter(false));
            A("flt.ai", () => CameraRawFilter(true));
            A("ai.settings", () => new AiSettingsDialog(this).ShowDialog(), false);
            A("adj.autotone", () => ApplyInstant(T("Tono automatico"), (s, w, h) => Adjustments.AutoLevels(s, true)));
            A("adj.autocontrast", () => ApplyInstant(T("Contrasto automatico"), (s, w, h) => Adjustments.AutoLevels(s, false)));

            // ---- Filters
            A("flt.gauss", () => RunFilter(T("Controllo sfocatura gaussiana"), new[] { new ParamSpec(T("Raggio (pixel)"), 0.1, 250, 2, 1) },
                (s, w, h, v) => Filters.GaussianBlur(s, w, h, v[0])));
            A("flt.motion", () => RunFilter(T("Sfocatura movimento"), new[] { new ParamSpec(T("Angolo"), -90, 90, 0), new ParamSpec(T("Distanza (pixel)"), 1, 200, 15) },
                (s, w, h, v) => Filters.MotionBlur(s, w, h, v[0], v[1])));
            A("flt.sharpen", () => ApplyInstant(T("Contrasta"), Filters.Sharpen));
            A("flt.unsharp", () => RunFilter(T("Maschera di contrasto"),
                new[] { new ParamSpec(T("Fattore %"), 1, 500, 100), new ParamSpec(T("Raggio (pixel)"), 0.1, 250, 1, 1), new ParamSpec(T("Soglia (livelli)"), 0, 255, 0) },
                (s, w, h, v) => Filters.UnsharpMask(s, w, h, v[0], v[1], v[2])));
            A("flt.clarity", () => RunFilter(T("Chiarezza"), new[] { new ParamSpec(T("Quantità"), -100, 100, 35) },
                (s, w, h, v) => Filters.Clarity(s, w, h, v[0])));
            A("flt.noise", () => RunFilter(T("Aggiungi disturbo"), new[] { new ParamSpec(T("Quantità %"), 0, 100, 10, 1), ParamSpec.Check(T("Monocromatico"), false) },
                (s, w, h, v) => Filters.AddNoise(s, w, h, v[0], v[1] > 0)));
            A("flt.pixelate", () => RunFilter(T("Mosaico"), new[] { new ParamSpec(T("Dimensione cella"), 2, 200, 12) },
                (s, w, h, v) => Filters.Pixelate(s, w, h, (int)v[0])));
            A("flt.edges", () => ApplyInstant(T("Trova bordi"), Filters.FindEdges));
            A("flt.emboss", () => RunFilter(T("Rilievo"), new[] { new ParamSpec(T("Fattore %"), 10, 500, 100) },
                (s, w, h, v) => Filters.Emboss(s, w, h, v[0])));
            A("flt.vignette", () => RunFilter(T("Vignettatura"),
                new[] { new ParamSpec(T("Quantità"), -100, 100, -50), new ParamSpec(T("Dimensione"), 0, 100, 50), new ParamSpec(T("Sfumatura"), 1, 100, 50) },
                (s, w, h, v) => Filters.Vignette(s, w, h, v[0], v[1], v[2])));

            // ---- Image
            A("img.resize", ResizeImage);
            A("img.aiupscale", AiUpscaleImage);
            A("img.canvas", CanvasSize);
            A("img.rot180", () => TransformImage(px => ImageOps.Rotate180(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, T("Ruota immagine 180°")));
            A("img.rotcw", () => TransformImage(px => ImageOps.Rotate90(px, Doc.Width, Doc.Height, true), Doc.Height, Doc.Width, T("Ruota immagine 90° orario")));
            A("img.rotccw", () => TransformImage(px => ImageOps.Rotate90(px, Doc.Width, Doc.Height, false), Doc.Height, Doc.Width, T("Ruota immagine 90° antiorario")));
            A("img.fliph", () => TransformImage(px => ImageOps.FlipH(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, T("Rifletti quadro orizzontale")));
            A("img.flipv", () => TransformImage(px => ImageOps.FlipV(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, T("Rifletti quadro verticale")));
            A("img.cropsel", () => { if (S.Selection != null) CropTo(S.Selection.Bounds, T("Ritaglia")); });

            // ---- Layers
            A("layer.new", NewLayer);
            A("layer.duplicate", DuplicateLayer);
            A("layer.delete", DeleteLayer);
            A("layer.rename", RenameLayer);
            A("layer.up", () => MoveLayer(-1));
            A("layer.down", () => MoveLayer(1));
            A("layer.merge", MergeDown);
            A("layer.mergevisible", MergeVisible);
            A("layer.flatten", Flatten);

            // ---- Selection
            A("sel.all", () => SetSelection(Selection.All(Doc.Width, Doc.Height), T("Seleziona tutto")));
            A("sel.none", Deselect);
            A("sel.invert", () => SetSelection(S.Selection == null ? Selection.All(Doc.Width, Doc.Height) : NullIfEmpty(S.Selection.Inverted()), T("Inversa")));
            A("sel.feather", FeatherSelection);

            // ---- View / crop / help
            A("view.zoomin", () => ZoomStep(1));
            A("view.zoomout", () => ZoomStep(-1));
            A("view.fit", () => FitToScreen());
            A("view.actual", () => SetZoom(1));
            A("crop.apply", ApplyCrop);
            A("crop.cancel", CancelCrop);
            A("help.keys", ShowShortcuts, false);
            A("help.about", ShowAbout, false);
            A("help.update", () => CheckForUpdates(true), false);
            A("ai.storage", ShowAiStorage, false);

            // ---- Shortcuts
            const ModifierKeys C = ModifierKeys.Control, Sh = ModifierKeys.Shift, Al = ModifierKeys.Alt, None = ModifierKeys.None;
            K("file.new", C, Key.N); K("file.open", C, Key.O); K("file.place", C | Sh, Key.P);
            K("file.close", C, Key.W); K("file.save", C, Key.S); K("file.saveas", C | Sh, Key.S);
            K("file.export", C | Sh | Al, Key.W); K("file.exit", C, Key.Q);
            K("file.cull", C | Al, Key.O); K("batch.savenext", C, Key.Enter);
            K("dev.copy", Al | Sh, Key.C); K("dev.paste", Al | Sh, Key.V);
            K("dev.smartlight", C | Al, Key.L); K("dev.masks", C | Al, Key.M);
            K("edit.undo", C, Key.Z); K("edit.undo", C | Al, Key.Z); K("edit.redo", C | Sh, Key.Z); K("edit.redo", C, Key.Y);
            K("edit.cut", C, Key.X); K("edit.copy", C, Key.C); K("edit.copymerged", C | Sh, Key.C); K("edit.paste", C, Key.V);
            K("edit.clear", None, Key.Delete, Key.Back);
            K("edit.fillfg", Al, Key.Back); K("edit.fillbg", C, Key.Back);
            K("edit.transform", C, Key.T);
            K("adj.levels", C, Key.L); K("adj.curves", C, Key.M); K("adj.huesat", C, Key.U); K("adj.colorbalance", C, Key.B);
            K("adj.bw", C | Sh | Al, Key.B); K("adj.invert", C, Key.I); K("adj.desaturate", C | Sh, Key.U);
            K("adj.autotone", C | Sh, Key.L); K("adj.autocontrast", C | Sh | Al, Key.L);
            K("adj.autoenhance", C | Sh | Al, Key.A); K("adj.autocolor", C | Sh, Key.B);
            K("flt.cameraraw", C | Sh, Key.A); K("flt.ai", C | Sh, Key.K);
            K("img.resize", C | Al, Key.I); K("img.canvas", C | Al, Key.C);
            K("layer.new", C | Sh, Key.N); K("layer.duplicate", C, Key.J); K("layer.merge", C, Key.E); K("layer.mergevisible", C | Sh, Key.E);
            K("layer.up", C, Key.OemCloseBrackets, Key.OemPeriod); K("layer.down", C, Key.OemOpenBrackets, Key.OemComma);
            K("sel.all", C, Key.A); K("sel.none", C, Key.D); K("sel.invert", C | Sh, Key.I); K("sel.feather", Sh, Key.F6);
            K("view.zoomin", C, Key.OemPlus, Key.Add); K("view.zoomout", C, Key.OemMinus, Key.Subtract);
            K("view.fit", C, Key.D0, Key.NumPad0); K("view.actual", C, Key.D1, Key.NumPad1);
            K("help.keys", None, Key.F1);
        }

        static Selection NullIfEmpty(Selection s) => s == null || s.IsEmpty ? null : s;

        void Run(string key)
        {
            if (!_actions.TryGetValue(key, out var a)) return;
            if (a.NeedsDoc && S == null) return;
            if (_dragging) return;
            a.Run();
        }

        bool CanRun(string key)
        {
            if (!_actions.TryGetValue(key, out var a)) return false;
            if (a.NeedsDoc && S == null) return false;
            if (S == null) return true;
            return key switch
            {
                "edit.undo" => S.History.CanUndo,
                "edit.redo" => S.History.CanRedo,
                "img.cropsel" or "edit.clear" or "edit.cut" or "sel.none" or "sel.feather" => S.Selection != null,
                "layer.merge" => ActiveIndex < Doc.Layers.Count - 1,
                "layer.delete" or "layer.mergevisible" => Doc.Layers.Count > 1,
                "layer.up" => ActiveIndex > 0,
                "layer.down" => ActiveIndex < Doc.Layers.Count - 1,
                "batch.savenext" => BatchItemOf(S) != null,
                _ => true,
            };
        }

        void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string t) Run(t);
        }

        void OptAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string t) Run(t);
        }

        void Menu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem root) UpdateMenuState(root);
        }

        void UpdateMenuState(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<MenuItem>())
            {
                if (item.Tag is string tag) item.IsEnabled = CanRun(tag);
                else if (item.HasItems)
                {
                    UpdateMenuState(item);
                    item.IsEnabled = S != null;
                }
            }
        }

        bool HandleShortcut(Key key, ModifierKeys mods)
        {
            if (_shortcuts.TryGetValue((mods, key), out var action))
            {
                Run(action);
                return true;
            }
            if (mods != ModifierKeys.None && mods != ModifierKeys.Shift) return false;
            switch (key)
            {
                case Key.V: SelectTool(Tool.Move); return true;
                case Key.M: SelectTool(_tool == Tool.RectSelect ? Tool.EllipseSelect : Tool.RectSelect); return true;
                case Key.L: SelectTool(Tool.Lasso); return true;
                case Key.W: SelectTool(Tool.Wand); return true;
                case Key.C: SelectTool(Tool.Crop); return true;
                case Key.I: SelectTool(Tool.Eyedropper); return true;
                case Key.B: SelectTool(Tool.Brush); return true;
                case Key.S: SelectTool(Tool.Clone); return true;
                case Key.E: SelectTool(Tool.Eraser); return true;
                case Key.G: SelectTool(_tool == Tool.Bucket ? Tool.Gradient : Tool.Bucket); return true;
                case Key.O: SelectTool(Tool.Dodge); return true;
                case Key.T: SelectTool(Tool.Text); return true;
                case Key.U: SelectTool(Tool.Shape); return true;
                case Key.H: SelectTool(Tool.Hand); return true;
                case Key.Z: SelectTool(Tool.Zoom); return true;
                case Key.X: SwapColors(); return true;
                case Key.D: ResetColors(); return true;
                case Key.OemOpenBrackets: case Key.OemComma:
                    BrushSize.Value = Math.Max(1, BrushSize.Value - BrushStep(BrushSize.Value - 1)); return true;
                case Key.OemCloseBrackets: case Key.OemPeriod:
                    BrushSize.Value = Math.Min(500, BrushSize.Value + BrushStep(BrushSize.Value)); return true;
                case Key.Enter:
                    if (_cropRect != null) { ApplyCrop(); return true; }
                    return false;
                case Key.Escape:
                    if (_cropRect != null) { CancelCrop(); return true; }
                    return false;
            }
            return false;
        }

        static double BrushStep(double v) => v < 10 ? 1 : v < 50 ? 5 : v < 100 ? 10 : 25;

        // ================= File =================

        void NewDocument()
        {
            var dlg = new NewImageDialog(this, 1920, 1080);
            if (dlg.ShowDialog() != true) return;
            Color? bg = dlg.BackgroundChoice switch
            {
                0 => Colors.White,
                1 => Colors.Black,
                3 => _secondary,
                _ => null,
            };
            var doc = Document.CreateBlank(dlg.PixelWidth, dlg.PixelHeight, bg);
            if (bg == null) doc.Layers[0].Name = T("Livello 1");
            var s = new Session(doc, T("Senza titolo-{0}", _untitled++));
            s.History.Push(T("Nuovo"), doc.Snapshot(0, null));
            AddSession(s);
        }

        void OpenDialog()
        {
            var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Multiselect = true, Title = T("Apri") };
            if (dlg.ShowDialog(this) != true) return;
            // More photos: browse them like a folder (Preselezione). Projects and a single photo open directly.
            var photos = dlg.FileNames.Where(f => PhotoLibrary.IsSupported(f)).ToList();
            if (photos.Count <= 1) photos.Clear();
            foreach (var f in dlg.FileNames.Except(photos)) OpenFile(f);
            if (photos.Count > 1) StartCulling(photos);
        }

        /// <summary>Opens an image (RAW files through Camera Raw). Returns the document, or null if cancelled or failed.</summary>
        /// <param name="settings">Development settings to apply to the original (RAW: the starting values in Camera Raw).</param>
        public Session OpenFile(string path, RawSettings settings = null)
        {
            var existing = _sessions.FirstOrDefault(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { ActivateSession(existing); return existing; }
            if (RawImage.IsRawFile(path)) return OpenRaw(path, settings);
            try
            {
                Document doc;
                bool project = Path.GetExtension(path).Equals(".psx", StringComparison.OrdinalIgnoreCase);
                using (Busy())
                {
                    if (project)
                    {
                        doc = ImageIO.LoadProject(path);
                    }
                    else
                    {
                        var (w, h, px) = ImageIO.LoadBitmap(path);
                        if (settings != null)
                        {
                            var developed = RawDevelop.Develop(RawImage.FromBgra(px, w, h), settings);
                            (w, h, px) = (developed.Width, developed.Height, developed.Pixels);
                        }
                        doc = new Document(w, h);
                        doc.Layers.Add(new Layer(T("Sfondo"), w, h, px));
                    }
                    foreach (var l in doc.Layers) l.UpdateThumbnail();
                }
                var s = new Session(doc, Path.GetFileName(path)) { FilePath = path, SourcePath = path };
                s.History.Push(T("Apri"), doc.Snapshot(0, null));
                if (!project) MarkDeveloped(s, settings);
                AddSession(s);
                Status(T("Aperto: {0}", path));
                return s;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Impossibile aprire il file:\n{0}\n\n{1}", path, ex.Message), "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        void PlaceFile()
        {
            var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = T("Inserisci come livello") };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var (w, h, px) = ImageIO.LoadBitmap(dlg.FileName);
                PlaceImage(w, h, px, Path.GetFileNameWithoutExtension(dlg.FileName), T("Inserisci"), true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Impossibile inserire il file:\n{0}", ex.Message), "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Adds an image as a new layer, centred on the selection (or canvas).</summary>
        void PlaceImage(int w, int h, byte[] px, string name, string historyName, bool fit)
        {
            if (fit && (w > Doc.Width || h > Doc.Height))
            {
                double s = Math.Min((double)Doc.Width / w, (double)Doc.Height / h);
                int nw = Math.Max(1, (int)Math.Round(w * s)), nh = Math.Max(1, (int)Math.Round(h * s));
                using (Busy()) px = ImageOps.Resize(px, w, h, nw, nh);
                w = nw;
                h = nh;
            }
            var b = S.Selection?.Bounds ?? Doc.Bounds;
            int x = b.X + (b.Width - w) / 2, y = b.Y + (b.Height - h) / 2;
            var buf = ImageOps.Crop(px, w, h, new Int32Rect(-x, -y, Doc.Width, Doc.Height));
            InsertLayer(new Layer(name, Doc.Width, Doc.Height, buf), historyName);
        }

        bool Save(bool saveAs)
        {
            if (S == null) return false;
            string path = S.FilePath;
            bool isProject = path != null && path.EndsWith(".psx", StringComparison.OrdinalIgnoreCase);
            if (!saveAs && path != null && !isProject && Doc.Layers.Count > 1)
            {
                var r = MessageBox.Show(this,
                    T("Il documento contiene più livelli.\n\nSì: salva un'immagine appiattita nel file originale\nNo: scegli un altro formato (es. progetto .psx che conserva i livelli)"),
                    T("Salva"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return false;
                if (r == MessageBoxResult.No) saveAs = true;
            }
            if (saveAs || path == null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = ImageIO.SaveFilter,
                    Title = T("Salva con nome"),
                    AddExtension = true,
                    FileName = Path.GetFileNameWithoutExtension(S.Title),
                    FilterIndex = Doc.Layers.Count > 1 ? 1 : 2,
                };
                if (path != null) dlg.InitialDirectory = Path.GetDirectoryName(path);
                if (dlg.ShowDialog(this) != true) return false;
                path = dlg.FileName;
                if (ImageIO.IsJpeg(path) && !AskJpegQuality()) return false;
            }
            if (!WriteFile(path)) return false;
            S.FilePath = path;
            S.Title = Path.GetFileName(path);
            S.Modified = false;
            UpdateTitle();
            Status(T("Salvato: {0}", path));
            return true;
        }

        void ExportImage()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|TIFF (*.tif)|*.tif|BMP (*.bmp)|*.bmp",
                Title = T("Esporta"),
                FileName = Path.GetFileNameWithoutExtension(S.Title),
            };
            if (dlg.ShowDialog(this) != true) return;
            if (ImageIO.IsJpeg(dlg.FileName) && !AskJpegQuality()) return;
            if (WriteFile(dlg.FileName)) Status(T("Esportato: {0}", dlg.FileName));
        }

        bool AskJpegQuality()
        {
            var d = new ParamDialog(this, T("Opzioni JPEG"), new[] { new ParamSpec(T("Qualità"), 1, 100, S.JpegQuality) }, null);
            if (d.ShowDialog() != true) return false;
            S.JpegQuality = (int)d.Values[0];
            return true;
        }

        bool WriteFile(string path)
        {
            try
            {
                using (Busy())
                {
                    if (path.EndsWith(".psx", StringComparison.OrdinalIgnoreCase)) ImageIO.SaveProject(path, Doc);
                    else ImageIO.SaveBitmap(path, Doc.Width, Doc.Height, Doc.Render(), S.JpegQuality, PhotoLibrary.ReadMetadata(S.SourcePath));
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Impossibile salvare il file:\n{0}\n\n{1}", path, ex.Message), "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ================= Help =================

        void ShowShortcuts()
        {
            MessageBox.Show(this,
                T("STRUMENTI\nV Sposta · M Selezione (rett./ellittica) · L Lazo · W Bacchetta magica\nC Taglierina · I Contagocce · B Pennello · S Timbro clone · E Gomma\nG Secchiello / Sfumatura · O Scherma/Brucia · T Testo · U Forme · H Mano · Z Zoom\n[ ] (oppure , .) dimensione pennello · X inverti colori · D colori predefiniti\nSpazio + trascina: scorri · Alt+rotellina o Ctrl+rotellina: zoom\n\nMODIFICA\nCtrl+Z annulla · Shift+Ctrl+Z / Ctrl+Y ripeti · Ctrl+X/C/V taglia/copia/incolla\nCanc cancella selezione · Alt+Backspace riempi primo piano · Ctrl+Backspace riempi sfondo\nCtrl+T trasforma livello\n\nREGOLAZIONI\nCtrl+L Livelli · Ctrl+M Curve · Ctrl+U Tonalità/Saturazione · Ctrl+B Bilanciamento colore\nCtrl+I Inverti · Shift+Ctrl+U Desatura · Shift+Ctrl+L Tono automatico\nShift+Ctrl+B Colore automatico · Alt+Shift+Ctrl+L Contrasto automatico\nAlt+Shift+Ctrl+A Miglioramento automatico · Shift+Ctrl+A Filtro Camera Raw (Ctrl+U Automatico, Ctrl+Maiusc+U bil. bianco) · Shift+Ctrl+K Modifica con AI\nAlt+Ctrl+L Luce intelligente · Alt+Ctrl+M Maschere di luce · in Camera Raw: Y confronto Prima/Dopo\n\nPRESELEZIONE\nAlt+Ctrl+O scegli la cartella · ← → scorri · Canc sposta nel Cestino · Ctrl+Z ripristina · Z zoom 100% · Invio fine\n0-5 stelle · 6-9 etichetta (rosso, giallo, verde, blu) · C confronta 2/4 foto · B confronta la raffica\nCtrl+← → raffica precedente/successiva · H istogramma · J luci bruciate e ombre chiuse\n\nCARTELLA DI MODIFICA\nCtrl+Invio salva la foto nella cartella scelta e passa alla successiva\nAlt+Maiusc+C copia le impostazioni di sviluppo · Alt+Maiusc+V applicale ad altre foto (o un preset)\n\nLIVELLI E SELEZIONE\nShift+Ctrl+N nuovo · Ctrl+J duplica · Ctrl+E unisci sotto · Ctrl+[ ] ordine\nCtrl+A tutto · Ctrl+D deseleziona · Shift+Ctrl+I inversa · Shift+F6 sfuma\n\nVISUALIZZA\nCtrl++ / Ctrl+- zoom · Ctrl+0 adatta · Ctrl+1 100%"),
                T("Scorciatoie da tastiera"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                T("PhotoStudio 1.0\n\nEditor di immagini a livelli per Windows.\nFormati: PNG, JPEG, BMP, TIFF, GIF, WebP/HEIC (se i codec sono installati)\nCamera RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2...) tramite LibRaw\ne progetto .psx con livelli."),
                T("Informazioni su PhotoStudio"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
