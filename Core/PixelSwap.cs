using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Pixels that never change (a step of the history, the copy a layer is compared with): kept in memory, or moved
    /// to a file on disk when the memory is needed, and read back when they are used again. The file of the current
    /// state of each tab is also its safety copy (see <see cref="Recovery"/>).
    /// </summary>
    public sealed class FrozenPixels
    {
        byte[] _data;
        SwapFile _file;
        byte[] _hash;   // SHA-256 of the pixels, from when they were written to disk

        public FrozenPixels(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            Length = data.Length;
        }

        FrozenPixels(SwapFile file)
        {
            _file = file;
            Length = file.Length;
        }

        /// <summary>Pixels that are only on disk (a tab recovered after a crash).</summary>
        internal static FrozenPixels OnDisk(SwapFile file) => new FrozenPixels(file);

        public int Length { get; }
        public bool InMemory => Volatile.Read(ref _data) != null;

        /// <summary>The file on disk, once written (null before).</summary>
        internal SwapFile File => Volatile.Read(ref _file);

        /// <summary>The pixels, read from disk and kept in memory if they had been moved there. Never change them.</summary>
        public byte[] Get()
        {
            var d = Volatile.Read(ref _data);
            if (d != null) return d;
            lock (this)
            {
                d = _data ??= _file.Read(Length);
                return d;
            }
        }

        /// <summary>The pixels for reading once: from disk they are not kept in memory. Never change them.</summary>
        public byte[] Peek() => Volatile.Read(ref _data) ?? Volatile.Read(ref _file).Read(Length);

        /// <summary>
        /// True when these pixels are the same: compared directly when in memory, otherwise by their SHA-256, so
        /// nothing is read back from disk.
        /// </summary>
        public bool SameAs(byte[] px)
        {
            if (px == null || px.Length != Length) return false;
            if (Volatile.Read(ref _data) is byte[] d) return px.AsSpan().SequenceEqual(d);
            var h = Volatile.Read(ref _hash);
            return h != null ? SHA256.HashData(px).AsSpan().SequenceEqual(h) : px.AsSpan().SequenceEqual(Peek());
        }

        /// <summary>A copy that can be changed (a layer starts from it).</summary>
        public byte[] Copy() => Volatile.Read(ref _data) is byte[] d ? (byte[])d.Clone() : Volatile.Read(ref _file).Read(Length);

        /// <summary>
        /// Writes the pixels to disk if they are not there yet, keeping them in memory. Null when the disk cannot
        /// take them (full, not writable).
        /// </summary>
        internal SwapFile EnsureOnDisk()
        {
            var file = Volatile.Read(ref _file);
            if (file != null) return file;
            var d = Volatile.Read(ref _data);
            if (d == null) return Volatile.Read(ref _file);
            file = SwapFile.Write(d);
            if (file == null) return null;
            var hash = SHA256.HashData(d);
            lock (this)
            {
                if (_file == null) { _file = file; _hash = hash; }
                else file.Delete();   // written twice at the same time: one is enough
                return _file;
            }
        }

        /// <summary>
        /// Frees the memory: the pixels are written to disk the first time, then only dropped (the file stays valid,
        /// as the pixels never change). False when the disk cannot take them.
        /// </summary>
        internal bool SwapOut()
        {
            if (Volatile.Read(ref _data) == null) return true;
            if (EnsureOnDisk() == null) return false;
            lock (this) _data = null;
            return true;
        }
    }

    /// <summary>
    /// One block of pixels on disk, written and read in pieces on all the processor cores. An opaque image (alpha
    /// always 255, as every developed photo) is stored without the alpha (file .rgb, otherwise .rgba): a quarter less
    /// to write and read. Each PhotoStudio has its own folder; a file is deleted when its pixels are no longer used
    /// and, at a normal exit, with the whole folder. After a crash the files stay, for <see cref="Recovery"/>.
    /// </summary>
    sealed class SwapFile
    {
        const int ChunkPixels = 1 << 20;
        public const string AliveFile = "running";
        readonly SafeFileHandle _handle;
        readonly bool _opaque;
        int _deleted;

        SwapFile(SafeFileHandle handle, string path, bool opaque, int length)
        {
            _handle = handle; Path = path; _opaque = opaque; Length = length;
        }

        ~SwapFile() => Delete();

        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path);
        public int Length { get; }

        /// <summary>Where every PhotoStudio keeps its folder.</summary>
        public static string Root => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoStudio", "Swap");

        static readonly object FolderLock = new object();
        static string _folder;
        static FileStream _alive;

        /// <summary>
        /// The folder of this PhotoStudio. A file inside stays open as long as it runs: another PhotoStudio that can
        /// open it knows that this one has ended.
        /// </summary>
        public static string Folder
        {
            get
            {
                lock (FolderLock)
                {
                    if (_folder != null) return _folder;
                    var dir = System.IO.Path.Combine(Root, $"{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
                    Directory.CreateDirectory(dir);
                    _alive = new FileStream(System.IO.Path.Combine(dir, AliveFile), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    return _folder = dir;
                }
            }
        }

        /// <summary>True when the PhotoStudio that owns this folder is no longer running.</summary>
        public static bool IsAbandoned(string dir)
        {
            if (string.Equals(dir, _folder, StringComparison.OrdinalIgnoreCase)) return false;
            string alive = System.IO.Path.Combine(dir, AliveFile);
            if (!System.IO.File.Exists(alive)) return true;
            try { using (new FileStream(alive, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static SwapFile Write(byte[] data)
        {
            bool opaque = data.Length % 4 == 0 && IsOpaque(data);
            const int chunkBytes = ChunkPixels * 4;
            int pixels = data.Length / 4;
            int count = opaque ? (pixels + ChunkPixels - 1) / ChunkPixels : (data.Length + chunkBytes - 1) / chunkBytes;
            SafeFileHandle handle = null;
            string path = null;
            try
            {
                path = System.IO.Path.Combine(Folder, Guid.NewGuid().ToString("N") + (opaque ? ".rgb" : ".rgba"));
                handle = Open(path, FileMode.CreateNew, opaque ? (long)pixels * 3 : data.Length);
                var h = handle;
                Parallel.For(0, count, i =>
                {
                    if (!opaque)
                    {
                        int start = i * chunkBytes;
                        RandomAccess.Write(h, data.AsSpan(start, Math.Min(chunkBytes, data.Length - start)), start);
                        return;
                    }
                    int p0 = i * ChunkPixels, n = Math.Min(ChunkPixels, pixels - p0);
                    var buf = ArrayPool<byte>.Shared.Rent(n * 3);
                    try
                    {
                        for (int p = 0, s = p0 * 4, d = 0; p < n; p++, s += 4, d += 3)
                        {
                            buf[d] = data[s]; buf[d + 1] = data[s + 1]; buf[d + 2] = data[s + 2];
                        }
                        RandomAccess.Write(h, buf.AsSpan(0, n * 3), (long)p0 * 3);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buf);
                    }
                });
                RandomAccess.FlushToDisk(handle);   // a safety copy must survive a crash of Windows too
                return new SwapFile(handle, path, opaque, data.Length);
            }
            catch
            {
                handle?.Dispose();
                if (path != null) try { System.IO.File.Delete(path); } catch { }
                return null;   // disk full or not writable: the pixels stay in memory
            }
        }

        /// <summary>Takes over a file left by a PhotoStudio that crashed (moved into the folder of this one).</summary>
        public static SwapFile Adopt(string path)
        {
            bool opaque = path.EndsWith(".rgb", StringComparison.OrdinalIgnoreCase);
            string mine = System.IO.Path.Combine(Folder, System.IO.Path.GetFileName(path));
            try { System.IO.File.Move(path, mine); } catch { mine = path; }
            var handle = Open(mine, FileMode.Open, 0);
            long size = RandomAccess.GetLength(handle);
            return new SwapFile(handle, mine, opaque, (int)(opaque ? size / 3 * 4 : size));
        }

        static SafeFileHandle Open(string path, FileMode mode, long size) =>
            System.IO.File.OpenHandle(path, mode, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, FileOptions.RandomAccess, size);

        /// <summary>Closes and deletes the file (its pixels are no longer used).</summary>
        public void Delete()
        {
            if (Interlocked.Exchange(ref _deleted, 1) != 0) return;
            try { _handle.Dispose(); } catch { }
            try { System.IO.File.Delete(Path); } catch { }
            GC.SuppressFinalize(this);
        }

        static bool IsOpaque(byte[] data)
        {
            int pixels = data.Length / 4;
            bool opaque = true;
            Parallel.For(0, (pixels + ChunkPixels - 1) / ChunkPixels, (i, state) =>
            {
                for (int p = i * ChunkPixels, end = Math.Min(pixels, p + ChunkPixels); p < end; p++)
                    if (data[p * 4 + 3] != 255) { opaque = false; state.Stop(); return; }
            });
            return opaque;
        }

        public byte[] Read(int length)
        {
            var data = new byte[length];
            if (!_opaque)
            {
                ReadExactly(data.AsSpan(), 0);
                return data;
            }
            int pixels = length / 4, count = (pixels + ChunkPixels - 1) / ChunkPixels;
            Parallel.For(0, count, i =>
            {
                int p0 = i * ChunkPixels, n = Math.Min(ChunkPixels, pixels - p0);
                var buf = ArrayPool<byte>.Shared.Rent(n * 3);
                try
                {
                    ReadExactly(buf.AsSpan(0, n * 3), (long)p0 * 3);
                    for (int p = 0, s = 0, d = p0 * 4; p < n; p++, s += 3, d += 4)
                    {
                        data[d] = buf[s]; data[d + 1] = buf[s + 1]; data[d + 2] = buf[s + 2]; data[d + 3] = 255;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            });
            return data;
        }

        void ReadExactly(Span<byte> dst, long offset)
        {
            while (dst.Length > 0)
            {
                int n = RandomAccess.Read(_handle, dst, offset);
                if (n <= 0) throw new EndOfStreamException();
                dst = dst.Slice(n);
                offset += n;
            }
        }
    }

    /// <summary>
    /// How much memory the open photos may use, and the background work that moves the excess to disk, one block at
    /// a time, in the order given (the least useful first).
    /// </summary>
    public static class PixelSwap
    {
        static readonly object Gate = new object();
        static List<FrozenPixels> _pending = new List<FrozenPixels>();
        static Task _worker = Task.CompletedTask;
        static DateTime _diskFailedUntil;

        /// <summary>
        /// The memory the open photos may take: a quarter of the RAM, at least 1 GB. Beyond that the copies that are
        /// not on screen go to disk, so PhotoStudio leaves room to the developing, to the AI and to the other programs.
        /// </summary>
        public static long Budget
        {
            get
            {
                long ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                return Math.Max(1L << 30, ram / 4);
            }
        }

        /// <summary>Moves these blocks to disk in the background, replacing any request not yet done.</summary>
        public static void SwapOutLater(List<FrozenPixels> blocks)
        {
            lock (Gate)
            {
                _pending = blocks;
                if (!_worker.IsCompleted) return;
                _worker = Task.Run(Work);
            }
        }

        /// <summary>Moves these blocks to disk now (when the memory has run out), then gives the memory back to Windows.</summary>
        public static void SwapOutNow(List<FrozenPixels> blocks)
        {
            lock (Gate) _pending = new List<FrozenPixels>();
            _worker.Wait();
            Swap(blocks);
            Errors.FreeMemory();
        }

        static void Work()
        {
            while (true)
            {
                List<FrozenPixels> blocks;
                lock (Gate)
                {
                    blocks = _pending;
                    _pending = new List<FrozenPixels>();
                    if (blocks.Count == 0) { _worker = Task.CompletedTask; return; }
                }
                Swap(blocks);
            }
        }

        static long Swap(List<FrozenPixels> blocks)
        {
            long freed = 0;
            foreach (var b in blocks)
            {
                if (DateTime.UtcNow < _diskFailedUntil) break;
                if (!b.InMemory) continue;
                if (b.SwapOut()) freed += b.Length;
                else _diskFailedUntil = DateTime.UtcNow.AddMinutes(1);
            }
            return freed;
        }
    }
}
