using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using PhotoStudio.Core;

namespace PhotoStudio
{
    // The memory of the open photos: at most PixelSwap.Budget, the rest goes to disk and comes back when needed.
    public partial class MainWindow
    {
        const long ReleaseThreshold = 256L << 20;
        DispatcherTimer _releaseTimer;
        long _allocatedAtRelease;

        void InitMemory()
        {
            // When the memory runs out anyway (a very large photo, the AI): everything not on screen goes to disk now.
            Errors.MemoryShort += () => Dispatcher.Invoke(() => TrimMemory(true));
            _releaseTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
            _releaseTimer.Tick += (s, e) =>
            {
                _releaseTimer.Stop();
                Errors.FreeMemory();
                _allocatedAtRelease = GC.GetTotalAllocatedBytes();
            };
        }

        /// <summary>
        /// Keeps the open photos within the memory budget. What exceeds it goes to disk in the background, starting
        /// from the tabs used least recently and, in each tab, from the history steps farthest from the current one.
        /// Nothing is lost: the tabs stay open and every step can still be undone.
        /// </summary>
        /// <param name="now">Moves to disk everything that can go, and waits (the memory has run out).</param>
        void TrimMemory(bool now = false)
        {
            CollectIfMuchAllocated();
            long used = 0;
            foreach (var s in _sessions) used += s.Doc.WorkingBytes;
            var order = _sessions.Where(s => s != S).OrderBy(s => s.LastUsed).ToList();
            if (S != null) order.Add(S);
            var blocks = new List<FrozenPixels>();
            var seen = new HashSet<FrozenPixels>(ReferenceEqualityComparer.Instance);
            foreach (var s in order)
                foreach (var b in s.FrozenBlocks())
                    if (seen.Add(b))
                    {
                        blocks.Add(b);
                        if (b.InMemory) used += b.Length;
                    }

            long excess = used - (now ? 0 : PixelSwap.Budget);
            var move = new List<FrozenPixels>();
            foreach (var b in blocks)
            {
                if (excess <= 0) break;
                if (!b.InMemory) continue;
                move.Add(b);
                excess -= b.Length;
            }
            if (now) PixelSwap.SwapOutNow(move);
            else if (move.Count > 0) PixelSwap.SwapOutLater(move);
            ReleaseMemorySoon(false);
            ScheduleBackup();
        }

        /// <summary>
        /// The memory of what has been freed (pixels moved to disk, a full-size development, a closed tab, the
        /// copies made by undo) is kept by .NET for later, and Windows counts it as used. Once PhotoStudio has been
        /// idle for a few seconds it is given back, with a short full collection (0.1-0.4 s), when at least 256 MB
        /// of large images have been made since the last time.
        /// </summary>
        /// <param name="always">After a big job (a development, a closed tab): no need to count.</param>
        void ReleaseMemorySoon(bool always = true)
        {
            if (!always && GC.GetTotalAllocatedBytes() - _allocatedAtRelease < ReleaseThreshold) return;
            _releaseTimer.Stop();
            _releaseTimer.Start();
        }

        long _allocatedAtGen2;
        int _gen2Count = -1;

        /// <summary>
        /// Working without pause (going through many photos) leaves behind a lot of large images: after 1 GB
        /// allocated without a full collection, a quick one (no compaction, a few ms), so the next images reuse
        /// that space instead of asking Windows for more. Giving the memory back waits for the idle moment.
        /// </summary>
        void CollectIfMuchAllocated()
        {
            long allocated = GC.GetTotalAllocatedBytes();
            int count = GC.CollectionCount(2);
            if (count != _gen2Count) { _gen2Count = count; _allocatedAtGen2 = allocated; return; }
            if (allocated - _allocatedAtGen2 < 1L << 30) return;
            GC.Collect(2, GCCollectionMode.Forced, true, false);
            _gen2Count = GC.CollectionCount(2);
            _allocatedAtGen2 = allocated;
        }
    }
}
