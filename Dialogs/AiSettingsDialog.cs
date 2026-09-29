using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PhotoStudio.Core;

namespace PhotoStudio.Dialogs
{
    /// <summary>Chooses the AI service (Gemini, Ollama or Claude) that sets the Camera Raw sliders, with its key and model.</summary>
    public sealed class AiSettingsDialog : DialogWindow
    {
        const double FieldWidth = 290;

        readonly AiSettings _settings;
        readonly Dictionary<AiProvider, RadioButton> _choices = new Dictionary<AiProvider, RadioButton>();
        readonly Dictionary<AiProvider, FrameworkElement> _panels = new Dictionary<AiProvider, FrameworkElement>();
        readonly PasswordBox _geminiKey, _claudeKey;
        readonly TextBlock _geminiState, _claudeState, _ollamaState;
        readonly ComboBox _geminiModel, _ollamaModel;
        readonly TextBox _ollamaUrl;
        readonly Button _geminiRemove, _claudeRemove;
        List<OllamaModel> _ollamaModels = new List<OllamaModel>();
        bool _ollamaListed;

        public AiSettingsDialog(Window owner, AiProvider? select = null) : base(owner, "Impostazioni AI")
        {
            _settings = AiSettings.Load();
            Body.Width = 520;

            Body.Children.Add(Wrap("Scegli il servizio AI che imposta i cursori di Camera Raw. In ogni caso l'AI risponde solo con dei numeri: " +
                                   "i pixel della foto li elabora sempre PhotoStudio.", dim: false, bottom: 12));

            AddChoice(AiProvider.Gemini, "Gemini (Google)", "Gratuito entro i limiti del piano free: serve una chiave API gratuita di Google AI Studio.");
            AddChoice(AiProvider.Ollama, "Ollama (sul tuo PC)", "Gratuito e senza chiave: il modello gira sul tuo computer e la foto non esce dal PC.");
            AddChoice(AiProvider.Claude, "Claude (Anthropic)", "A pagamento: serve una chiave API della Claude Console.");

            var host = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x48)), BorderThickness = new Thickness(1),
                Background = Res("PanelHeaderBg"), Padding = new Thickness(14, 12, 14, 4), Margin = new Thickness(0, 6, 0, 4),
            };
            var stack = new Grid();
            host.Child = stack;
            Body.Children.Add(host);

            // ---- Gemini
            var gemini = new StackPanel();
            _geminiKey = KeyBox();
            gemini.Children.Add(Row(Label("Chiave API:", 90), _geminiKey));
            _geminiState = Wrap("", left: 98, top: -6, bottom: 4);
            gemini.Children.Add(_geminiState);
            gemini.Children.Add(LinkLine("Crea una chiave gratuita in ", "Google AI Studio", GeminiProvider.KeyUrl, " (pulsante \"Create API key\")."));
            _geminiModel = new ComboBox { Width = 200 };
            var geminiRefresh = new Button { Content = "Aggiorna elenco", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            geminiRefresh.Click += async (s, e) =>
            {
                string key = _geminiKey.Password.Trim();
                if (key.Length == 0) key = _settings.ResolveKey(AiProvider.Gemini);
                if (string.IsNullOrWhiteSpace(key)) { SetState(_geminiState, "Inserisci prima la chiave API.", true); return; }
                geminiRefresh.IsEnabled = false;
                SetState(_geminiState, "Lettura dei modelli disponibili...", false);
                try
                {
                    var names = await GeminiProvider.ListModelsAsync(key, CancellationToken.None);
                    string current = SelectedGeminiModel();
                    FillStrings(_geminiModel, names, names.Contains(current) ? current : names.FirstOrDefault(n => n == GeminiProvider.DefaultModel) ?? names.FirstOrDefault());
                    SetState(_geminiState, $"La chiave funziona: {names.Count} modelli disponibili.", false);
                }
                catch (Exception ex) { SetState(_geminiState, ex.Message, true); }
                finally { geminiRefresh.IsEnabled = true; }
            };
            gemini.Children.Add(Row(Label("Modello:", 90), _geminiModel, geminiRefresh));
            gemini.Children.Add(Wrap("Privacy: a Google viene inviata solo un'anteprima ridotta della foto (circa 1000 pixel) con i valori dei cursori. " +
                                     "Con il piano gratuito Google può usare i dati inviati per migliorare i suoi servizi: evitalo per le foto private. " +
                                     "Il piano gratuito ha un limite di richieste al minuto e al giorno.", bottom: 8));
            _geminiRemove = RemoveButton(AiProvider.Gemini, () => _geminiState);
            gemini.Children.Add(_geminiRemove);
            AddPanel(stack, AiProvider.Gemini, gemini);

            // ---- Ollama
            var ollama = new StackPanel();
            _ollamaUrl = new TextBox { Width = FieldWidth, Height = 24, Text = _settings.OllamaUrl };
            ollama.Children.Add(Row(Label("Indirizzo:", 90), _ollamaUrl));
            _ollamaModel = new ComboBox { Width = 200 };
            var ollamaRefresh = new Button { Content = "Aggiorna elenco", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            ollamaRefresh.Click += async (s, e) => { ollamaRefresh.IsEnabled = false; await RefreshOllama(); ollamaRefresh.IsEnabled = true; };
            ollama.Children.Add(Row(Label("Modello:", 90), _ollamaModel, ollamaRefresh));
            _ollamaState = Wrap("", left: 98, top: -6, bottom: 6);
            ollama.Children.Add(_ollamaState);
            ollama.Children.Add(LinkLine("Come iniziare: installa Ollama da ", "ollama.com", OllamaProvider.DownloadUrl,
                ", poi nel Prompt dei comandi scarica un modello che vede le immagini, ad esempio:"));
            ollama.Children.Add(new TextBox
            {
                Text = "ollama pull gemma3", IsReadOnly = true, FontFamily = new FontFamily("Consolas"), Width = 200,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 6), Height = 24,
            });
            ollama.Children.Add(Wrap("I modelli più grandi (es. gemma3:12b, qwen2.5vl:7b) danno risultati migliori ma richiedono più memoria e una buona scheda video. " +
                                     "Tutto resta sul tuo PC: nessun dato viene inviato su Internet.", bottom: 8));
            AddPanel(stack, AiProvider.Ollama, ollama);

            // ---- Claude
            var claude = new StackPanel();
            _claudeKey = KeyBox();
            claude.Children.Add(Row(Label("Chiave API:", 90), _claudeKey));
            _claudeState = Wrap("", left: 98, top: -6, bottom: 4);
            claude.Children.Add(_claudeState);
            claude.Children.Add(LinkLine("Puoi crearla nella ", "Claude Console", ClaudeProvider.KeyUrl, " (sezione API Keys)."));
            claude.Children.Add(Wrap("Privacy: ad Anthropic viene inviata solo un'anteprima ridotta della foto (circa 1000 pixel) con i valori dei cursori. " +
                                     "Ogni richiesta viene addebitata sul tuo account Anthropic.", top: 4, bottom: 8));
            _claudeRemove = RemoveButton(AiProvider.Claude, () => _claudeState);
            claude.Children.Add(_claudeRemove);
            AddPanel(stack, AiProvider.Claude, claude);

            Body.Children.Add(Wrap("Le chiavi vengono salvate cifrate con Windows e sono leggibili solo dal tuo utente.", top: 4, bottom: 0));

            FillStrings(_geminiModel, GeminiProvider.SuggestedModels.ToList(), string.IsNullOrWhiteSpace(_settings.GeminiModel) ? GeminiProvider.DefaultModel : _settings.GeminiModel);
            if (!string.IsNullOrWhiteSpace(_settings.OllamaModel))
            {
                _ollamaModels = new List<OllamaModel> { new OllamaModel { Name = _settings.OllamaModel } };
                FillOllama(_settings.OllamaModel);
            }
            UpdateKeyStates();
            var initial = select ?? _settings.Provider;
            _choices[initial].IsChecked = true;
            ShowPanel(initial);
        }

        public AiProvider SelectedProvider => _choices.First(c => c.Value.IsChecked == true).Key;

        void AddChoice(AiProvider p, string title, string description)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = description, Foreground = Res("TextDimBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            var rb = new RadioButton { Content = content, GroupName = "aiprovider", Margin = new Thickness(0, 0, 0, 8) };
            rb.Checked += (s, e) => ShowPanel(p);
            _choices[p] = rb;
            Body.Children.Add(rb);
        }

        void AddPanel(Grid host, AiProvider p, FrameworkElement panel)
        {
            panel.Visibility = Visibility.Hidden;   // hidden (not collapsed): the box keeps the size of the largest panel
            host.Children.Add(panel);
            _panels[p] = panel;
        }

        async void ShowPanel(AiProvider p)
        {
            foreach (var kv in _panels) kv.Value.Visibility = kv.Key == p ? Visibility.Visible : Visibility.Hidden;
            if (p == AiProvider.Ollama && !_ollamaListed)
            {
                _ollamaListed = true;
                await RefreshOllama();
            }
        }

        async System.Threading.Tasks.Task RefreshOllama()
        {
            SetState(_ollamaState, "Ricerca dei modelli installati...", false);
            try
            {
                string current = (_ollamaModel.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.OllamaModel;
                _ollamaModels = await OllamaProvider.ListModelsAsync(_ollamaUrl.Text, CancellationToken.None);
                if (_ollamaModels.Count == 0)
                {
                    FillOllama(null);
                    SetState(_ollamaState, "Ollama è attivo ma non ha modelli installati: scaricane uno (vedi sotto) e premi Aggiorna elenco.", true);
                    return;
                }
                var pick = _ollamaModels.FirstOrDefault(m => m.Name == current) ?? _ollamaModels.FirstOrDefault(m => m.Vision == true) ?? _ollamaModels[0];
                FillOllama(pick.Name);
                int vision = _ollamaModels.Count(m => m.Vision == true);
                SetState(_ollamaState, $"Ollama è attivo: {_ollamaModels.Count} modelli installati, {vision} in grado di vedere le immagini.", vision == 0);
            }
            catch (Exception ex)
            {
                SetState(_ollamaState, ex.Message, true);
            }
        }

        void FillOllama(string select)
        {
            _ollamaModel.Items.Clear();
            foreach (var m in _ollamaModels)
            {
                string note = m.Vision == true ? "  (visione)" : m.Vision == false ? "  (senza visione)" : "";
                string size = string.IsNullOrEmpty(m.Size) ? "" : "  " + m.Size;
                var item = new ComboBoxItem { Content = m.Name + size + note, Tag = m.Name };
                _ollamaModel.Items.Add(item);
                if (m.Name == select) _ollamaModel.SelectedItem = item;
            }
        }

        static void FillStrings(ComboBox combo, List<string> names, string select)
        {
            if (!string.IsNullOrWhiteSpace(select) && !names.Contains(select)) names.Insert(0, select);
            combo.Items.Clear();
            foreach (var n in names) combo.Items.Add(new ComboBoxItem { Content = n, Tag = n });
            combo.SelectedIndex = Math.Max(0, names.IndexOf(select));
        }

        string SelectedGeminiModel() => (_geminiModel.SelectedItem as ComboBoxItem)?.Tag as string ?? GeminiProvider.DefaultModel;

        void UpdateKeyStates()
        {
            foreach (var (p, state, remove) in new[] { (AiProvider.Gemini, _geminiState, _geminiRemove), (AiProvider.Claude, _claudeState, _claudeRemove) })
            {
                string env = AiSettings.EnvironmentKey(p);
                string text = env != null ? $"È impostata la variabile d'ambiente {env}: viene usata quella."
                    : KeyStore.HasSavedKey(p) ? "Una chiave è già salvata. Inseriscine una nuova solo per sostituirla."
                    : "Nessuna chiave salvata.";
                SetState(state, text, false);
                remove.IsEnabled = KeyStore.HasSavedKey(p);
            }
        }

        Button RemoveButton(AiProvider p, Func<TextBlock> state)
        {
            var b = new Button { Content = "Rimuovi chiave salvata", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 0, 10) };
            b.Click += (s, e) =>
            {
                KeyStore.Delete(p);
                UpdateKeyStates();
                SetState(state(), "Chiave rimossa.", false);
            };
            return b;
        }

        protected override void OnOk()
        {
            var provider = SelectedProvider;
            string gk = _geminiKey.Password.Trim(), ck = _claudeKey.Password.Trim();
            if (gk.Length > 0 && (gk.Length < 20 || gk.Any(char.IsWhiteSpace)))
            {
                Error("La chiave di Gemini non sembra valida: copiala di nuovo da Google AI Studio.");
                return;
            }
            if (ck.Length > 0 && !ck.StartsWith("sk-ant-", StringComparison.Ordinal))
            {
                Error("La chiave non sembra valida: le chiavi API di Anthropic iniziano con \"sk-ant-\".");
                return;
            }
            if (gk.Length > 0) KeyStore.Save(AiProvider.Gemini, gk);
            if (ck.Length > 0) KeyStore.Save(AiProvider.Claude, ck);

            _settings.Provider = provider;
            _settings.GeminiModel = SelectedGeminiModel();
            _settings.OllamaUrl = OllamaProvider.NormalizeUrl(_ollamaUrl.Text);
            _settings.OllamaModel = (_ollamaModel.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

            string missing = AiEditor.MissingConfiguration(_settings);
            if (missing != null)
            {
                Error(missing + (provider == AiProvider.Ollama
                    ? " Avvia Ollama e premi \"Aggiorna elenco\"."
                    : " Inserisci la chiave API oppure scegli un altro servizio."));
                UpdateKeyStates();
                return;
            }
            if (provider == AiProvider.Ollama && _ollamaModels.FirstOrDefault(m => m.Name == _settings.OllamaModel)?.Vision == false &&
                MessageBox.Show(this, $"Il modello \"{_settings.OllamaModel}\" non sembra in grado di vedere le immagini, quindi non potrà valutare la foto.\n\nUsarlo comunque?",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _settings.Save();
            base.OnOk();
        }

        // ---------- helpers ----------

        static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        static PasswordBox KeyBox() => new PasswordBox
        {
            Width = FieldWidth, Height = 24, Padding = new Thickness(4, 3, 4, 3),
            Background = Res("InputBg"), Foreground = Res("TextBrush"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), CaretBrush = Brushes.White,
        };

        static TextBlock Wrap(string text, bool dim = true, double left = 0, double top = 0, double bottom = 6) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(left, top, 0, bottom),
            Foreground = dim ? Res("TextDimBrush") : Res("TextBrush"),
        };

        static void SetState(TextBlock t, string text, bool error)
        {
            t.Text = text;
            t.Foreground = error ? new SolidColorBrush(Color.FromRgb(0xF0, 0x86, 0x76)) : Res("TextDimBrush");
        }

        static TextBlock LinkLine(string before, string linkText, string url, string after)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 0, 0, 8) };
            t.Inlines.Add(new Run(before));
            var link = new Hyperlink(new Run(linkText)) { NavigateUri = new Uri(url), Foreground = Res("AccentBrush") };
            link.RequestNavigate += (s, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; };
            t.Inlines.Add(link);
            t.Inlines.Add(new Run(after));
            return t;
        }
    }
}
