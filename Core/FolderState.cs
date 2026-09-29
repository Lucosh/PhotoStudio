using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoStudio.Core
{
    public sealed class PhotoMark
    {
        public int Rating { get; set; }
        public PhotoLabel Label { get; set; }
    }

    public sealed class BatchPhotoState
    {
        public List<string> Files { get; set; } = new List<string>();
        public string OutputPath { get; set; }
        public bool Saved { get; set; }
        public RawSettings Settings { get; set; }
    }

    public sealed class BatchState
    {
        public string OutputFolder { get; set; }
        public List<BatchPhotoState> Photos { get; set; } = new List<BatchPhotoState>();
    }

    /// <summary>
    /// What PhotoStudio remembers about a folder: stars and color labels, the last photo viewed in the
    /// Preselezione and the editing in progress. Saved in %APPDATA%\PhotoStudio\folders, never next to the photos.
    /// </summary>
    public sealed class FolderState
    {
        public string Folder { get; set; }
        public Dictionary<string, PhotoMark> Marks { get; set; } = new Dictionary<string, PhotoMark>(StringComparer.OrdinalIgnoreCase);
        public string LastPhoto { get; set; }
        public BatchState Batch { get; set; }
        public DateTime Updated { get; set; }

        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,   // RawSettings uses fields
            Converters = { new JsonStringEnumConverter() },
        };

        static string FileFor(string folder)
        {
            string key = Path.GetFullPath(folder).TrimEnd('\\', '/').ToLowerInvariant();
            string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "folders", hash + ".json");
        }

        public static FolderState Load(string folder)
        {
            try
            {
                string path = FileFor(folder);
                if (File.Exists(path))
                {
                    var s = JsonSerializer.Deserialize<FolderState>(File.ReadAllText(path), Json);
                    if (s != null)
                    {
                        s.Folder = folder;
                        s.Marks = new Dictionary<string, PhotoMark>(s.Marks ?? new Dictionary<string, PhotoMark>(), StringComparer.OrdinalIgnoreCase);
                        return s;
                    }
                }
            }
            catch { }
            return new FolderState { Folder = folder };
        }

        public void Save()
        {
            try
            {
                string path = FileFor(Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Updated = DateTime.Now;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, path, true);
            }
            catch { }   // losing the stars is annoying, but never a reason to interrupt the work
        }

        public void ApplyMarks(IEnumerable<PhotoItem> photos)
        {
            foreach (var p in photos)
                if (Marks.TryGetValue(p.Name, out var m)) { p.Rating = m.Rating; p.Label = m.Label; }
        }

        public void Remember(PhotoItem p)
        {
            if (p.Rating == 0 && p.Label == PhotoLabel.None) Marks.Remove(p.Name);
            else Marks[p.Name] = new PhotoMark { Rating = p.Rating, Label = p.Label };
        }

        /// <summary>The editing left half-way, if any photo is still to be saved.</summary>
        public bool HasPendingBatch =>
            Batch?.Photos != null && Batch.Photos.Any(p => !p.Saved && p.Files.Any(File.Exists));
    }

    /// <summary>Named Camera Raw settings. Saved in %APPDATA%\PhotoStudio\presets.json.</summary>
    public sealed class RawPreset
    {
        public string Name { get; set; }
        public RawSettings Settings { get; set; }

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "presets.json");

        public static List<RawPreset> LoadAll()
        {
            try
            {
                if (File.Exists(FilePath))
                    return (JsonSerializer.Deserialize<List<RawPreset>>(File.ReadAllText(FilePath), FolderState.Json) ?? new List<RawPreset>())
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Settings != null)
                        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch { }
            return new List<RawPreset>();
        }

        static void SaveAll(List<RawPreset> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, FolderState.Json));
        }

        /// <summary>Adds the preset, replacing one with the same name.</summary>
        public static void Save(string name, RawSettings settings)
        {
            var list = LoadAll();
            list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
            list.Add(new RawPreset { Name = name.Trim(), Settings = settings.Clone() });
            SaveAll(list);
        }

        public static void Delete(string name)
        {
            var list = LoadAll();
            list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
            SaveAll(list);
        }
    }
}
