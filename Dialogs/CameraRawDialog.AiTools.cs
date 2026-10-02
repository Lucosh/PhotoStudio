using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    // Dettagli tab, "AI sul tuo PC": noise reduction and refocus done by neural networks on the RAW data.
    public sealed partial class CameraRawDialog
    {
        ComboBox _refocusArea;
        bool _downloading, _aiStopped;
        CancellationTokenSource _downloadCts;

        void BuildAiDetailSection(Panel p)
        {
            Section(p, T("AI SUL TUO PC"));
            Row(p, T("Rumore AI"), 0, 100, 0, x => x.AiDenoise, (x, v) => x.AiDenoise = v,
                changed: () => EnsureModel(AiModels.Denoise, x => x.AiDenoise = 0),
                tip: T("Toglie la grana degli alti ISO con una rete neurale, conservando i dettagli meglio del cursore Luminanza"));
            Row(p, T("Messa a fuoco AI"), 0, 150, 0, x => x.AiRefocus, (x, v) => x.AiRefocus = v,
                changed: () => EnsureModel(AiModels.Deblur, x => x.AiRefocus = 0),
                tip: T("Recupera la nitidezza persa per un leggero mosso o una messa a fuoco mancata"));

            var area = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            area.ColumnDefinitions.Add(new ColumnDefinition());
            area.Children.Add(new TextBlock { Text = T("Zona"), VerticalAlignment = VerticalAlignment.Center });
            _refocusArea = new ComboBox { ItemsSource = new[] { T("Soggetto (visi e persone, o il centro)"), T("Tutta la foto") }, Focusable = false };
            _refocusArea.SelectionChanged += (s, e) =>
            {
                if (_loading || _refocusArea.SelectedIndex < 0) return;
                _s.AiRefocusArea = (RefocusArea)_refocusArea.SelectedIndex;
                Edited();
            };
            Grid.SetColumn(_refocusArea, 1);
            area.Children.Add(_refocusArea);
            p.Children.Add(area);
            p.Children.Add(Hint(T("Le reti lavorano sul tuo PC (sulla scheda video, se possibile) e nulla viene inviato. La prima volta si scarica il modello; poi l'anteprima richiede qualche secondo, e Ok la rifà a piena risoluzione: per una foto da 24 MP servono da uno a qualche minuto, e Interrompi la ferma. Il risultato resta salvato: riaprendo la foto è subito pronto.")));

            AiRestore.Progress += OnAiProgress;
            Closed += (s, e) => AiRestore.Progress -= OnAiProgress;
        }

        void RefreshAiDetail()
        {
            if (_refocusArea == null) return;
            bool was = _loading;
            _loading = true;
            _refocusArea.SelectedIndex = (int)_s.AiRefocusArea;
            _loading = was;
        }

        void OnAiProgress(string label, double fraction)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // After Interrompi the messages still queued from the stopped work must not cover "interrotta".
                if (_closed || _aiStopped) return;
                _status.Text = fraction >= 1 ? T("{0}: fatto.", label) : T("{0}: {1:0}%...", label, fraction * 100);
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// The first time a network is needed it is downloaded, after asking; without it the slider goes back to 0.
        /// </summary>
        async void EnsureModel(AiModels.Model model, Action<RawSettings> off)
        {
            if (model.Installed || _downloading) return;
            void TurnOff()
            {
                off(_s);
                RefreshSliders();
                Schedule();
            }
            var answer = MessageBox.Show(this,
                T("Questo strumento usa una rete neurale che va scaricata una sola volta ({0:0} MB) da GitHub.\nPoi funziona senza internet e senza inviare le foto.\n\nScaricarla adesso?", model.Size / 1048576.0),
                "Camera Raw", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) { TurnOff(); return; }

            _downloading = true;
            _downloadCts = new CancellationTokenSource();
            var progress = new Progress<double>(f => { if (!_closed) _status.Text = T("Download del modello AI: {0:0}%...", f * 100); });
            try
            {
                await AiModels.DownloadAsync(model, progress, _downloadCts.Token);
                if (_closed) return;
                _status.Text = T("Modello AI installato.");
                Schedule();
            }
            catch (Exception ex)
            {
                if (_closed) return;
                _status.Text = "";
                if (!(ex is OperationCanceledException))
                    MessageBox.Show(this, T("Download non riuscito:\n{0}", ex.Message), "Camera Raw", MessageBoxButton.OK, MessageBoxImage.Warning);
                TurnOff();
            }
            finally
            {
                _downloading = false;
            }
        }
    }
}
