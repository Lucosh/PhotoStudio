using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>Where the AI refocus works: on the subject (faces and the person under them, or the centre) or everywhere.</summary>
    public enum RefocusArea { Subject, Whole }

    /// <summary>
    /// AI noise reduction and refocus, applied to the linear RAW data before the development, like Lightroom's
    /// Denoise: the networks see the photo once (the result is kept for each size of the image), and the sliders
    /// only mix the result with the original, so moving them is immediate.
    /// </summary>
    public static class AiRestore
    {
        const int Tile = 512, Overlap = 48;

        /// <summary>
        /// Raised (on a worker thread) while a network works on a photo: what it is doing and how far it got, 0..1.
        /// </summary>
        public static event Action<string, double> Progress;

        /// <summary>True when the settings ask for an AI tool whose network is installed.</summary>
        public static bool Wanted(RawSettings s) =>
            (s.AiDenoise >= 1 && AiModels.Denoise.Installed) || (s.AiRefocus >= 1 && AiModels.Deblur.Installed);

        /// <summary>True when the work for these settings is already done on this image, so developing it is quick.</summary>
        public static bool Ready(RawImage img, RawSettings s)
        {
            bool dn = s.AiDenoise >= 1 && AiModels.Denoise.Installed, rf = s.AiRefocus >= 1 && AiModels.Deblur.Installed;
            return (!dn || img.Restore.Has(img, "dn")) && (!rf || img.Restore.Has(img, dn ? "rf+dn" : "rf"));
        }

        /// <summary>The image the development starts from: the original mixed with what the networks made of it.</summary>
        /// <param name="ct">Stops the networks between two tiles (OperationCanceledException); nothing half done is kept.</param>
        public static RawImage Apply(RawImage img, RawSettings s, CancellationToken ct = default)
        {
            bool dn = s.AiDenoise >= 1 && AiModels.Denoise.Installed, rf = s.AiRefocus >= 1 && AiModels.Deblur.Installed;
            if (!dn && !rf) return img;
            int w = img.Width, h = img.Height;
            var orig = img.Data;
            var clean = dn ? img.Restore.Get(img, "dn", AiModels.Denoise, () => Run(AiModels.Denoise, orig, w, h, Loc.T("Riduzione rumore AI"), ct)) : null;
            var basis = clean ?? orig;
            var sharp = rf ? img.Restore.Get(img, dn ? "rf+dn" : "rf", AiModels.Deblur, () => Run(AiModels.Deblur, basis, w, h, Loc.T("Rimessa a fuoco AI"), ct)) : null;
            if (clean == null && sharp == null) return img;   // the network could not be loaded

            float kd = (float)(Math.Clamp(s.AiDenoise, 0, 100) / 100), kr = (float)(Math.Clamp(s.AiRefocus, 0, 150) / 100);
            var mask = sharp != null && s.AiRefocusArea == RefocusArea.Subject ? SubjectMask(img) : null;
            var data = new ushort[orig.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0, p = y * w; x < w; x++, p++)
                {
                    float m = sharp == null ? 0 : mask == null ? kr : kr * mask.Weight(x / (float)w, y / (float)h);
                    for (int c = 0, i = p * 3; c < 3; c++, i++)
                    {
                        float v = orig[i];
                        if (clean != null) v += kd * (clean[i] - v);
                        if (sharp != null && m > 0) v += m * (sharp[i] - basis[i]);
                        data[i] = v <= 0 ? (ushort)0 : v >= 65535 ? (ushort)65535 : (ushort)(v + 0.5f);
                    }
                }
            });
            return img.WithData(data);
        }

        /// <summary>
        /// Runs a network over the whole image in overlapping tiles. The linear data are brought to a normal brightness
        /// and encoded as sRGB, as the networks were trained on ordinary photos; what is brighter than the white of the
        /// encoding (the RAW headroom) keeps its original values.
        /// </summary>
        static ushort[] Run(AiModels.Model model, ushort[] src, int w, int h, string label, CancellationToken ct)
        {
            var net = AiModels.Get(model);
            if (net == null) return null;
            int n = w * h;

            // Gain: the brightest tones (99.9th percentile) to white, and a dark photo up towards a normal brightness,
            // so the network sees the noise as it will look once developed.
            var hist = new int[4096];
            var lum = new int[4096];
            int step = Math.Max(1, n / 400000);
            for (int p = 0; p < n; p += step)
            {
                int r = src[p * 3], g = src[p * 3 + 1], b = src[p * 3 + 2];
                hist[Math.Max(r, Math.Max(g, b)) >> 4]++;
                lum[(int)((r * 13933L + g * 46871L + b * 4732L) >> 20)]++;
            }
            int samples = (n + step - 1) / step;
            double Pct(int[] hs, double q)
            {
                long target = (long)(q * samples), acc = 0;
                for (int i = 0; i < 4096; i++) if ((acc += hs[i]) > target) return (i + 0.5) / 4096;
                return 1;
            }
            double top = Math.Max(Pct(hist, 0.999), 1e-3), median = Math.Max(Pct(lum, 0.5), 1e-4);
            float gain = (float)Math.Clamp(Math.Min(1 / top, 0.18 / median), 0.5, 64) / 65535f;

            var acc = new float[n * 3];
            var weight = new float[n];
            var xs = Starts(w);
            var ys = Starts(h);
            int total = xs.Count * ys.Count, done = 0;
            var planes = new float[3 * Tile * Tile];
            const int plane = Tile * Tile;
            Progress?.Invoke(label, 0);
            foreach (int ty in ys)
                foreach (int tx in xs)
                {
                    ct.ThrowIfCancellationRequested();
                    // The tile, mirrored at the edges of the photo when the photo is smaller than a tile.
                    Parallel.For(0, Tile, yy =>
                    {
                        int sy = Mirror(ty + yy, h);
                        for (int xx = 0; xx < Tile; xx++)
                        {
                            int si = (sy * w + Mirror(tx + xx, w)) * 3, o = yy * Tile + xx;
                            planes[o] = Encode(src[si] * gain);
                            planes[plane + o] = Encode(src[si + 1] * gain);
                            planes[2 * plane + o] = Encode(src[si + 2] * gain);
                        }
                    });
                    var result = net.Run(planes, Tile);
                    // Each tile weighs less towards its edges, so the seams between tiles do not show.
                    Parallel.For(0, Math.Min(Tile, h - ty), yy =>
                    {
                        float wy = Ramp(yy, ty == 0, ty + Tile >= h);
                        for (int xx = 0, maxX = Math.Min(Tile, w - tx); xx < maxX; xx++)
                        {
                            float wt = wy * Ramp(xx, tx == 0, tx + Tile >= w);
                            int p = (ty + yy) * w + tx + xx, o = yy * Tile + xx;
                            acc[p * 3] += wt * result[o];
                            acc[p * 3 + 1] += wt * result[plane + o];
                            acc[p * 3 + 2] += wt * result[2 * plane + o];
                            weight[p] += wt;
                        }
                    });
                    Progress?.Invoke(label, ++done / (double)total);
                }

            var dst = new ushort[n * 3];
            float inv = 1 / gain;
            Parallel.For(0, h, y =>
            {
                for (int p = y * w, end = p + w; p < end; p++)
                {
                    float wt = Math.Max(weight[p], 1e-6f);
                    // Near and above the white of the encoding the network saw clipped values: keep the original there.
                    float bright = Math.Max(src[p * 3], Math.Max(src[p * 3 + 1], src[p * 3 + 2])) * gain;
                    float keep = bright <= 0.92f ? 0 : bright >= 1 ? 1 : (bright - 0.92f) / 0.08f;
                    for (int c = 0; c < 3; c++)
                    {
                        int i = p * 3 + c;
                        float v = Decode(acc[i] / wt) * inv;
                        v += keep * (src[i] - v);
                        dst[i] = v <= 0 ? (ushort)0 : v >= 65535 ? (ushort)65535 : (ushort)(v + 0.5f);
                    }
                }
            });
            return dst;
        }

        /// <summary>Tile origins along one side: tiles overlap, the last one ends on the edge.</summary>
        static List<int> Starts(int size)
        {
            var list = new List<int> { 0 };
            if (size <= Tile) return list;
            for (int s = Tile - Overlap; ; s += Tile - Overlap)
            {
                if (s + Tile >= size) { list.Add(size - Tile); break; }
                list.Add(s);
            }
            return list;
        }

        static float Ramp(int i, bool firstEdge, bool lastEdge)
        {
            float a = firstEdge ? 1 : Math.Min(1, (i + 1) / (float)Overlap);
            float b = lastEdge ? 1 : Math.Min(1, (Tile - i) / (float)Overlap);
            return Math.Min(a, b);
        }

        static int Mirror(int i, int size)
        {
            if (size == 1) return 0;
            int period = 2 * (size - 1);
            i %= period;
            if (i < 0) i += period;
            return i < size ? i : period - i;
        }

        static float Encode(float v) => v <= 0 ? 0 : v >= 1 ? 1 : (float)Adjustments.LinearToSrgb(v);
        static float Decode(float v) => v <= 0 ? 0 : (float)Adjustments.SrgbToLinear(Math.Min(v, 1.2f));

        // ================= Where the subject is =================

        sealed class Subject
        {
            public (float Cx, float Cy, float Rx, float Ry)[] Ellipses;

            public float Weight(float x, float y)
            {
                float best = 0;
                foreach (var e in Ellipses)
                {
                    float dx = (x - e.Cx) / e.Rx, dy = (y - e.Cy) / e.Ry;
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    float t = r <= 0.6f ? 1 : r >= 1 ? 0 : 1 - (r - 0.6f) / 0.4f;
                    best = Math.Max(best, t * t * (3 - 2 * t));
                }
                return best;
            }
        }

        /// <summary>
        /// The subject: each face with the body under it, or, in a photo without people, the middle of the frame.
        /// The coordinates are fractions of the frame, so the mask is the same at every size of the image.
        /// </summary>
        static Subject SubjectMask(RawImage img)
        {
            var faces = SceneAnalysis.Of(img).Faces;
            var list = new List<(float, float, float, float)>();
            foreach (var f in faces)
            {
                float cx = (float)f.Cx, cy = (float)f.Cy, fw = (float)f.W, fh = (float)f.H;
                list.Add((cx, cy, fw * 1.1f, fh * 1.1f));                // head and hair
                list.Add((cx, cy + fh * 2.2f, fw * 1.9f, fh * 2.4f));    // shoulders and chest
            }
            if (list.Count == 0) list.Add((0.5f, 0.5f, 0.34f, 0.4f));
            return new Subject { Ellipses = list.ToArray() };
        }
    }

    /// <summary>
    /// What the AI networks made of a photo, for each size of it: shared by the image and its reduced copies like
    /// <see cref="SceneCache"/>. The results are also kept on disk (%LOCALAPPDATA%\PhotoStudio\AiCache, at most
    /// <see cref="DiskLimit"/>), under a hash of the pixels, so reopening the photo does not run the networks again.
    /// </summary>
    sealed class RestoreCache
    {
        const long DiskLimit = 4L << 30;
        static readonly object DiskLock = new object();
        readonly Dictionary<(string, int, int), ushort[]> _done = new Dictionary<(string, int, int), ushort[]>();
        readonly Dictionary<(int, int), string> _hashes = new Dictionary<(int, int), string>();
        readonly object _work = new object();

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoStudio", "AiCache");

        public bool Has(RawImage img, string key)
        {
            lock (_done) if (_done.ContainsKey((key, img.Width, img.Height))) return true;
            return File.Exists(FileOf(img, key));
        }

        public ushort[] Get(RawImage img, string key, AiModels.Model model, Func<ushort[]> make)
        {
            var k = (key, img.Width, img.Height);
            lock (_done) if (_done.TryGetValue(k, out var d)) return d;
            // One job at a time: two previews of the same photo would otherwise run the network twice.
            lock (_work)
            {
                lock (_done) if (_done.TryGetValue(k, out var d)) return d;
                string file = FileOf(img, key);
                var r = Load(file, img.Data.Length);
                if (r == null)
                {
                    r = make();
                    if (r != null) Save(file, r);
                }
                if (r != null) lock (_done) _done[k] = r;
                return r;
            }
        }

        /// <summary>The file of a result: the pixels of the image, the network and the step decide its name.</summary>
        string FileOf(RawImage img, string key)
        {
            string hash;
            lock (_hashes)
                if (!_hashes.TryGetValue((img.Width, img.Height), out hash))
                    _hashes[(img.Width, img.Height)] = hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(img.Data.AsSpan()))).Substring(0, 32);
            string models = key.Contains("dn") ? AiModels.Denoise.Sha256[..8] : "";
            if (key.Contains("rf")) models += AiModels.Deblur.Sha256[..8];
            return Path.Combine(Folder, $"{hash}-{key.Replace('+', '_')}-{models}-{img.Width}x{img.Height}.bin");
        }

        static ushort[] Load(string file, int length)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var data = new ushort[length];
                using (var f = File.OpenRead(file))
                using (var z = new ZLibStream(f, CompressionMode.Decompress))
                    z.ReadExactly(MemoryMarshal.AsBytes(data.AsSpan()));
                File.SetLastAccessTimeUtc(file, DateTime.UtcNow);
                return data;
            }
            catch
            {
                try { File.Delete(file); } catch { }
                return null;   // damaged or from another version: made again
            }
        }

        static void Save(string file, ushort[] data)
        {
            try
            {
                lock (DiskLock)
                {
                    Directory.CreateDirectory(Folder);
                    string part = file + ".part";
                    using (var f = File.Create(part))
                    using (var z = new ZLibStream(f, CompressionLevel.Fastest))
                        z.Write(MemoryMarshal.AsBytes(data.AsSpan()));
                    File.Move(part, file, true);
                    // The oldest results go when the folder grows past the limit.
                    var files = new DirectoryInfo(Folder).GetFiles("*.bin").OrderByDescending(x => x.LastAccessTimeUtc).ToList();
                    long total = 0;
                    foreach (var x in files)
                        if ((total += x.Length) > DiskLimit && x.FullName != file) x.Delete();
                }
            }
            catch { }   // a full disk only costs the time of running the network again
        }
    }
}
