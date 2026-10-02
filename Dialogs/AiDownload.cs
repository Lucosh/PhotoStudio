using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>The first time an AI network is needed outside Camera Raw: asks, then downloads it with a progress window.</summary>
    public static class AiDownload
    {
        /// <summary>True when the network is installed, after downloading it if the user agrees.</summary>
        public static async Task<bool> EnsureAsync(Window owner, AiModels.Model model, string title)
        {
            if (model.Installed) return true;
            if (MessageBox.Show(owner,
                    T("Questo strumento usa una rete neurale che va scaricata una sola volta ({0:0} MB) da GitHub.\nPoi funziona senza internet e senza inviare le foto.\n\nScaricarla adesso?", model.Size / 1048576.0),
                    title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            var progress = new ProgressWindow(owner, title, 100);
            using var cts = new CancellationTokenSource();
            progress.Closing += (s, e) => { if (progress.Cancelled) cts.Cancel(); };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (s, e) => { if (progress.Cancelled) cts.Cancel(); };
            timer.Start();
            progress.Show();
            try
            {
                await AiModels.DownloadAsync(model, new Progress<double>(f => progress.Report((int)(f * 100), T("Download del modello AI: {0:0}%...", f * 100))), cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    MessageBox.Show(owner, T("Download non riuscito:\n{0}", ex.Message), title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                timer.Stop();
                progress.Finish();
            }
        }
    }
}
