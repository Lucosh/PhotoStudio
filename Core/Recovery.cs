using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>A layer of a tab in the safety copy.</summary>
    public sealed class RecoveryLayer
    {
        public string Name { get; set; }
        public bool Visible { get; set; } = true;
        public double Opacity { get; set; } = 100;
        public BlendMode Blend { get; set; }
        /// <summary>The file with the pixels, in the folder of the copy (null when the disk could not take it).</summary>
        public string File { get; set; }
    }

    /// <summary>An open tab in the safety copy: what is needed to open it again as it was.</summary>
    public sealed class RecoveryTab
    {
        /// <summary>Position among the tabs (the copy is written in another order: the most valuable first).</summary>
        public int Order { get; set; }
        public string Title { get; set; }
        public string SourcePath { get; set; }
        public string FilePath { get; set; }
        /// <summary>The development settings, when the tab is the original developed with them.</summary>
        public RawSettings Developed { get; set; }
        public bool Modified { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ActiveIndex { get; set; }
        public int JpegQuality { get; set; } = 92;
        public List<RecoveryLayer> Layers { get; set; } = new List<RecoveryLayer>();
    }

    /// <summary>A tab to write (the pixels go with its layers, in order) or one read back from disk.</summary>
    public sealed record RecoveryItem(RecoveryTab Tab, FrozenPixels[] Pixels);

    /// <summary>
    /// The safety copy of the open tabs: the current state of each one (its layers, as they were after the last
    /// change) stays on disk, so that nothing is lost if PhotoStudio or Windows stops without warning or the memory
    /// runs out. The pixels are the same files used to free the memory (see <see cref="FrozenPixels"/>): what is
    /// already on disk is not written again. At a normal exit the copy is deleted; after a crash PhotoStudio offers
    /// to open the tabs again at the next start.
    /// </summary>
    public static class Recovery
    {
        const string IndexFile = "tabs.json";
        static readonly object Gate = new object();
        static List<RecoveryItem> _pending;
        static Task _worker = Task.CompletedTask;
        static bool _closed;

        /// <summary>Raised (on a worker thread) after each copy: false when some tab could not be written (disk full).</summary>
        public static event Action<bool> Written;

        /// <summary>
        /// Writes the copy of these tabs in the background, in the order given (the most valuable first), replacing
        /// any copy not yet written.
        /// </summary>
        public static void SaveLater(List<RecoveryItem> tabs)
        {
            lock (Gate)
            {
                if (_closed) return;
                _pending = tabs;
                if (!_worker.IsCompleted) return;
                _worker = Task.Run(Work);
            }
        }

        static void Work()
        {
            while (true)
            {
                List<RecoveryItem> tabs;
                lock (Gate)
                {
                    tabs = _pending;
                    _pending = null;
                    if (tabs == null || _closed) { _worker = Task.CompletedTask; return; }
                }
                bool ok = true;
                try { ok = Write(tabs); }
                catch { ok = false; }
                try { Written?.Invoke(ok); } catch { }
            }
        }

        /// <summary>
        /// Writes the pixels not yet on disk, tab by tab, and the list of the tabs that are safe: at once for those
        /// already on disk, then at least every second, so a crash in the middle loses as little as possible. A newer
        /// request interrupts this one (what is written stays and is not written again).
        /// </summary>
        static bool Write(List<RecoveryItem> tabs)
        {
            bool ok = true;
            var safe = new List<RecoveryTab>();
            var todo = new List<RecoveryItem>();
            foreach (var item in tabs)
            {
                if (item.Pixels.All(p => p.File != null)) { SetFiles(item); safe.Add(item.Tab); }
                else todo.Add(item);
            }
            WriteIndex(safe, tabs.Count);
            var sinceIndex = System.Diagnostics.Stopwatch.StartNew();
            foreach (var item in todo)
            {
                lock (Gate) if (_closed || _pending != null) return ok;
                if (SetFiles(item)) safe.Add(item.Tab); else ok = false;
                if (sinceIndex.ElapsedMilliseconds >= 1000) { WriteIndex(safe, tabs.Count); sinceIndex.Restart(); }
            }
            if (todo.Count > 0) WriteIndex(safe, tabs.Count);
            return ok;
        }

        /// <summary>Puts the pixels of the tab on disk; false when the disk could not take them all.</summary>
        static bool SetFiles(RecoveryItem item)
        {
            bool all = true;
            for (int i = 0; i < item.Pixels.Length; i++)
            {
                var file = item.Pixels[i].EnsureOnDisk();
                item.Tab.Layers[i].File = file?.Name;
                if (file == null) all = false;
            }
            return all;
        }

        static void WriteIndex(List<RecoveryTab> safe, int total)
        {
            lock (Gate) if (_closed) return;
            string dir = SwapFile.Folder, tmp = Path.Combine(dir, IndexFile + ".tmp");
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(f, safe.OrderBy(t => t.Order).ToList(), FolderState.Json);
                f.Flush(true);
            }
            File.Move(tmp, Path.Combine(dir, IndexFile), true);
        }

        /// <summary>At a normal exit: no copy is needed any more.</summary>
        public static void Clear()
        {
            lock (Gate) { _closed = true; _pending = null; }
            try { _worker.Wait(TimeSpan.FromSeconds(5)); } catch { }
            string dir = SwapFile.Folder;
            try { File.Delete(Path.Combine(dir, IndexFile)); } catch { }
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (!Path.GetFileName(f).Equals(SwapFile.AliveFile, StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// The copies left by a PhotoStudio that stopped without closing normally, with the number of tabs in each.
        /// The folders without tabs are removed.
        /// </summary>
        public static List<(string Folder, int Tabs)> FindAbandoned()
        {
            var found = new List<(string, int)>();
            try
            {
                if (!Directory.Exists(SwapFile.Root)) return found;
                foreach (var dir in Directory.GetDirectories(SwapFile.Root))
                {
                    if (!SwapFile.IsAbandoned(dir)) continue;
                    int n = ReadIndex(dir)?.Count ?? 0;
                    if (n > 0) found.Add((dir, n));
                    else try { Directory.Delete(dir, true); } catch { }
                }
                // Files of an older PhotoStudio, kept directly in the root.
                foreach (var f in Directory.GetFiles(SwapFile.Root))
                    try { File.Delete(f); } catch { }
            }
            catch { }
            return found;
        }

        static List<RecoveryTab> ReadIndex(string dir)
        {
            try
            {
                string path = Path.Combine(dir, IndexFile);
                return File.Exists(path) ? JsonSerializer.Deserialize<List<RecoveryTab>>(File.ReadAllText(path), FolderState.Json) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Takes over the tabs of these copies: their pixels move into the folder of this PhotoStudio and stay on
        /// disk until they are shown. A tab whose files are missing is skipped (counted in <paramref name="lost"/>).
        /// </summary>
        public static List<RecoveryItem> Load(IEnumerable<string> folders, out int lost)
        {
            var list = new List<RecoveryItem>();
            lost = 0;
            foreach (var dir in folders)
            {
                foreach (var tab in ReadIndex(dir) ?? new List<RecoveryTab>())
                {
                    try
                    {
                        if (tab.Layers.Count == 0 || tab.Layers.Any(l => l.File == null || !File.Exists(Path.Combine(dir, l.File))))
                        {
                            lost++;
                            continue;
                        }
                        var px = tab.Layers.Select(l => FrozenPixels.OnDisk(SwapFile.Adopt(Path.Combine(dir, l.File)))).ToArray();
                        if (px.Any(p => p.Length != tab.Width * tab.Height * 4)) { lost++; continue; }
                        list.Add(new RecoveryItem(tab, px));
                    }
                    catch
                    {
                        lost++;
                    }
                }
                Discard(dir);
            }
            return list.OrderBy(i => i.Tab.Order).ToList();
        }

        /// <summary>Deletes a copy (what has been taken over has already moved out of it).</summary>
        public static void Discard(string folder)
        {
            try { Directory.Delete(folder, true); } catch { }
        }
    }
}
