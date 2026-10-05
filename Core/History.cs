using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PhotoStudio.Core
{
    public sealed class LayerState
    {
        public string Name;
        public bool Visible;
        public double Opacity;
        public BlendMode Blend;
        public FrozenPixels Pixels; // shared, immutable, in memory or on disk
    }

    public sealed class DocState
    {
        public int Width, Height, ActiveIndex;
        public List<LayerState> Layers = new List<LayerState>();
        public Selection Selection;
    }

    public sealed class HistoryEntry : INotifyPropertyChanged
    {
        bool _future;
        public string Name { get; init; }
        public DocState State { get; init; }

        public bool IsFuture
        {
            get => _future;
            set
            {
                if (_future == value) return;
                _future = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFuture)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Snapshot-based undo history. Unchanged layers share pixel buffers between states.</summary>
    public sealed class History
    {
        public ObservableCollection<HistoryEntry> Entries { get; } = new ObservableCollection<HistoryEntry>();
        public int Index { get; private set; } = -1;
        public bool CanUndo => Index > 0;
        public bool CanRedo => Index < Entries.Count - 1;

        public void Push(string name, DocState state)
        {
            while (Entries.Count > Index + 1) Entries.RemoveAt(Entries.Count - 1);
            Entries.Add(new HistoryEntry { Name = name, State = state });

            // Keep memory bounded: roughly 1.5 GB worth of full-size layer copies, between 10 and 50 steps.
            long bytesPerState = Math.Max(1L, (long)state.Width * state.Height * 4);
            int limit = (int)Math.Clamp(1_500_000_000L / bytesPerState, 10, 50);
            while (Entries.Count > limit) Entries.RemoveAt(0);

            Index = Entries.Count - 1;
            Refresh();
        }

        public DocState Undo() => CanUndo ? JumpTo(Index - 1) : null;
        public DocState Redo() => CanRedo ? JumpTo(Index + 1) : null;

        public DocState JumpTo(int i)
        {
            if (i < 0 || i >= Entries.Count || i == Index) return null;
            Index = i;
            Refresh();
            return Entries[i].State;
        }

        void Refresh()
        {
            for (int i = 0; i < Entries.Count; i++) Entries[i].IsFuture = i > Index;
        }
    }
}
