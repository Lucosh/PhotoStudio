using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace PhotoStudio.Core
{
    /// <summary>
    /// Interface translations. The Italian text written in the code is the key: Localization\{code}.json maps it
    /// to the translation, and a missing entry falls back to Italian. The language is chosen once at startup
    /// (saved choice, otherwise the Windows language); changing it takes effect at the next start.
    /// </summary>
    public static partial class Loc
    {
        /// <summary>Supported interface languages, with their names written in that language.</summary>
        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"), ("it", "Italiano"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        };

        static Dictionary<string, string> _map = new Dictionary<string, string>();

        /// <summary>Two-letter code of the interface language in use.</summary>
        public static string Language { get; private set; } = "it";

        /// <summary>Name of the interface language in English (used to ask the AI to answer in it).</summary>
        public static string EnglishName => Language switch
        {
            "en" => "English",
            "de" => "German",
            "fr" => "French",
            "es" => "Spanish",
            _ => "Italian",
        };

        public static void Init()
        {
            Language = SavedLanguage() ?? SystemLanguage();
            _map = Load(Language);
            var culture = CultureInfo.GetCultureInfo(Language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        /// <summary>The translation of an Italian interface text.</summary>
        public static string T(string it) =>
            it != null && _map.TryGetValue(it, out var s) && !string.IsNullOrEmpty(s) ? s : it;

        /// <summary>The translation of an Italian format string, filled with <paramref name="args"/>.</summary>
        public static string T(string it, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(it), args);

        /// <summary>
        /// For an Italian word with more than one meaning (e.g. "Blu" as a label and as the blues of the colour mixer):
        /// the key is "context\u0004text", as in gettext.
        /// </summary>
        public static string TC(string context, string it) =>
            _map.TryGetValue(context + "\u0004" + it, out var s) && !string.IsNullOrEmpty(s) ? s : it;

        /// <summary>A name used inside a sentence: lowercase, except in German where nouns keep their capital.</summary>
        public static string InSentence(string name) => Language == "de" ? name : name.ToLower(CultureInfo.CurrentUICulture);

        static Dictionary<string, string> Load(string code)
        {
            if (code == "it") return new Dictionary<string, string>();
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"PhotoStudio.Localization.{code}.json");
            if (stream == null) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }

        static bool Supported(string code) => Array.Exists(Languages, l => l.Code == code);

        static string SystemLanguage()
        {
            string code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Supported(code) ? code : "en";
        }

        static string SettingPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoStudio", "language.txt");

        static string SavedLanguage()
        {
            try
            {
                string code = File.ReadAllText(SettingPath).Trim();
                return Supported(code) ? code : null;
            }
            catch { return null; }
        }

        /// <summary>Saves the language to use from the next start.</summary>
        public static void SaveLanguage(string code)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingPath));
            File.WriteAllText(SettingPath, code);
        }
    }
}
