using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Core
{
    /// <summary>Claude (Anthropic API, paid). Uses the official Anthropic SDK with structured JSON output.</summary>
    public static class ClaudeProvider
    {
        public const string ModelId = "claude-opus-5";
        public const string KeyUrl = "https://console.anthropic.com/settings/keys";

        public static async Task<AiSuggestion> SuggestAsync(AiRequest r, string apiKey, CancellationToken cancellationToken)
        {
            var schema = AiEditor.Schema(true).ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
            var client = new AnthropicClient { ApiKey = apiKey };
            BetaMessage response;
            try
            {
                response = await client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = ModelId,
                    MaxTokens = 16000,
                    // Server-side refusal fallback: if Claude Opus 5 declines, the request is re-served by another model.
                    Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
                    Fallbacks = new List<BetaFallbackParam> { new(Anthropic.Models.Messages.Model.ClaudeOpus4_8) },
                    Thinking = new BetaThinkingConfigAdaptive(),
                    OutputConfig = new BetaOutputConfig
                    {
                        Effort = Effort.Medium,
                        Format = new BetaJsonOutputFormat { Schema = schema },
                    },
                    System = AiEditor.SystemPrompt,
                    Messages =
                    [
                        new BetaMessageParam
                        {
                            Role = Role.User,
                            Content = new List<BetaContentBlockParam>
                            {
                                new BetaImageBlockParam
                                {
                                    Source = new BetaBase64ImageSource
                                    {
                                        Data = Convert.ToBase64String(r.PreviewJpeg),
                                        MediaType = MediaType.ImageJpeg,
                                    },
                                },
                                new BetaTextBlockParam { Text = AiEditor.UserText(r) },
                            },
                        },
                    ],
                }, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (AnthropicUnauthorizedException)
            {
                throw new InvalidOperationException(T("La chiave API di Claude non è valida. Controllala in Modifica ▸ Impostazioni AI."));
            }
            catch (AnthropicRateLimitException)
            {
                throw new InvalidOperationException(T("Troppe richieste a Claude in poco tempo: riprova tra qualche secondo."));
            }
            catch (Anthropic5xxException)
            {
                throw new InvalidOperationException(T("Il servizio di Claude è temporaneamente non disponibile: riprova tra poco."));
            }
            catch (AnthropicIOException ex)
            {
                throw new InvalidOperationException(T("Impossibile contattare Claude: controlla la connessione a Internet.\n{0}", ex.Message));
            }
            catch (AnthropicApiException ex)
            {
                throw new InvalidOperationException(T("Claude ha restituito un errore: {0}", ex.Message));
            }

            if (response.StopReason == "refusal")
                throw new InvalidOperationException(T("Claude ha rifiutato questa richiesta."));
            if (response.StopReason == "max_tokens")
                throw new InvalidOperationException(T("La risposta di Claude è stata troncata: riprova."));

            string json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            return AiEditor.Parse(json, r.Current);
        }
    }

    /// <summary>Google Gemini (free tier available with a Google AI Studio key). REST generateContent API.</summary>
    public static class GeminiProvider
    {
        public const string DefaultModel = "gemini-flash-latest";
        public const string KeyUrl = "https://aistudio.google.com/apikey";
        public static readonly string[] SuggestedModels = { "gemini-flash-latest", "gemini-3.8-flash", "gemini-3.5-flash-lite" };

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        // GEMINI_BASE_URL lets tests point the client at a local fake server.
        static string BaseUrl => (Environment.GetEnvironmentVariable("GEMINI_BASE_URL") is string u && u.Length > 0 ? u : "https://generativelanguage.googleapis.com").TrimEnd('/');

        static string CleanModel(string model)
        {
            model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            return model.StartsWith("models/", StringComparison.Ordinal) ? model.Substring(7) : model;
        }

        /// <summary>Tried in this order when the chosen model is overloaded, missing or out of quota.</summary>
        static readonly string[] FallbackModels = { "gemini-flash-latest", "gemini-2.5-flash", "gemini-flash-lite-latest" };

        // Models that just answered "overloaded": skipped for a while, so every request does not wait for them again.
        static readonly Dictionary<string, DateTime> Unavailable = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>An error of the Gemini API; TryAnotherModel = the problem is this model, not the key or the connection.</summary>
        sealed class GeminiException : InvalidOperationException
        {
            public GeminiException(string message, bool tryAnotherModel, bool retry, string reason = null) : base(message)
            {
                TryAnotherModel = tryAnotherModel;
                Retry = retry;
                Reason = reason;
            }

            public bool TryAnotherModel { get; }
            public bool Retry { get; }
            /// <summary>Short reason for the user, e.g. "sovraccarico".</summary>
            public string Reason { get; }
        }

        /// <summary>
        /// Asks the chosen model; if it is overloaded (503), no longer exists (404) or out of free quota (429),
        /// tries once more and then falls back to the other Gemini models, telling the user which one answered.
        /// </summary>
        public static async Task<AiSuggestion> SuggestAsync(AiRequest r, string apiKey, string model, CancellationToken cancellationToken)
        {
            model = CleanModel(model);
            var models = new List<string>();
            lock (Unavailable)
            {
                bool skipChosen = Unavailable.TryGetValue(model, out var until) && until > DateTime.Now;
                if (!skipChosen) models.Add(model);
                foreach (var m in FallbackModels)
                    if (!models.Contains(m, StringComparer.OrdinalIgnoreCase) && !(Unavailable.TryGetValue(m, out var u) && u > DateTime.Now)) models.Add(m);
                if (skipChosen) models.Add(model);   // last resort, it may be back
            }

            var failures = new List<string>();
            GeminiException last = null;
            foreach (var m in models)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var s = await SuggestWithModelAsync(r, apiKey, m, cancellationToken);
                        if (string.Equals(m, model, StringComparison.OrdinalIgnoreCase)) return s;
                        return new AiSuggestion
                        {
                            Settings = s.Settings, Explanation = s.Explanation,
                            Note = T("{0}: ha risposto {1}. Se succede spesso scegli un altro modello in Modifica ▸ Impostazioni AI.", string.Join("; ", failures), m),
                        };
                    }
                    catch (GeminiException ex) when (ex.TryAnotherModel)
                    {
                        last = ex;
                        if (ex.Retry && attempt == 0)
                        {
                            await Task.Delay(1500, cancellationToken);   // brief overloads often pass in a moment
                            continue;
                        }
                        failures.Add($"{m} {ex.Reason ?? T("non disponibile")}");
                        lock (Unavailable) Unavailable[m] = DateTime.Now.AddMinutes(10);
                        break;
                    }
                }
            }
            throw new InvalidOperationException(
                T("Nessun modello Gemini ha risposto: Google li segnala sovraccarichi o hai finito le richieste gratuite di oggi. Riprova tra qualche minuto, oppure usa Ollama o Claude.\n\nUltimo errore: {0}", last?.Message));
        }

        static async Task<AiSuggestion> SuggestWithModelAsync(AiRequest r, string apiKey, string model, CancellationToken cancellationToken)
        {
            var body = new Dictionary<string, object>
            {
                ["systemInstruction"] = new { parts = new[] { new { text = AiEditor.SystemPrompt } } },
                ["contents"] = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { inlineData = new { mimeType = "image/jpeg", data = Convert.ToBase64String(r.PreviewJpeg) } },
                            new { text = AiEditor.UserText(r) },
                        },
                    },
                },
                ["generationConfig"] = new { responseMimeType = "application/json", responseJsonSchema = AiEditor.Schema(false) },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", apiKey);
            string text = await Send(request, model, cancellationToken);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("promptFeedback", out var pf) && pf.TryGetProperty("blockReason", out var br))
                throw new InvalidOperationException(T("Gemini ha bloccato la richiesta ({0}).", br.GetString()));
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                throw new InvalidOperationException(T("Gemini non ha restituito alcuna risposta: riprova."));

            var candidate = candidates[0];
            string finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : "STOP";
            var answer = new StringBuilder();
            if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray())
                {
                    bool thought = part.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True;
                    if (!thought && part.TryGetProperty("text", out var t)) answer.Append(t.GetString());
                }

            if (finish == "MAX_TOKENS")
                throw new InvalidOperationException(T("La risposta di Gemini è stata troncata: riprova."));
            if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "IMAGE_SAFETY" or "BLOCKLIST" or "SPII" or "RECITATION")
                throw new InvalidOperationException(T("Gemini ha rifiutato questa richiesta ({0}).", finish));
            if (answer.Length == 0)
                throw new InvalidOperationException(T("Gemini non ha restituito una risposta ({0}): riprova.", finish));
            return AiEditor.Parse(answer.ToString(), r.Current);
        }

        /// <summary>Names of the Gemini models this key can use for generateContent.</summary>
        public static async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
        {
            var names = new List<string>();
            string pageToken = null;
            for (int page = 0; page < 5; page++)
            {
                string url = $"{BaseUrl}/v1beta/models?pageSize=1000" + (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-goog-api-key", apiKey);
                using var doc = JsonDocument.Parse(await Send(request, null, cancellationToken));
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                    {
                        string name = CleanModel(m.GetProperty("name").GetString());
                        bool generates = m.TryGetProperty("supportedGenerationMethods", out var methods) &&
                                         methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
                        string lower = name.ToLowerInvariant();
                        if (generates && lower.StartsWith("gemini") &&
                            !new[] { "tts", "embedding", "image", "audio", "live", "robotics", "computer-use" }.Any(lower.Contains))
                            names.Add(name);
                    }
                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
                if (string.IsNullOrEmpty(pageToken)) break;
            }
            // "-latest" aliases first, then the newest versions.
            return names.Distinct()
                .OrderBy(n => n.EndsWith("-latest", StringComparison.Ordinal) ? 0 : 1)
                .ThenByDescending(n => n, StringComparer.Ordinal)
                .ToList();
        }

        static async Task<string> Send(HttpRequestMessage request, string model, CancellationToken ct)
        {
            HttpResponseMessage response;
            string body;
            try
            {
                response = await Http.SendAsync(request, ct);
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException(T("Gemini non ha risposto in tempo: riprova."));
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(T("Impossibile contattare Gemini: controlla la connessione a Internet.\n{0}", ex.Message));
            }

            using (response)
            {
                if (response.IsSuccessStatusCode) return body;
                string message = "", status = "", reason = "";
                try
                {
                    using var err = JsonDocument.Parse(body);
                    var e = err.RootElement.GetProperty("error");
                    message = e.TryGetProperty("message", out var m) ? m.GetString() : "";
                    status = e.TryGetProperty("status", out var s) ? s.GetString() : "";
                    if (e.TryGetProperty("details", out var details))
                        foreach (var d in details.EnumerateArray())
                            if (d.TryGetProperty("reason", out var rs)) reason = rs.GetString();
                }
                catch { message = body.Length > 300 ? body.Substring(0, 300) : body; }

                int code = (int)response.StatusCode;
                string detail = string.IsNullOrWhiteSpace(message) ? "" : " — " + message;
                if (reason == "API_KEY_INVALID" || code == 401 || (code == 400 && message.Contains("API key", StringComparison.OrdinalIgnoreCase)))
                    throw new GeminiException(T("La chiave API di Gemini non è valida. Controllala in Modifica ▸ Impostazioni AI."), false, false);
                if (code == 403)
                    throw new GeminiException(T("La chiave di Gemini non ha accesso a questo servizio: {0}", message), false, false);
                if (code == 404)
                    throw new GeminiException(T("Il modello Gemini \"{0}\" non esiste o non è disponibile (errore 404): scegline un altro in Modifica ▸ Impostazioni AI (pulsante Aggiorna elenco).", model), model != null, false, T("non esiste più"));
                if (code == 429 || status == "RESOURCE_EXHAUSTED")
                    throw new GeminiException(T("Hai raggiunto il limite di richieste di Gemini per \"{0}\" (errore 429: con il piano gratuito c'è un limite al minuto e al giorno){1}", model, detail), model != null, false, T("ha finito le richieste gratuite"));
                if (code >= 500)
                    throw new GeminiException(T("Il modello Gemini \"{0}\" è sovraccarico o non disponibile in questo momento (errore {1}){2}", model, code, detail), model != null, true, T("è sovraccarico"));
                throw new GeminiException(T("Gemini ha restituito un errore: {0}", message), false, false);
            }
        }
    }

    public sealed class OllamaModel
    {
        public string Name { get; init; }
        public string Size { get; init; }
        /// <summary>True when the model can see images; null when Ollama did not say.</summary>
        public bool? Vision { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>Ollama: free local models running on this PC (no key, the photo never leaves the computer).</summary>
    public static class OllamaProvider
    {
        // 127.0.0.1 rather than "localhost": Ollama listens on IPv4, and on Windows a refused IPv6 attempt costs ~2 s.
        public const string DefaultUrl = "http://127.0.0.1:11434";
        public const string DownloadUrl = "https://ollama.com/download";

        // Local models can be slow on a PC without a powerful GPU.
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        public static string NormalizeUrl(string url)
        {
            url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim().TrimEnd('/');
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? url : "http://" + url;
        }

        public static async Task<AiSuggestion> SuggestAsync(AiRequest r, string baseUrl, string model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(T("Scegli il modello di Ollama in Modifica ▸ Impostazioni AI."));
            var body = new Dictionary<string, object>
            {
                ["model"] = model.Trim(),
                ["stream"] = false,
                ["format"] = AiEditor.Schema(true),
                ["keep_alive"] = "15m",
                ["options"] = new { temperature = 0.2 },
                ["messages"] = new object[]
                {
                    new { role = "system", content = AiEditor.SystemPrompt },
                    new { role = "user", content = AiEditor.UserText(r), images = new[] { Convert.ToBase64String(r.PreviewJpeg) } },
                },
            };
            string text = await Send(HttpMethod.Post, NormalizeUrl(baseUrl), "/api/chat", body, model, cancellationToken);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("done_reason", out var dr) && dr.GetString() == "length")
                throw new InvalidOperationException(T("La risposta di Ollama è stata troncata: riprova."));
            string answer = root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c) ? c.GetString() : null;
            return AiEditor.Parse(answer, r.Current);
        }

        /// <summary>Installed models, with whether each one can see images.</summary>
        public static async Task<List<OllamaModel>> ListModelsAsync(string baseUrl, CancellationToken cancellationToken)
        {
            string url = NormalizeUrl(baseUrl);
            using var doc = JsonDocument.Parse(await Send(HttpMethod.Get, url, "/api/tags", null, null, cancellationToken));
            var result = new List<OllamaModel>();
            if (!doc.RootElement.TryGetProperty("models", out var models)) return result;
            foreach (var m in models.EnumerateArray())
            {
                string name = m.GetProperty("name").GetString();
                string size = m.TryGetProperty("details", out var d) && d.TryGetProperty("parameter_size", out var ps) ? ps.GetString() : "";
                bool? vision = null;
                try
                {
                    using var show = JsonDocument.Parse(await Send(HttpMethod.Post, url, "/api/show", new { model = name }, name, cancellationToken));
                    if (show.RootElement.TryGetProperty("capabilities", out var caps))
                        vision = caps.EnumerateArray().Any(x => x.GetString() == "vision");
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                result.Add(new OllamaModel { Name = name, Size = size, Vision = vision });
            }
            return result.OrderByDescending(m => m.Vision == true).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static async Task<string> Send(HttpMethod method, string baseUrl, string path, object body, string model, CancellationToken ct)
        {
            HttpResponseMessage response;
            string text;
            try
            {
                using var request = new HttpRequestMessage(method, baseUrl + path);
                if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                response = await Http.SendAsync(request, ct);
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (TaskCanceledException)
            {
                throw new InvalidOperationException(T("Ollama non ha risposto in tempo: il modello potrebbe essere troppo pesante per questo PC."));
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException(T("Ollama non risponde su {0}. Verifica che Ollama sia installato e avviato ({1}).", baseUrl, DownloadUrl));
            }
            catch (UriFormatException)
            {
                throw new InvalidOperationException(T("L'indirizzo di Ollama \"{0}\" non è valido.", baseUrl));
            }

            using (response)
            {
                if (response.IsSuccessStatusCode) return text;
                string error = text;
                try
                {
                    using var err = JsonDocument.Parse(text);
                    if (err.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
                }
                catch { }
                if (response.StatusCode == HttpStatusCode.NotFound && model != null)
                    throw new InvalidOperationException(T("Il modello \"{0}\" non è installato in Ollama. Scaricalo con: ollama pull {1}", model, model));
                if (error != null && error.Contains("image", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T("Il modello \"{0}\" non può vedere le immagini: scegli un modello con visione (es. gemma3, qwen2.5vl, llama3.2-vision).", model));
                throw new InvalidOperationException(T("Ollama ha restituito un errore: {0}", error));
            }
        }
    }
}
