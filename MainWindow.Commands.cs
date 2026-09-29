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
            A("edit.layer180", () => TransformActiveLayer(px => ImageOps.Rotate180(px, Doc.Width, Doc.Height), "Ruota livello 180°"));
            A("edit.layerfliph", () => TransformActiveLayer(px => ImageOps.FlipH(px, Doc.Width, Doc.Height), "Rifletti livello orizzontale"));
            A("edit.layerflipv", () => TransformActiveLayer(px => ImageOps.FlipV(px, Doc.Width, Doc.Height), "Rifletti livello verticale"));

            // ---- Adjustments
            A("adj.brightness", () => RunFilter("Luminosità/Contrasto",
                new[] { new ParamSpec("Luminosità", -100, 100, 0), new ParamSpec("Contrasto", -100, 100, 0) },
                (s, w, h, v) => Adjustments.BrightnessContrast(s, v[0], v[1])));
            A("adj.levels", () => RunFilter("Livelli",
                new[]
                {
                    new ParamSpec("Input nero", 0, 253, 0), new ParamSpec("Input bianco", 2, 255, 255),
                    new ParamSpec("Gamma (mezzitoni)", 0.1, 9.99, 1, 2),
                    new ParamSpec("Output nero", 0, 255, 0), new ParamSpec("Output bianco", 0, 255, 255),
                },
                (s, w, h, v) => Adjustments.Levels(s, v[0], Math.Max(v[0] + 2, v[1]), v[2], v[3], v[4])));
            A("adj.curves", RunCurves);
            A("adj.exposure", () => RunFilter("Esposizione",
                new[] { new ParamSpec("Esposizione (EV)", -5, 5, 0, 2), new ParamSpec("Scostamento", -0.5, 0.5, 0, 3), new ParamSpec("Correzione gamma", 0.1, 3, 1, 2) },
                (s, w, h, v) => Adjustments.Exposure(s, v[0], v[1], v[2])));
            A("adj.vibrance", () => RunFilter("Vividezza",
                new[] { new ParamSpec("Vividezza", -100, 100, 0), new ParamSpec("Saturazione", -100, 100, 0) },
                (s, w, h, v) => Adjustments.Vibrance(s, v[0], v[1])));
            A("adj.huesat", () => RunFilter("Tonalità/Saturazione",
                new[] { new ParamSpec("Tonalità", -180, 180, 0), new ParamSpec("Saturazione", -100, 100, 0), new ParamSpec("Luminosità", -100, 100, 0) },
                (s, w, h, v) => Adjustments.HueSaturation(s, v[0], v[1], v[2])));
            A("adj.colorbalance", () => RunFilter("Bilanciamento colore",
                new[]
                {
                    new ParamSpec("Ciano ↔ Rosso", -100, 100, 0), new ParamSpec("Magenta ↔ Verde", -100, 100, 0),
                    new ParamSpec("Giallo ↔ Blu", -100, 100, 0), ParamSpec.Check("Mantieni luminosità", true),
                },
                (s, w, h, v) => Adjustments.ColorBalance(s, v[0], v[1], v[2], v[3] > 0)));
            A("adj.temperature", () => RunFilter("Temperatura colore",
                new[] { new ParamSpec("Temperatura", -100, 100, 0), new ParamSpec("Tinta", -100, 100, 0) },
                (s, w, h, v) => Adjustments.Temperature(s, v[0], v[1])));
            A("adj.bw", () => RunFilter("Bianco e nero",
                new[] { new ParamSpec("Rossi %", -200, 300, 30), new ParamSpec("Verdi %", -200, 300, 59), new ParamSpec("Blu %", -200, 300, 11) },
                (s, w, h, v) => Adjustments.BlackWhite(s, v[0], v[1], v[2])));
            A("adj.invert", () => ApplyInstant("Inverti", (s, w, h) => Adjustments.Invert(s)));
            A("adj.posterize", () => RunFilter("Posterizza", new[] { new ParamSpec("Livelli", 2, 64, 4) },
                (s, w, h, v) => Adjustments.Posterize(s, v[0])));
            A("adj.threshold", () => RunFilter("Soglia", new[] { new ParamSpec("Livello soglia", 1, 255, 128) },
                (s, w, h, v) => Adjustments.Threshold(s, v[0])));
            A("adj.shadows", () => RunFilter("Ombre/Luci",
                new[] { new ParamSpec("Ombre", 0, 100, 35), new ParamSpec("Luci", 0, 100, 0), new ParamSpec("Raggio", 2, 200, 30) },
                (s, w, h, v) => Adjustments.ShadowsHighlights(s, w, h, v[0], v[1], v[2])));
            A("adj.desaturate", () => ApplyInstant("Desatura", (s, w, h) => Adjustments.Desaturate(s)));
            A("adj.sepia", () => ApplyInstant("Seppia", (s, w, h) => Adjustments.Sepia(s)));
            A("adj.autoenhance", AutoEnhance);
            A("adj.autocolor", () => ApplyInstant("Colore automatico", (s, w, h) => Adjustments.AutoColor(s)));
            A("flt.cameraraw", () => CameraRawFilter(false));
            A("flt.ai", () => CameraRawFilter(true));
            A("ai.settings", () => new AiSettingsDialog(this).ShowDialog(), false);
            A("adj.autotone", () => ApplyInstant("Tono automatico", (s, w, h) => Adjustments.AutoLevels(s, true)));
            A("adj.autocontrast", () => ApplyInstant("Contrasto automatico", (s, w, h) => Adjustments.AutoLevels(s, false)));

            // ---- Filters
            A("flt.gauss", () => RunFilter("Controllo sfocatura gaussiana", new[] { new ParamSpec("Raggio (pixel)", 0.1, 250, 2, 1) },
                (s, w, h, v) => Filters.GaussianBlur(s, w, h, v[0])));
            A("flt.motion", () => RunFilter("Sfocatura movimento", new[] { new ParamSpec("Angolo", -90, 90, 0), new ParamSpec("Distanza (pixel)", 1, 200, 15) },
                (s, w, h, v) => Filters.MotionBlur(s, w, h, v[0], v[1])));
            A("flt.sharpen", () => ApplyInstant("Contrasta", Filters.Sharpen));
            A("flt.unsharp", () => RunFilter("Maschera di contrasto",
                new[] { new ParamSpec("Fattore %", 1, 500, 100), new ParamSpec("Raggio (pixel)", 0.1, 250, 1, 1), new ParamSpec("Soglia (livelli)", 0, 255, 0) },
                (s, w, h, v) => Filters.UnsharpMask(s, w, h, v[0], v[1], v[2])));
            A("flt.clarity", () => RunFilter("Chiarezza", new[] { new ParamSpec("Quantità", -100, 100, 35) },
                (s, w, h, v) => Filters.Clarity(s, w, h, v[0])));
            A("flt.noise", () => RunFilter("Aggiungi disturbo", new[] { new ParamSpec("Quantità %", 0, 100, 10, 1), ParamSpec.Check("Monocromatico", false) },
                (s, w, h, v) => Filters.AddNoise(s, w, h, v[0], v[1] > 0)));
            A("flt.pixelate", () => RunFilter("Mosaico", new[] { new ParamSpec("Dimensione cella", 2, 200, 12) },
                (s, w, h, v) => Filters.Pixelate(s, w, h, (int)v[0])));
            A("flt.edges", () => ApplyInstant("Trova bordi", Filters.FindEdges));
            A("flt.emboss", () => RunFilter("Rilievo", new[] { new ParamSpec("Fattore %", 10, 500, 100) },
                (s, w, h, v) => Filters.Emboss(s, w, h, v[0])));
            A("flt.vignette", () => RunFilter("Vignettatura",
                new[] { new ParamSpec("Quantità", -100, 100, -50), new ParamSpec("Dimensione", 0, 100, 50), new ParamSpec("Sfumatura", 1, 100, 50) },
                (s, w, h, v) => Filters.Vignette(s, w, h, v[0], v[1], v[2])));

            // ---- Image
            A("img.resize", ResizeImage);
            A("img.canvas", CanvasSize);
            A("img.rot180", () => TransformImage(px => ImageOps.Rotate180(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, "Ruota immagine 180°"));
            A("img.rotcw", () => TransformImage(px => ImageOps.Rotate90(px, Doc.Width, Doc.Height, true), Doc.Height, Doc.Width, "Ruota immagine 90° orario"));
            A("img.rotccw", () => TransformImage(px => ImageOps.Rotate90(px, Doc.Width, Doc.Height, false), Doc.Height, Doc.Width, "Ruota immagine 90° antiorario"));
            A("img.fliph", () => TransformImage(px => ImageOps.FlipH(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, "Rifletti quadro orizzontale"));
            A("img.flipv", () => TransformImage(px => ImageOps.FlipV(px, Doc.Width, Doc.Height), Doc.Width, Doc.Height, "Rifletti quadro verticale"));
            A("img.cropsel", () => { if (S.Selection != null) CropTo(S.Selection.Bounds, "Ritaglia"); });

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
            A("sel.all", () => SetSelection(Selection.All(Doc.Width, Doc.Height), "Seleziona tutto"));
            A("sel.none", Deselect);
            A("sel.invert", () => SetSelection(S.Selection == null ? Selection.All(Doc.Width, Doc.Height) : NullIfEmpty(S.Selection.Inverted()), "Inversa"));
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
            if (bg == null) doc.Layers[0].Name = "Livello 1";
            var s = new Session(doc, $"Senza titolo-{_untitled++}");
            s.History.Push("Nuovo", doc.Snapshot(0, null));
            AddSession(s);
        }

        void OpenDialog()
        {
            var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Multiselect = true, Title = "Apri" };
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
                        doc.Layers.Add(new Layer("Sfondo", w, h, px));
                    }
                    foreach (var l in doc.Layers) l.UpdateThumbnail();
                }
                var s = new Session(doc, Path.GetFileName(path)) { FilePath = path, SourcePath = path };
                s.History.Push("Apri", doc.Snapshot(0, null));
                if (!project) MarkDeveloped(s, settings);
                AddSession(s);
                Status("Aperto: " + path);
                return s;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile aprire il file:\n{path}\n\n{ex.Message}", "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        void PlaceFile()
        {
            var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = "Inserisci come livello" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var (w, h, px) = ImageIO.LoadBitmap(dlg.FileName);
                PlaceImage(w, h, px, Path.GetFileNameWithoutExtension(dlg.FileName), "Inserisci", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossibile inserire il file:\n" + ex.Message, "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "Il documento contiene più livelli.\n\nSì: salva un'immagine appiattita nel file originale\nNo: scegli un altro formato (es. progetto .psx che conserva i livelli)",
                    "Salva", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return false;
                if (r == MessageBoxResult.No) saveAs = true;
            }
            if (saveAs || path == null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = ImageIO.SaveFilter,
                    Title = "Salva con nome",
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
            Status("Salvato: " + path);
            return true;
        }

        void ExportImage()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|TIFF (*.tif)|*.tif|BMP (*.bmp)|*.bmp",
                Title = "Esporta",
                FileName = Path.GetFileNameWithoutExtension(S.Title),
            };
            if (dlg.ShowDialog(this) != true) return;
            if (ImageIO.IsJpeg(dlg.FileName) && !AskJpegQuality()) return;
            if (WriteFile(dlg.FileName)) Status("Esportato: " + dlg.FileName);
        }

        bool AskJpegQuality()
        {
            var d = new ParamDialog(this, "Opzioni JPEG", new[] { new ParamSpec("Qualità", 1, 100, S.JpegQuality) }, null);
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
                MessageBox.Show(this, $"Impossibile salvare il file:\n{path}\n\n{ex.Message}", "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ================= Help =================

        void ShowShortcuts()
        {
            MessageBox.Show(this,
                "STRUMENTI\n" +
                "V Sposta · M Selezione (rett./ellittica) · L Lazo · W Bacchetta magica\n" +
                "C Taglierina · I Contagocce · B Pennello · S Timbro clone · E Gomma\n" +
                "G Secchiello / Sfumatura · O Scherma/Brucia · T Testo · U Forme · H Mano · Z Zoom\n" +
                "[ ] (oppure , .) dimensione pennello · X inverti colori · D colori predefiniti\n" +
                "Spazio + trascina: scorri · Alt+rotellina o Ctrl+rotellina: zoom\n\n" +
                "MODIFICA\n" +
                "Ctrl+Z annulla · Shift+Ctrl+Z / Ctrl+Y ripeti · Ctrl+X/C/V taglia/copia/incolla\n" +
                "Canc cancella selezione · Alt+Backspace riempi primo piano · Ctrl+Backspace riempi sfondo\n" +
                "Ctrl+T trasforma livello\n\n" +
                "REGOLAZIONI\n" +
                "Ctrl+L Livelli · Ctrl+M Curve · Ctrl+U Tonalità/Saturazione · Ctrl+B Bilanciamento colore\n" +
                "Ctrl+I Inverti · Shift+Ctrl+U Desatura · Shift+Ctrl+L Tono automatico\n" +
                "Shift+Ctrl+B Colore automatico · Alt+Shift+Ctrl+L Contrasto automatico\n" +
                "Alt+Shift+Ctrl+A Miglioramento automatico · Shift+Ctrl+A Filtro Camera Raw (Ctrl+U Automatico, Ctrl+Maiusc+U bil. bianco) · Shift+Ctrl+K Modifica con AI\n" +
                "Alt+Ctrl+L Luce intelligente · Alt+Ctrl+M Maschere di luce · in Camera Raw: Y confronto Prima/Dopo\n\n" +
                "PRESELEZIONE\n" +
                "Alt+Ctrl+O scegli la cartella · ← → scorri · Canc sposta nel Cestino · Ctrl+Z ripristina · Z zoom 100% · Invio fine\n" +
                "0-5 stelle · 6-9 etichetta (rosso, giallo, verde, blu) · C confronta 2/4 foto · B confronta la raffica\n" +
                "Ctrl+← → raffica precedente/successiva · H istogramma · J luci bruciate e ombre chiuse\n\n" +
                "CARTELLA DI MODIFICA\n" +
                "Ctrl+Invio salva la foto nella cartella scelta e passa alla successiva\n" +
                "Alt+Maiusc+C copia le impostazioni di sviluppo · Alt+Maiusc+V applicale ad altre foto (o un preset)\n\n" +
                "LIVELLI E SELEZIONE\n" +
                "Shift+Ctrl+N nuovo · Ctrl+J duplica · Ctrl+E unisci sotto · Ctrl+[ ] ordine\n" +
                "Ctrl+A tutto · Ctrl+D deseleziona · Shift+Ctrl+I inversa · Shift+F6 sfuma\n\n" +
                "VISUALIZZA\n" +
                "Ctrl++ / Ctrl+- zoom · Ctrl+0 adatta · Ctrl+1 100%",
                "Scorciatoie da tastiera", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                "PhotoStudio 1.0\n\nEditor di immagini a livelli per Windows.\n" +
                "Formati: PNG, JPEG, BMP, TIFF, GIF, WebP/HEIC (se i codec sono installati)\n" +
                "Camera RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2...) tramite LibRaw\n" +
                "e progetto .psx con livelli.",
                "Informazioni su PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
