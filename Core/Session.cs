using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoStudio.Core
{
    /// <summary>An open document tab: image, undo history, selection and view state.</summary>
    public sealed class Session : INotifyPropertyChanged
    {
        string _title;
        bool _modified, _isActive;

        public Session(Document doc, string title)
        {
            Doc = doc;
            _title = title;
        }

        public Document Doc { get; }
        public History History { get; } = new History();
        public Selection Selection { get; set; }
        public string FilePath { get; set; }
        /// <summary>The photo this document was opened from (also for RAW files, whose FilePath stays empty).</summary>
        public string SourcePath { get; set; }
        public int ActiveIndex { get; set; }
        public int JpegQuality { get; set; } = 92;
        /// <summary>Grows each time the tab is shown: the most recently used tabs have the highest values.</summary>
        public long LastUsed { get; set; }

        /// <summary>
        /// The copies of the photo that can go to disk, the least likely to be needed first: the history steps
        /// farthest from the current one, then the copies the layers are compared with.
        /// </summary>
        public IEnumerable<FrozenPixels> FrozenBlocks()
        {
            int current = History.Index;
            var entries = new List<HistoryEntry>(History.Entries);
            var order = new List<int>();
            for (int i = 0; i < entries.Count; i++) order.Add(i);
            order.Sort((a, b) => Math.Abs(b - current).CompareTo(Math.Abs(a - current)));
            foreach (int i in order)
                foreach (var ls in entries[i].State.Layers)
                    if (ls.Pixels != null) yield return ls.Pixels;
            foreach (var l in Doc.Layers)
                if (l.Snapshot != null) yield return l.Snapshot;
        }

        // History steps that are "the original developed with these settings" (null settings = the original as it is).
        readonly Dictionary<HistoryEntry, RawSettings> _developed = new Dictionary<HistoryEntry, RawSettings>();
        RawSettings _lastDeveloped;

        HistoryEntry CurrentEntry => History.Index >= 0 && History.Index < History.Entries.Count ? History.Entries[History.Index] : null;

        /// <summary>Records that the current history step is the original file developed with these settings.</summary>
        public void MarkDeveloped(RawSettings settings)
        {
            var entry = CurrentEntry;
            if (entry == null) return;
            _developed[entry] = settings?.Clone();
            _lastDeveloped = settings?.Clone();
        }

        /// <summary>
        /// Development settings of the photo: those of the current step when it is a development (so Ctrl+Z also
        /// restores them), otherwise the last ones used. Null = never developed.
        /// </summary>
        public RawSettings RawSettings => CurrentEntry is HistoryEntry e && _developed.TryGetValue(e, out var s) ? s : _lastDeveloped;

        /// <summary>
        /// True while the document is exactly the developed original (no other edits): Camera Raw and
        /// "Applica impostazioni" can then develop the original file again instead of the pixels.
        /// </summary>
        public bool IsAsDeveloped => CurrentEntry is HistoryEntry e && _developed.ContainsKey(e) && Doc.Layers.Count == 1;

        // View state restored when switching tabs (Zoom <= 0 means "fit on first show").
        public double Zoom { get; set; } = -1;
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }

        public string Title { get => _title; set { _title = value; Raise(); Raise(nameof(DisplayTitle)); } }
        public bool Modified { get => _modified; set { if (_modified != value) { _modified = value; Raise(); Raise(nameof(DisplayTitle)); } } }
        public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; Raise(); } } }
        public string DisplayTitle => _modified ? _title + " *" : _title;

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
