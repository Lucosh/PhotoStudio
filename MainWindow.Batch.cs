using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;

namespace PhotoStudio
{
    public partial class MainWindow
    {
        // "Cartella di modifica": the photos kept in the Preselezione, opened and edited one at a time.
        readonly ObservableCollection<PhotoItem> _batch = new ObservableCollection<PhotoItem>();
        ThumbnailLoader _batchThumbs;
        string _batchFolder;
        bool _batchSyncing;
        /// <summary>What is remembered about the folder being edited (stars, progress): saved as the work goes on.</summary>
        FolderState _batchState;
        /// <summary>"Copia impostazioni" (Alt+Maiusc+C).</summary>
        RawSettings _copiedSettings;
        ExportOptions _export = ExportOptions.Load();

        /// <summary>Preselezione: choose a folder, browse its photos, delete the rejects, then edit the others.</summary>
        public void StartCulling(string folder = null)
        {
            if (folder == null)
            {
                var dlg = new OpenFolderDialog { Title = "Scegli la cartella con le foto da selezionare" };
                if (_batchFolder != null && Directory.Exists(_batchFolder)) dlg.InitialDirectory = _batchFolder;
                if (dlg.ShowDialog(this) != true) return;
                folder = dlg.FolderName;
            }
            var state = FolderState.Load(folder);
            if (state.HasPendingBatch)
            {
                int total = state.Batch.Photos.Count, saved = state.Batch.Photos.Count(p => p.Saved);
                var answer = MessageBox.Show(this,
                    $"In questa cartella avevi lasciato a metà la modifica: {saved} di {total} foto salvate.\n\n" +
                    "Sì: riprendi la modifica da dove eri rimasto\nNo: fai una nuova preselezione (stelle ed etichette restano)",
                    "Riprendi il lavoro", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) { ResumeBatch(folder, state); return; }
            }
            List<PhotoItem> photos;
            try
            {
                SortedDictionary<string, int> counts;
                using (Busy()) counts = PhotoLibrary.CountExtensions(folder);
                HashSet<string> extensions = null;
                if (counts.Count > 1)
                {
                    // More formats (e.g. CR2 + JPG): let the user choose which ones to work on.
                    var formats = new FormatDialog(this, counts);
                    if (formats.ShowDialog() != true) return;
                    extensions = formats.Extensions;
                }
                using (Busy()) photos = PhotoLibrary.Scan(folder, extensions);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile leggere la cartella:\n{folder}\n\n{ex.Message}", "Preselezione", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (photos.Count == 0)
            {
                MessageBox.Show(this, $"Nella cartella non ci sono foto:\n{folder}\n\nFormati riconosciuti: JPEG, PNG, TIFF, HEIC e RAW (CR2, CR3, NEF, ARW, DNG...).",
                    "Preselezione", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RunCulling(folder, photos, state);
        }

        /// <summary>Preselezione of a few photos chosen with "Apri..." (all from the same folder).</summary>
        void StartCulling(IReadOnlyList<string> files)
        {
            var photoFiles = files.Where(PhotoLibrary.IsSupported).ToList();
            var counts = PhotoLibrary.CountExtensions(photoFiles);
            if (counts.Count > 1)
            {
                var formats = new FormatDialog(this, counts);
                if (formats.ShowDialog() != true) return;
                photoFiles = photoFiles.Where(f => formats.Extensions.Contains(Path.GetExtension(f))).ToList();
            }
            if (photoFiles.Count == 0) return;
            string folder = Path.GetDirectoryName(photoFiles[0]);
            RunCulling(folder, PhotoLibrary.Group(photoFiles), FolderState.Load(folder));
        }

        void RunCulling(string folder, List<PhotoItem> photos, FolderState state)
        {
            state.ApplyMarks(photos);
            var window = new CullingWindow(this, folder, photos, state);
            bool ok = window.ShowDialog() == true && window.Kept.Count > 0;
            string deleted = window.DeletedCount == 1 ? "1 foto spostata nel Cestino" : window.DeletedCount > 1 ? $"{window.DeletedCount} foto spostate nel Cestino" : "";
            if (!ok)
            {
                Status(deleted.Length > 0 ? "Preselezione: " + deleted + "." : "");
                return;
            }
            foreach (var p in window.Kept) { p.Session = null; p.OutputPath = null; p.State = PhotoState.Pending; p.Settings = null; }
            StartBatch(folder, window.Kept, window.OutputFolder, state);
            Status($"Preselezione completata: {window.Kept.Count} foto da modificare" + (deleted.Length > 0 ? ", " + deleted + "." : "."));
        }

        /// <summary>Reopens the editing left half-way in this folder (saved photos, settings and output folder).</summary>
        void ResumeBatch(string folder, FolderState state)
        {
            var photos = new List<PhotoItem>();
            foreach (var saved in state.Batch.Photos)
            {
                var files = saved.Files.Where(File.Exists).ToList();
                if (files.Count == 0) continue;
                var item = new PhotoItem(Path.GetFileNameWithoutExtension(files[0]), files) { Settings = saved.Settings };
                if (saved.Saved && saved.OutputPath != null && File.Exists(saved.OutputPath))
                {
                    item.OutputPath = saved.OutputPath;
                    item.State = PhotoState.Saved;
                }
                photos.Add(item);
            }
            if (photos.Count == 0) { StartCulling(folder); return; }
            state.ApplyMarks(photos);
            StartBatch(folder, photos, state.Batch.OutputFolder ?? Path.Combine(folder, "Modificate"), state);
            Status($"Modifica ripresa: {photos.Count(p => p.State == PhotoState.Saved)} di {photos.Count} foto già salvate.");
        }

        void StartBatch(string folder, List<PhotoItem> photos, string outputFolder, FolderState state)
        {
            _batchThumbs?.Stop();
            _batch.Clear();
            foreach (var p in photos) _batch.Add(p);
            _batchFolder = folder;
            _batchState = state;
            BatchList.ItemsSource = _batch;
            BatchOutput.Text = outputFolder;
            BatchPanel.Visibility = Visibility.Visible;
            _batchThumbs = new ThumbnailLoader(_batch, 1);
            _batchThumbs.Kick();
            UpdateBatchTitle();
            UpdateExportTooltip();
            SaveBatchState();
            OpenBatchItem(_batch.FirstOrDefault(p => p.State != PhotoState.Saved) ?? _batch[0]);
        }

        /// <summary>Remembers the editing progress, so it can be resumed after closing PhotoStudio.</summary>
        void SaveBatchState()
        {
            if (_batchState == null) return;
            _batchState.Batch = new BatchState
            {
                OutputFolder = BatchOutput.Text.Trim(),
                Photos = _batch.Select(i => new BatchPhotoState
                {
                    Files = i.Files.ToList(), OutputPath = i.OutputPath, Saved = i.State == PhotoState.Saved, Settings = i.Settings,
                }).ToList(),
            };
            _batchState.Save();
        }

        PhotoItem BatchItemOf(Session s) => s == null ? null : _batch.FirstOrDefault(i => i.Session == s);

        void OpenBatchItem(PhotoItem item)
        {
            if (item == null) return;
            if (item.Session != null && _sessions.Contains(item.Session))
            {
                ActivateSession(item.Session);
                return;
            }
            if (!File.Exists(item.EditPath))
            {
                MessageBox.Show(this, "Il file non esiste più:\n" + item.EditPath, "Cartella di modifica", MessageBoxButton.OK, MessageBoxImage.Warning);
                SyncBatchSelection();
                return;
            }
            var s = OpenFile(item.EditPath, item.Settings);   // RAW files go through Camera Raw first
            if (s == null)
            {
                SyncBatchSelection();
                return;
            }
            item.Session = s;
            item.State = PhotoState.Open;
            if (s.RawSettings != null) item.Settings = s.RawSettings.Clone();
            SyncBatchSelection();
            SaveBatchState();
        }

        void BatchMove(int delta)
        {
            if (_batch.Count == 0) return;
            var current = BatchItemOf(S) ?? BatchList.SelectedItem as PhotoItem;
            int i = current == null ? (delta > 0 ? 0 : _batch.Count - 1) : _batch.IndexOf(current) + delta;
            if (i < 0) { Status("Sei alla prima foto della cartella di modifica."); return; }
            if (i >= _batch.Count) { Status("Sei all'ultima foto della cartella di modifica."); return; }
            OpenBatchItem(_batch[i]);
        }

        /// <summary>Highlights the photo shown in the active tab.</summary>
        void SyncBatchSelection()
        {
            if (_batch.Count == 0) return;
            var item = BatchItemOf(S);
            _batchSyncing = true;
            BatchList.SelectedItem = item;
            _batchSyncing = false;
            if (item != null)
            {
                BatchList.ScrollIntoView(item);
                if (_batchThumbs != null) _batchThumbs.Focus = _batch.IndexOf(item);
            }
            UpdateBatchTitle();
        }

        void OnSessionClosed(Session s)
        {
            foreach (var item in _batch.Where(i => i.Session == s))
            {
                item.Session = null;
                item.State = item.OutputPath != null && File.Exists(item.OutputPath) ? PhotoState.Saved : PhotoState.Pending;
            }
            UpdateBatchTitle();
        }

        void UpdateBatchTitle()
        {
            if (_batch.Count == 0) return;
            int saved = _batch.Count(i => i.State == PhotoState.Saved);
            var current = BatchItemOf(S);
            string position = current != null ? $"foto {_batch.IndexOf(current) + 1} di {_batch.Count}" : $"{_batch.Count} foto";
            string name = Path.GetFileName((_batchFolder ?? "").TrimEnd('\\', '/'));
            BatchTitle.Text = $"CARTELLA DI MODIFICA — {name}   ·   {position}   ·   {saved} salvate";
        }

        // ---------- saving ----------

        string BatchExtension => BatchFormat.SelectedIndex switch { 1 => ".tif", 2 => ".png", _ => ".jpg" };

        bool EnsureOutputFolder(out string dir)
        {
            dir = BatchOutput.Text.Trim();
            try
            {
                if (dir.Length == 0 || !Path.IsPathRooted(dir)) throw new ArgumentException("Il percorso deve essere completo, ad esempio C:\\Foto\\Modificate.");
                dir = Path.GetFullPath(dir);
                Directory.CreateDirectory(dir);
                BatchOutput.Text = dir;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Scegli una cartella valida in cui salvare le foto modificate (campo \"Salva in\").\n\n" + ex.Message,
                    "Cartella di modifica", MessageBoxButton.OK, MessageBoxImage.Warning);
                BatchOutput.Focus();
                return false;
            }
        }

        /// <summary>A file name in the output folder that never overwrites an existing file (e.g. the camera JPEG).</summary>
        static string UniqueOutputPath(string dir, string name, string ext)
        {
            string candidate = Path.Combine(dir, name + ext);
            if (!File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, name + "_modificata" + ext);
            for (int n = 2; File.Exists(candidate); n++)
                candidate = Path.Combine(dir, $"{name}_modificata-{n}{ext}");
            return candidate;
        }

        int BatchQualityValue()
        {
            if (!int.TryParse(BatchQuality.Text.Trim(), out int quality)) quality = 92;
            quality = Math.Clamp(quality, 1, 100);
            BatchQuality.Text = quality.ToString();
            return quality;
        }

        /// <summary>
        /// Writes a finished photo to the output folder with the export options (size, name, watermark).
        /// Returns null when done, otherwise the error message.
        /// </summary>
        string ExportPhoto(PhotoItem item, int w, int h, byte[] px, string dir)
        {
            string ext = BatchExtension;
            int quality = BatchQualityValue();
            string name = _export.FileName(item.Name, Math.Max(0, _batch.IndexOf(item)));
            // Saving the same photo again overwrites our own previous output, never someone else's file.
            string path = item.OutputPath != null
                          && string.Equals(Path.GetDirectoryName(item.OutputPath), dir, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(Path.GetExtension(item.OutputPath), ext, StringComparison.OrdinalIgnoreCase)
                          && Path.GetFileNameWithoutExtension(item.OutputPath).StartsWith(name, StringComparison.OrdinalIgnoreCase)
                ? item.OutputPath
                : UniqueOutputPath(dir, name, ext);
            try
            {
                var original = px;
                px = _export.ApplySize(px, ref w, ref h);
                if (_export.Watermark && ReferenceEquals(px, original)) px = (byte[])px.Clone();   // never draw on the document's pixels
                _export.ApplyWatermark(px, w, h);
                ImageIO.SaveBitmap(path, w, h, px, quality, PhotoLibrary.ReadMetadata(item.EditPath));
            }
            catch (Exception ex)
            {
                return $"{Path.GetFileName(path)}: {ex.Message}";
            }
            item.OutputPath = path;
            item.State = PhotoState.Saved;
            return null;
        }

        bool SaveBatchItem(PhotoItem item, string dir)
        {
            var s = item.Session;
            if (s == null || !_sessions.Contains(s)) return false;
            string error;
            using (Busy()) error = ExportPhoto(item, s.Doc.Width, s.Doc.Height, s.Doc.Render(), dir);
            if (error != null)
            {
                MessageBox.Show(this, "Impossibile salvare il file:\n" + error, "Cartella di modifica", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (s.RawSettings != null) item.Settings = s.RawSettings.Clone();
            s.JpegQuality = BatchQualityValue();
            s.Modified = false;
            UpdateBatchTitle();
            SaveBatchState();
            return true;
        }

        /// <summary>Ctrl+Invio: saves the photo in the output folder, closes it and opens the next one to edit.</summary>
        void BatchSaveNext()
        {
            var item = BatchItemOf(S);
            if (item == null)
            {
                MessageBox.Show(this, _batch.Count == 0
                        ? "Non c'è una cartella di modifica: usa File ▸ Preselezione cartella."
                        : "La foto aperta non fa parte della cartella di modifica: scegli una foto dalla striscia in basso.",
                    "Cartella di modifica", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!EnsureOutputFolder(out var dir) || !SaveBatchItem(item, dir)) return;
            var session = item.Session;
            item.Session = null;
            CloseSession(session);
            Status("Salvata: " + item.OutputPath);

            int index = _batch.IndexOf(item);
            var next = _batch.Skip(index + 1).FirstOrDefault(i => i.State != PhotoState.Saved)
                       ?? _batch.Take(index).FirstOrDefault(i => i.State != PhotoState.Saved);
            if (next != null) OpenBatchItem(next);
            else BatchFinished(dir);
        }

        void BatchSaveAll()
        {
            var open = _batch.Where(i => i.Session != null && _sessions.Contains(i.Session)).ToList();
            if (open.Count == 0)
            {
                MessageBox.Show(this, "Nessuna foto della cartella di modifica è aperta: aprile dalla striscia in basso.",
                    "Cartella di modifica", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!EnsureOutputFolder(out var dir)) return;
            int saved = open.Count(item => SaveBatchItem(item, dir));
            int pending = _batch.Count(i => i.State != PhotoState.Saved);
            if (pending == 0) BatchFinished(dir);
            else Status($"{saved} foto salvate in {dir}. Restano {pending} foto da modificare.");
        }

        void BatchFinished(string dir)
        {
            UpdateBatchTitle();
            int saved = _batch.Count(i => i.State == PhotoState.Saved);
            if (MessageBox.Show(this, $"Hai finito: {saved} foto salvate in\n{dir}\n\nAprire la cartella?", "Cartella di modifica",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }

        // ---------- panel events ----------

        void BatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_batchSyncing || BatchList.SelectedItem is not PhotoItem item) return;
            // Opening may show Camera Raw: do it once the click has been processed.
            Dispatcher.BeginInvoke(new Action(() => OpenBatchItem(item)));
        }

        void BatchBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Dove salvare le foto modificate" };
            string current = BatchOutput.Text.Trim();
            if (Directory.Exists(current)) dlg.InitialDirectory = current;
            else if (_batchFolder != null) dlg.InitialDirectory = _batchFolder;
            if (dlg.ShowDialog(this) == true) BatchOutput.Text = dlg.FolderName;
        }

        void BatchFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BatchQualityPanel != null) BatchQualityPanel.Visibility = BatchFormat.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void BatchClose_Click(object sender, RoutedEventArgs e)
        {
            int pending = _batch.Count(i => i.State != PhotoState.Saved);
            if (pending > 0 && MessageBox.Show(this,
                    $"Chiudere la cartella di modifica?\n\n{pending} foto non sono ancora state salvate. Le schede aperte restano aperte.\n" +
                    "Potrai riprendere da dove eri rimasto riaprendo la stessa cartella con la Preselezione.",
                    "Cartella di modifica", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            if (_batchState != null)
            {
                if (pending == 0) { _batchState.Batch = null; _batchState.Save(); }
                else SaveBatchState();
            }
            _batchThumbs?.Stop();
            _batchThumbs = null;
            _batch.Clear();
            _batchState = null;
            BatchPanel.Visibility = Visibility.Collapsed;
        }

        // ---------- development settings: copy, paste, presets ----------

        /// <summary>Alt+Maiusc+C: copies the Camera Raw settings of the open photo.</summary>
        void CopySettings()
        {
            var settings = S?.RawSettings ?? BatchItemOf(S)?.Settings;
            if (settings == null)
            {
                MessageBox.Show(this,
                    "Questa foto non è stata sviluppata con Camera Raw, quindi non ci sono impostazioni da copiare.\n\n" +
                    "Apri Camera Raw (Maiusc+Ctrl+A), regola i cursori e premi OK, poi copia le impostazioni.",
                    "Copia impostazioni", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _copiedSettings = settings.Clone();
            Status("Impostazioni copiate: " + settings.Describe() + ".  Alt+Maiusc+V per applicarle ad altre foto.");
        }

        /// <summary>Alt+Maiusc+V: applies copied settings or a preset to the open photo or to the editing folder.</summary>
        async void ApplySettings()
        {
            bool hasOpen = S != null && S.SourcePath != null;
            var pendingItems = _batch.Where(i => i.State != PhotoState.Saved).ToList();
            if (_copiedSettings == null && RawPreset.LoadAll().Count == 0)
            {
                MessageBox.Show(this,
                    "Non ci sono impostazioni da applicare.\n\nCopia quelle di una foto (Alt+Maiusc+C) oppure salva un preset in Camera Raw.",
                    "Applica impostazioni", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new ApplySettingsDialog(this, _copiedSettings, hasOpen, pendingItems.Count);
            if (dlg.ShowDialog() != true) return;

            if (dlg.ToOpenPhoto)
            {
                ApplySettingsToSession(S, dlg.Settings, dlg.Groups);
                return;
            }
            var targets = pendingItems.Where(i => i.Rating >= dlg.MinRating).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Nessuna foto da salvare ha abbastanza stelle.", "Applica impostazioni", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RawSettings Merged(PhotoItem i) =>
                (i.Settings ?? RawSettings.Default(RawImage.IsRawFile(i.EditPath))).MergeFrom(dlg.Settings, dlg.Groups);

            if (!dlg.SaveNow)
            {
                foreach (var i in targets)
                {
                    if (i.Session != null && _sessions.Contains(i.Session)) ApplySettingsToSession(i.Session, dlg.Settings, dlg.Groups, quiet: true);
                    else i.Settings = Merged(i);
                }
                SaveBatchState();
                Status($"Impostazioni pronte per {targets.Count} foto: le trovi applicate quando le apri.");
                return;
            }

            if (!EnsureOutputFolder(out var dir)) return;
            var result = await ProcessPhotos(targets, "Sviluppo e salvataggio", dir, true,
                (src, item) => Merged(item),
                session => ApplySettingsToSession(session, dlg.Settings, dlg.Groups, quiet: true));
            ReportProcessed(result, dir, true, "Applica impostazioni");
        }

        sealed class ProcessResult
        {
            public int Done, Skipped;
            public List<string> Errors = new List<string>();
            public bool Cancelled;
        }

        /// <summary>
        /// Develops each photo from its original with its own settings, in the background, with a progress window.
        /// saveNow: also writes it to the output folder; otherwise the settings become its starting point.
        /// Photos open in a tab go through applyToOpen (on the UI thread), and only if they have no other edits.
        /// </summary>
        async Task<ProcessResult> ProcessPhotos(List<PhotoItem> targets, string title, string dir, bool saveNow,
                                                Func<RawImage, PhotoItem, RawSettings> settingsFor, Func<Session, bool> applyToOpen)
        {
            var r = new ProcessResult();
            var progress = new ProgressWindow(this, title, targets.Count);
            IsEnabled = false;
            progress.Show();
            try
            {
                foreach (var item in targets)
                {
                    if (progress.Cancelled) { r.Cancelled = true; break; }
                    progress.Report(r.Done, $"{r.Done + 1} di {targets.Count}: {item.Name}");
                    var session = item.Session != null && _sessions.Contains(item.Session) ? item.Session : null;
                    if (session != null)
                    {
                        // Open photo: only when it has no other edits, so nothing done by hand is lost.
                        if (!session.IsAsDeveloped || !applyToOpen(session)) r.Skipped++;
                        else if (saveNow && !SaveBatchItem(item, dir)) r.Errors.Add(item.Name);
                        r.Done++;
                        continue;
                    }
                    try
                    {
                        var (settings, w, h, px) = await Task.Run(() =>
                        {
                            var src = LoadSourceLinear(item.EditPath);
                            var st = settingsFor(src, item);
                            if (!saveNow) return (st, 0, 0, (byte[])null);
                            var d = RawDevelop.Develop(src, st);
                            return (st, d.Width, d.Height, d.Pixels);
                        });
                        item.Settings = settings;
                        if (saveNow)
                        {
                            string error = ExportPhoto(item, w, h, px, dir);
                            if (error != null) r.Errors.Add(error);
                        }
                    }
                    catch (Exception ex)
                    {
                        r.Errors.Add($"{item.Name}: {ex.Message}");
                    }
                    r.Done++;
                    SaveBatchState();
                }
            }
            finally
            {
                progress.Finish();
                IsEnabled = true;
                Activate();
                UpdateBatchTitle();
                SaveBatchState();
            }
            return r;
        }

        void ReportProcessed(ProcessResult r, string dir, bool saved, string title)
        {
            string summary = saved
                ? $"{r.Done - r.Skipped - r.Errors.Count} foto sviluppate e salvate in {dir}."
                : $"{r.Done - r.Skipped - r.Errors.Count} foto pronte: le trovi già sistemate quando le apri.";
            if (r.Skipped > 0) summary += $"\n{r.Skipped} foto aperte con modifiche a mano non sono state toccate.";
            if (r.Errors.Count > 0) summary += "\n\nNon riuscite:\n" + string.Join("\n", r.Errors.Take(8)) + (r.Errors.Count > 8 ? "\n..." : "");
            if (r.Cancelled) summary = "Interrotto.\n" + summary;
            if (saved && _batch.All(i => i.State == PhotoState.Saved)) { BatchFinished(dir); if (r.Errors.Count == 0 && r.Skipped == 0) return; }
            MessageBox.Show(this, summary, title, MessageBoxButton.OK, r.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // ---------- Luce intelligente ----------

        double _smartIntensity = 100;

        /// <summary>Alt+Ctrl+L: fixes the light of the open photo, or of every photo of the editing folder.</summary>
        async void SmartLightCommand()
        {
            var pending = _batch.Where(i => i.State != PhotoState.Saved).ToList();
            if (S == null && pending.Count == 0)
            {
                MessageBox.Show(this, "Apri una foto, oppure fai una Preselezione per sistemare la luce di tutte le foto di una cartella.",
                    "Luce intelligente", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new SmartLightDialog(this, S != null, pending.Count, _smartIntensity);
            if (dlg.ShowDialog() != true) return;
            _smartIntensity = dlg.Intensity;
            double k = dlg.Intensity;
            if (dlg.ToOpenPhoto)
            {
                SmartLightOnSession(S, k, quiet: false);
                return;
            }
            var targets = pending.Where(i => i.Rating >= dlg.MinRating).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Nessuna foto da salvare ha abbastanza stelle.", "Luce intelligente", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string dir = null;
            if (dlg.SaveNow && !EnsureOutputFolder(out dir)) return;
            var result = await ProcessPhotos(targets, "Luce intelligente", dir, dlg.SaveNow,
                (src, item) => SmartLight.Apply(src, item.Settings ?? RawSettings.Default(src.SceneReferred), k),
                session => SmartLightOnSession(session, k, quiet: true));
            ReportProcessed(result, dir, dlg.SaveNow, "Luce intelligente");
        }

        /// <summary>
        /// Luce intelligente on an open photo: from the original when it has no other edits (the masks stay
        /// editable in Camera Raw), otherwise on the active layer.
        /// </summary>
        bool SmartLightOnSession(Session s, double intensity, bool quiet)
        {
            if (s == null) return false;
            if (s != S) ActivateSession(s);
            RawSettings settings;
            try
            {
                if (s.IsAsDeveloped && s.SourcePath != null && File.Exists(s.SourcePath))
                {
                    (int Width, int Height, byte[] Pixels) d;
                    using (Busy())
                    {
                        var src = OriginalOf(s);
                        settings = SmartLight.Apply(src, s.RawSettings ?? RawSettings.Default(src.SceneReferred), intensity);
                        d = RawDevelop.Develop(src, settings);
                    }
                    ReplaceDeveloped(d.Width, d.Height, d.Pixels, "Luce intelligente");
                    MarkDeveloped(s, settings);
                    var item = BatchItemOf(s);
                    if (item != null) { item.Settings = settings.Clone(); SaveBatchState(); }
                }
                else
                {
                    var layer = ActiveLayer;
                    if (layer == null) return false;
                    var original = layer.Pixels;
                    using (Busy())
                    {
                        var linear = RawImage.FromBgra(original, Doc.Width, Doc.Height);
                        settings = SmartLight.Apply(linear, RawSettings.Default(false), intensity);
                        layer.Pixels = ImageOps.ApplyMask(original, RawDevelop.Render(linear, settings), S.Selection);
                    }
                    Recomposite();
                    Commit("Luce intelligente");
                }
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show(this, "Luce intelligente non riuscita:\n" + ex.Message, "Luce intelligente", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!quiet)
                Status("Luce intelligente: " + settings.Describe() + ".   Ctrl+Z per annullare · Alt+Ctrl+M per ritoccare le maschere di luce.");
            return true;
        }

        /// <summary>Alt+Ctrl+M: Camera Raw opened directly on the (beginner friendly) Maschere tab.</summary>
        void LightMasksCommand()
        {
            if (S == null)
            {
                MessageBox.Show(this, "Apri prima una foto: le maschere di luce ritoccano solo una parte della foto (le ombre, le luci, il cielo...).",
                    "Maschere di luce", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            CameraRawFilter(false, "Maschere");
        }

        void EditExportOptions()
        {
            var sample = BatchItemOf(S)?.Name ?? _batch.FirstOrDefault()?.Name;
            var dlg = new ExportOptionsDialog(this, _export, sample);
            if (dlg.ShowDialog() != true) return;
            _export = dlg.Options;
            try { _export.Save(); } catch { }
            UpdateExportTooltip();
            Status("Opzioni di esportazione: " + _export.Describe());
        }

        void UpdateExportTooltip()
        {
            if (BatchExportButton != null) BatchExportButton.ToolTip = "Dimensione, nome dei file e filigrana delle foto salvate\nAdesso: " + _export.Describe();
        }
    }
}
