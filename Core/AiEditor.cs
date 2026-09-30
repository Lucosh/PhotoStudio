using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    public enum AiProvider { Gemini, Ollama, Claude }

    public sealed class AiSuggestion
    {
        public RawSettings Settings { get; init; }
        public string Explanation { get; init; }
        /// <summary>Something the user should know about how the answer was obtained (e.g. another model was used).</summary>
        public string Note { get; init; }
    }

    /// <summary>What is sent to the AI: a small preview, the current slider values and a few statistics.</summary>
    public sealed class AiRequest
    {
        public byte[] PreviewJpeg { get; init; }
        public RawSettings Current { get; init; }
        public bool SceneReferred { get; init; }
        public string PhotoInfo { get; init; }
        public string Stats { get; init; }
        public string Instruction { get; init; }
    }

    /// <summary>
    /// Asks an AI (Claude, Gemini or a local Ollama model) to choose develop-slider values for a photo.
    /// The AI only sees a small preview and returns numbers: the pixels are always rendered by
    /// PhotoStudio's own deterministic engine.
    /// </summary>
    public static class AiEditor
    {
        internal static readonly (string Key, Func<RawSettings, double> Get, Action<RawSettings, double> Set, double Min, double Max)[] Params =
        {
            ("temperature", s => s.Temperature, (s, v) => s.Temperature = v, -100, 100),
            ("tint", s => s.Tint, (s, v) => s.Tint = v, -100, 100),
            ("exposure", s => s.Exposure, (s, v) => s.Exposure = v, -5, 5),
            ("contrast", s => s.Contrast, (s, v) => s.Contrast = v, -100, 100),
            ("highlights", s => s.Highlights, (s, v) => s.Highlights = v, -100, 100),
            ("shadows", s => s.Shadows, (s, v) => s.Shadows = v, -100, 100),
            ("whites", s => s.Whites, (s, v) => s.Whites = v, -100, 100),
            ("blacks", s => s.Blacks, (s, v) => s.Blacks = v, -100, 100),
            ("texture", s => s.Texture, (s, v) => s.Texture = v, -100, 100),
            ("clarity", s => s.Clarity, (s, v) => s.Clarity = v, -100, 100),
            ("dehaze", s => s.Dehaze, (s, v) => s.Dehaze = v, -100, 100),
            ("vibrance", s => s.Vibrance, (s, v) => s.Vibrance = v, -100, 100),
            ("saturation", s => s.Saturation, (s, v) => s.Saturation = v, -100, 100),
            ("sharpening", s => s.Sharpening, (s, v) => s.Sharpening = v, 0, 150),
        };

        /// <summary>Instructions for the AI; the explanation is requested in the interface language.</summary>
        internal static string SystemPrompt => SystemPromptTemplate.Replace("{language}", Loc.EnglishName);

        const string SystemPromptTemplate =
@"You are an expert photo editor operating the develop sliders of a RAW editor similar to Adobe Camera Raw.
You never edit pixels yourself: you only choose slider values, and the application's deterministic engine renders the photo.

You receive the photo as it currently looks (a downscaled JPEG that already reflects the current slider values), the current values, basic statistics and, optionally, an instruction from the user. Return the complete new set of values (absolute values, not deltas).

Slider meanings:
- temperature (-100..100): negative = cooler/bluer, positive = warmer/yellower. ±100 changes the red/blue balance by about 3.3x, so tungsten light usually needs -50 to -90.
- tint (-100..100): negative = greener, positive = more magenta.
- exposure (-5..5, in EV stops).
- contrast (-100..100). For RAW files a gentle base tone curve is already applied at 0.
- highlights (-100..100): negative recovers detail in bright areas; RAW files have extra headroom above white.
- shadows (-100..100): positive opens up dark areas.
- whites / blacks (-100..100): move the white and black points.
- texture (-100..100): fine detail; negative smooths skin.
- clarity (-100..100): local midtone contrast.
- dehaze (-100..100): positive removes haze/fog and deepens colours; negative adds a soft haze. Use sparingly (0-25) unless the photo is hazy.
- vibrance (-100..100): boosts muted colours more than saturated ones and protects skin tones. saturation (-100..100): uniform.
- sharpening (0..150): output sharpening.

Guidelines:
- Without an instruction, aim for the edit a skilled photographer would make: natural colours and white balance, good exposure, detail in highlights and shadows, pleasing contrast. Avoid over-processing (HDR look, oversaturation, crushed blacks, halos from extreme clarity).
- With an instruction, follow it faithfully; keep the result technically sound unless the user explicitly asks for an extreme or stylised look.
- If the photo already looks good, make small refinements rather than large changes.
- Keep sharpening modest (about 0-30 for 8-bit images, 20-50 for RAW) and lower it for noisy, high-ISO photos.
- The explanation must be 1-3 short sentences in {language}, addressed to the user, saying what you changed and why.";

        public static string DisplayName(AiProvider p) => p switch
        {
            AiProvider.Claude => "Claude",
            AiProvider.Gemini => "Gemini",
            _ => "Ollama",
        };

        /// <summary>Sends the request to the provider chosen in the settings.</summary>
        public static Task<AiSuggestion> SuggestAsync(AiSettings settings, AiRequest request, CancellationToken cancellationToken) =>
            settings.Provider switch
            {
                AiProvider.Claude => ClaudeProvider.SuggestAsync(request, settings.ResolveKey(AiProvider.Claude), cancellationToken),
                AiProvider.Gemini => GeminiProvider.SuggestAsync(request, settings.ResolveKey(AiProvider.Gemini), settings.GeminiModel, cancellationToken),
                _ => OllamaProvider.SuggestAsync(request, settings.OllamaUrl, settings.OllamaModel, cancellationToken),
            };

        /// <summary>Null when the chosen provider is ready to use, otherwise what is missing.</summary>
        public static string MissingConfiguration(AiSettings s) => s.Provider switch
        {
            AiProvider.Claude when string.IsNullOrWhiteSpace(s.ResolveKey(AiProvider.Claude)) => T("Manca la chiave API di Claude."),
            AiProvider.Gemini when string.IsNullOrWhiteSpace(s.ResolveKey(AiProvider.Gemini)) => T("Manca la chiave API di Gemini."),
            AiProvider.Ollama when string.IsNullOrWhiteSpace(s.OllamaModel) => T("Scegli il modello di Ollama da usare."),
            _ => null,
        };

        internal static string UserText(AiRequest r)
        {
            var currentJson = JsonSerializer.Serialize(Params.ToDictionary(p => p.Key, p => Math.Round(p.Get(r.Current), 2)));
            string source = r.SceneReferred
                ? "Camera RAW file (the preview is the current development)"
                : "8-bit image (JPEG/PNG/layer)";
            string request = string.IsNullOrWhiteSpace(r.Instruction)
                ? "No specific instruction: apply the best natural edit for this photo."
                : "User instruction (in " + Loc.EnglishName + "): \"" + r.Instruction.Trim() + "\"";
            return
                $"Photo: {r.PhotoInfo}\nSource: {source}\nStatistics of the current rendering: {r.Stats}\n" +
                $"Current slider values: {currentJson}\n\n{request}\n\n" +
                "Answer with a single JSON object containing all the slider keys above (numbers) and \"explanation\" (string).";
        }

        /// <summary>JSON schema of the answer: every slider as a number plus the explanation.</summary>
        internal static Dictionary<string, object> Schema(bool closed)
        {
            var properties = new Dictionary<string, object>();
            foreach (var p in Params) properties[p.Key] = new Dictionary<string, object> { ["type"] = "number" };
            properties["explanation"] = new Dictionary<string, object> { ["type"] = "string" };
            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = Params.Select(p => p.Key).Append("explanation").ToArray(),
            };
            if (closed) schema["additionalProperties"] = false;
            return schema;
        }

        /// <summary>
        /// Parses the AI's JSON answer; values are clamped to the slider ranges. Tolerates the small deviations
        /// local models sometimes make (code fences, text around the JSON, numbers written as strings).
        /// </summary>
        public static AiSuggestion Parse(string text, RawSettings current)
        {
            int start = text?.IndexOf('{') ?? -1, end = text?.LastIndexOf('}') ?? -1;
            if (start < 0 || end <= start)
                throw new InvalidOperationException(T("La risposta dell'AI non contiene i valori dei cursori: riprova."));

            JsonDocument doc;
            try { doc = JsonDocument.Parse(text.Substring(start, end - start + 1)); }
            catch (JsonException) { throw new InvalidOperationException(T("La risposta dell'AI non è in un formato valido: riprova.")); }

            using (doc)
            {
                var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject()) fields[prop.Name] = prop.Value;

                var s = current.Clone();
                int found = 0;
                foreach (var p in Params)
                {
                    if (!fields.TryGetValue(p.Key, out var v)) continue;
                    double value;
                    if (v.ValueKind == JsonValueKind.Number) value = v.GetDouble();
                    else if (v.ValueKind != JsonValueKind.String ||
                             !double.TryParse(v.GetString().Trim().TrimStart('+').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        continue;
                    if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                    value = Math.Clamp(value, p.Min, p.Max);
                    p.Set(s, p.Key == "exposure" ? Math.Round(value, 2) : Math.Round(value));
                    found++;
                }
                if (found == 0)
                    throw new InvalidOperationException(T("La risposta dell'AI non contiene i valori dei cursori: riprova."));

                string explanation = fields.TryGetValue("explanation", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : "";
                return new AiSuggestion { Settings = s, Explanation = explanation };
            }
        }

        /// <summary>Short numeric description of a rendered BGRA image (helps the AI judge exposure precisely).</summary>
        public static string DescribeStatistics(byte[] bgra)
        {
            var hist = new long[256];
            long n = 0, clippedHigh = 0, clippedLow = 0;
            double satSum = 0;
            for (int i = 0; i < bgra.Length; i += 4 * 3)
            {
                if (bgra[i + 3] < 128) continue;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                int l = (r * 77 + g * 150 + b * 29) >> 8;
                hist[l]++;
                int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                if (mx >= 254) clippedHigh++;
                if (mx <= 2) clippedLow++;
                if (mx > 5) satSum += (mx - mn) / (double)mx;
                n++;
            }
            if (n == 0) return "empty image";
            int Pct(double q)
            {
                long target = (long)(q * n), acc = 0;
                for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > target) return i; }
                return 255;
            }
            var ci = CultureInfo.InvariantCulture;
            return string.Format(ci,
                "luminance percentiles (0-255) p1={0} p10={1} median={2} p90={3} p99={4}; clipped highlights {5:0.0}%; crushed shadows {6:0.0}%; mean saturation {7:0.00}",
                Pct(0.01), Pct(0.10), Pct(0.5), Pct(0.90), Pct(0.99), 100.0 * clippedHigh / n, 100.0 * clippedLow / n, satSum / n);
        }
    }
}
