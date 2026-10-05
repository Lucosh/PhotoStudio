using System;
using System.Linq;

namespace PhotoStudio.Core
{
    public static class Errors
    {
        /// <summary>
        /// True when the memory ran out, also inside the AggregateException of a parallel loop (which wraps the
        /// error of each of its threads).
        /// </summary>
        public static bool IsOutOfMemory(Exception ex) =>
            ex is OutOfMemoryException ||
            ex is AggregateException a && a.Flatten().InnerExceptions.All(x => x is OutOfMemoryException);

        /// <summary>
        /// Runs the work; if the memory runs out, frees all that can be freed (the open photos go to disk, the AI
        /// networks close) and runs it once more. Used where giving up would cost work: saving above all.
        /// </summary>
        public static T Retry<T>(Func<T> work)
        {
            try { return work(); }
            catch (Exception ex) when (IsOutOfMemory(ex))
            {
                ReleaseMemory();
                return work();
            }
        }

        /// <summary>Raised (on any thread) when the memory has run out: whoever holds photos moves them to disk.</summary>
        public static event Action MemoryShort;

        /// <summary>
        /// After running out of memory: asks for the open photos to go to disk, then gives the freed memory back to
        /// Windows, so a new attempt can find room.
        /// </summary>
        public static void ReleaseMemory()
        {
            try { MemoryShort?.Invoke(); } catch { }
            FreeMemory();
        }

        /// <summary>Gives back to Windows the memory of buffers already freed.</summary>
        public static void FreeMemory()
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        }
    }
}
