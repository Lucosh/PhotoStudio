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

        // ================= Rimuovi con AI =================

        /// <summary>Modifica ▸ Rimuovi con AI: what is selected on the active layer goes, and the background behind it is rebuilt.</summary>
        async void AiRemove()
        {
            var layer = ActiveLayer;
            if (layer == null) return;
            if (S.Selection == null || S.Selection.IsEmpty)
            {
                Status(T("Seleziona prima l'oggetto da togliere (lazo, bacchetta magica o Selezione ▸ Soggetto AI), poi Rimuovi con AI."));
                return;
            }
            string title = T("Rimuovi con AI");
            if (!await AiDownload.EnsureAsync(this, AiModels.Inpaint, title)) return;
            var s = S;
            int w = Doc.Width, h = Doc.Height;
            var src = layer.Pixels;
            var mask = S.Selection.Mask;
            var progress = new ProgressWindow(this, title, 100);
            using var cts = new CancellationTokenSource();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (o, e) => { if (progress.Cancelled) cts.Cancel(); };
            timer.Start();
            progress.Report(0, T("La rete AI ricostruisce lo sfondo..."));
            progress.Show();
            IsEnabled = false;
            byte[] result;
            try
            {
                result = await Task.Run(() => AiInpaint.Remove(src, w, h, mask, f =>
                    Dispatcher.BeginInvoke(new Action(() => progress.Report((int)(f * 100), T("La rete AI ricostruisce lo sfondo: {0:0}%", f * 100)))), cts.Token));
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    MessageBox.Show(this, T("Rimozione non riuscita:\n{0}", ex.Message), title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                timer.Stop();
                progress.Finish();
                IsEnabled = true;
            }
            // The photo, the layer or the selection may have changed meanwhile: then the result does not belong.
            if (S != s || ActiveLayer != layer || layer.Pixels != src || Doc.Width != w || Doc.Height != h) return;
            layer.Pixels = result;
            Recomposite();
            Commit(title);
            Status(T("Oggetto rimosso. Ctrl+Z per tornare indietro; se resta qualche traccia, selezionala e ripeti."));
        }

        /// <summary>Selezione ▸ Soggetto AI: selects the main subject of the photo (IS-Net, the network of the AI subject masks).</summary>
        async void SelectSubject()
        {
            string title = T("Soggetto AI");
            if (!await AiDownload.EnsureAsync(this, AiModels.Subject, title)) return;
            var s = S;
            int w = Doc.Width, h = Doc.Height;
            var composite = Doc.Render();
            byte[] mask;
            using (Busy())
            {
                mask = await Task.Run(() =>
                {
                    var map = SubjectMap.Detect(RawImage.FromBgra(composite, w, h));
                    if (map == null) return null;
                    var m = new byte[w * h];
                    Parallel.For(0, h, y =>
                    {
                        for (int x = 0; x < w; x++) m[y * w + x] = (byte)(map.Weight(x, y, w, h) * 255 + 0.5f);
                    });
                    return m;
                });
            }
            if (S != s || Doc.Width != w || Doc.Height != h) return;
            if (mask == null) { Status(T("Nessun soggetto riconosciuto in questa foto.")); return; }
            SetSelection(Selection.FromMask(mask, w, h), title);
            Status(T("Soggetto selezionato. Ora puoi ritoccarlo, invertire la selezione o toglierlo con Modifica ▸ Rimuovi con AI."));
        }
    }
}
