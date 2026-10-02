using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Looks for a newer PhotoStudio among the releases on GitHub and installs it. The installed version downloads the
    /// new installer, checks it against the SHA256SUMS.txt of the same release and runs it silently; the portable
    /// version opens the release page instead. Settings in %APPDATA%\PhotoStudio\update.json.
    /// </summary>
    public static class Updater
    {
        const string Repo = "Lucosh/PhotoStudio";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        public sealed class Release
        {
            public Version Version { get; init; }
            public string Tag { get; init; }
            public string Notes { get; init; }
            public string Page { get; init; }
            public string SetupUrl { get; init; }
            public string SetupName { get; init; }
            public string SumsUrl { get; init; }
        }

        public sealed class Options
        {
            public bool AutoCheck { get; set; } = true;
            public DateTime LastCheck { get; set; }
            public string SkippedVersion { get; set; } = "";

            static string PathOf => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "update.json");

            public static Options Load()
            {
                try { if (File.Exists(PathOf)) return JsonSerializer.Deserialize<Options>(File.ReadAllText(PathOf)) ?? new Options(); }
                catch { }
                return new Options();
            }

            public void Save()
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PathOf));
                    File.WriteAllText(PathOf, JsonSerializer.Serialize(this));
                }
                catch { }
            }
        }

        /// <summary>The running version (the Version of the project, e.g. 1.2.0).</summary>
        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>True when PhotoStudio was installed with the installer (which leaves its uninstaller next to the exe).</summary>
        public static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        static HttpClient Client()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoStudio/" + Current);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>The latest release if it is newer than this version, otherwise null. Throws on network errors.</summary>
        public static async Task<Release> CheckAsync(CancellationToken ct = default)
        {
            using var http = Client();
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParse(tag, out var version) || version <= Current) return null;
            var assets = root.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .ToList();
            var setup = assets.FirstOrDefault(a => a.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
            var sums = assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            return new Release
            {
                Version = version, Tag = tag,
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                Page = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage,
                SetupName = setup.Name, SetupUrl = setup.Url, SumsUrl = sums.Url,
            };
        }

        /// <summary>"v1.2.0" or "1.2.0" (pre-releases such as "1.3.0-beta.1" are ignored).</summary>
        public static bool TryParse(string tag, out Version v)
        {
            v = null;
            var s = tag.TrimStart('v', 'V');
            if (s.Contains('-') || !Version.TryParse(s, out var parsed)) return false;
            v = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        /// <summary>
        /// Downloads the installer of a release to the temporary folder and checks its SHA-256 against the release's
        /// SHA256SUMS.txt. Returns the path of the verified installer.
        /// </summary>
        public static async Task<string> DownloadAsync(Release r, IProgress<double> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(r.SetupUrl) || string.IsNullOrEmpty(r.SumsUrl))
                throw new InvalidOperationException(Loc.T("Questa versione non ha un programma di installazione verificabile."));
            using var http = Client();
            http.Timeout = TimeSpan.FromMinutes(30);
            string sums = await http.GetStringAsync(r.SumsUrl, ct);
            string expected = sums.Split('\n')
                .Select(l => l.Trim().Split(new[] { ' ', '*' }, 2, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2 && p[1].Trim().TrimStart('*') == r.SetupName)
                .Select(p => p[0].ToLowerInvariant())
                .FirstOrDefault() ?? throw new InvalidDataException(Loc.T("Il controllo SHA-256 dell'aggiornamento non è disponibile."));

            string dir = Path.Combine(Path.GetTempPath(), "PhotoStudio-update");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Path.GetFileName(r.SetupName));
            using (var response = await http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1, done / (double)total));
                }
            }
            string actual;
            await using (var f = File.OpenRead(path))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(path);
                throw new InvalidDataException(Loc.T("Il file scaricato è danneggiato (controllo SHA-256 non superato)."));
            }
            return path;
        }

        /// <summary>
        /// Starts the installer without questions; it closes PhotoStudio, replaces it and starts it again
        /// (see the [Run] section of installer\PhotoStudio.iss).
        /// </summary>
        public static void RunInstaller(string path) =>
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1") { UseShellExecute = true });

        public static void OpenPage(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
