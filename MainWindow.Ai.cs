using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    // Immagine ▸ Ingrandimento AI 2×, and Modifica ▸ Spazio usato dall'AI.
    public partial class MainWindow
    {
        async void AiUpscaleImage()
        {
            int ow = Doc.Width, oh = Doc.Height, nw = 2 * ow, nh = 2 * oh;
            double mp = nw * (double)nh / 1e6;
            if (mp > 400)
            {
                MessageBox.Show(this, T("L'immagine diventerebbe di {0:0} megapixel: troppo grande. Riducila prima con Immagine ▸ Dimensione immagine.", mp),
                    T("Ingrandimento AI 2×"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this,
                    T("La rete AI raddoppia larghezza e altezza ricostruendo i dettagli: da {0} × {1} a {2} × {3} pixel.\nGira sul tuo PC (sulla scheda video, se possibile) e può richiedere da qualche secondo a qualche minuto.\n\nContinuare?", ow, oh, nw, nh),
                    T("Ingrandimento AI 2×"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            var s = S;
            var layers = Doc.Layers.ToList();
            var progress = new ProgressWindow(this, T("Ingrandimento AI 2×"), 1000);
            using var cts = new CancellationTokenSource();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (o, e) => { if (progress.Cancelled) cts.Cancel(); };
            timer.Start();
            progress.Show();
            IsEnabled = false;
            var results = new Dictionary<byte[], byte[]>(ReferenceEqualityComparer.Instance);
            try
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var src = layers[i].Pixels;
                    int index = i;
                    string text = layers.Count == 1 ? T("Ingrandimento") : T("Livello {0} di {1}", i + 1, layers.Count);
                    results[src] = await Task.Run(() => AiUpscale.Upscale2x(src, ow, oh, f =>
                        Dispatcher.BeginInvoke(new Action(() => progress.Report((int)((index + f) / layers.Count * 1000), T("{0}: {1:0}%", text, f * 100)))),
                        cts.Token));
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                progress.Finish();
                IsEnabled = true;
                if (!(ex is OperationCanceledException))
                    MessageBox.Show(this, T("Ingrandimento non riuscito:\n{0}", ex.Message), T("Ingrandimento AI 2×"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            timer.Stop();
            progress.Finish();
            IsEnabled = true;
            if (S != s || Doc.Width != ow || Doc.Height != oh) return;   // the photo changed meanwhile
            TransformImage(px => results.TryGetValue(px, out var r) ? r : ImageOps.Resize(px, ow, oh, nw, nh), nw, nh, T("Ingrandimento AI 2×"));
            _ = Dispatcher.BeginInvoke(new Action(() => FitToScreen(true)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void ShowAiStorage() => new AiStorageDialog(this).ShowDialog();
    }
}
