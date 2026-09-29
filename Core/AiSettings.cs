using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoStudio.Core
{
    /// <summary>Which AI service edits the sliders, and its options. Saved in %APPDATA%\PhotoStudio\ai.json.</summary>
    public sealed class AiSettings
    {
        public AiProvider Provider { get; set; } = AiProvider.Gemini;
        public string GeminiModel { get; set; } = GeminiProvider.DefaultModel;
        public string OllamaUrl { get; set; } = OllamaProvider.DefaultUrl;
        public string OllamaModel { get; set; } = "";

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "ai.json");

        public static AiSettings Load(string path = null)
        {
            try
            {
                path ??= DefaultPath;
                if (File.Exists(path)) return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), Options) ?? new AiSettings();
            }
            catch { }
            // First run: keep using Claude if a Claude key was already configured.
            return new AiSettings { Provider = KeyStore.HasSavedKey(AiProvider.Claude) ? AiProvider.Claude : AiProvider.Gemini };
        }

        public void Save(string path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }

        /// <summary>Environment variable that overrides the saved key, if set.</summary>
        public static string EnvironmentKey(AiProvider p)
        {
            string[] names = p switch
            {
                AiProvider.Claude => new[] { "ANTHROPIC_API_KEY" },
                AiProvider.Gemini => new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" },
                _ => Array.Empty<string>(),
            };
            foreach (var n in names)
            {
                var v = Environment.GetEnvironmentVariable(n);
                if (!string.IsNullOrWhiteSpace(v)) return n;
            }
            return null;
        }

        /// <summary>The API key: environment variable first, then the key saved in the app.</summary>
        public string ResolveKey(AiProvider p)
        {
            var env = EnvironmentKey(p);
            return env != null ? Environment.GetEnvironmentVariable(env) : KeyStore.Load(p);
        }
    }

    /// <summary>Stores API keys encrypted with Windows DPAPI (readable only by the current Windows user).</summary>
    public static class KeyStore
    {
        static string Name(AiProvider p) => p == AiProvider.Claude ? "Claude" : "Gemini";

        static byte[] Entropy(AiProvider p) => Encoding.UTF8.GetBytes($"PhotoStudio.{Name(p)}ApiKey.v1");

        static string FilePath(AiProvider p) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", Name(p).ToLowerInvariant() + ".key");

        public static bool HasSavedKey(AiProvider p) => p != AiProvider.Ollama && File.Exists(FilePath(p));

        public static string Load(AiProvider p)
        {
            try
            {
                if (!HasSavedKey(p)) return null;
                var data = ProtectedData.Unprotect(File.ReadAllBytes(FilePath(p)), Entropy(p), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(AiProvider p, string key)
        {
            if (p == AiProvider.Ollama) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath(p)));
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy(p), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath(p), data);
        }

        public static void Delete(AiProvider p)
        {
            if (HasSavedKey(p)) File.Delete(FilePath(p));
        }
    }
}
