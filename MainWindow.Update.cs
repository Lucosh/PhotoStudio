using System;
using System.Threading;
using System.Windows;
using PhotoStudio.Core;
using PhotoStudio.Dialogs;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    // Aiuto ▸ Cerca aggiornamenti, and the quiet check once a day at startup.
    public partial class MainWindow
    {
        bool _checkingUpdates;

        /// <summary>At startup: at most once a day, and silent unless there is a version the user has not turned down.</summary>
        public void CheckForUpdatesInBackground()
        {
            var o = Updater.Options.Load();
            if (!o.AutoCheck || DateTime.UtcNow - o.LastCheck < TimeSpan.FromHours(20)) return;
            CheckForUpdates(false);
        }

        async void CheckForUpdates(bool manual)
        {
            if (_checkingUpdates) return;
            _checkingUpdates = true;
            var o = Updater.Options.Load();
            Updater.Release r;
            try
            {
                r = await Updater.CheckAsync();
                o.LastCheck = DateTime.UtcNow;
                o.Save();
            }
            catch (Exception ex)
            {
                if (manual)
                    MessageBox.Show(this, T("Impossibile controllare gli aggiornamenti:\n{0}", ex.Message), T("Aggiornamenti"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                _checkingUpdates = false;
            }

            if (r == null)
            {
                if (manual)
                    MessageBox.Show(this, T("Hai già la versione più recente di PhotoStudio ({0}).", Updater.Current.ToString(3)), T("Aggiornamenti"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // A version turned down once is not proposed again at startup; the menu still finds it.
            if (!manual && o.SkippedVersion == r.Tag) return;

            string question = Updater.Installed
                ? T("È disponibile PhotoStudio {0} (hai la {1}).\n\nAggiornare adesso? PhotoStudio si chiuderà, si aggiornerà e si riaprirà da solo.", r.Version.ToString(3), Updater.Current.ToString(3))
                : T("È disponibile PhotoStudio {0} (hai la {1}).\n\nAprire la pagina per scaricare la nuova versione portatile?", r.Version.ToString(3), Updater.Current.ToString(3));
            var answer = MessageBox.Show(this, question, T("Aggiornamenti"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                o.SkippedVersion = r.Tag;
                o.Save();
                return;
            }
            if (!Updater.Installed || string.IsNullOrEmpty(r.SetupUrl)) { Updater.OpenPage(r.Page); return; }
            await InstallUpdate(r);
        }

        async System.Threading.Tasks.Task InstallUpdate(Updater.Release r)
        {
            var progress = new ProgressWindow(this, T("Aggiornamento a PhotoStudio {0}", r.Version.ToString(3)), 100);
            using var cts = new CancellationTokenSource();
            progress.Closing += (s, e) => { if (progress.Cancelled) cts.Cancel(); };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (s, e) => { if (progress.Cancelled) cts.Cancel(); };
            timer.Start();
            progress.Show();
            string path;
            try
            {
                path = await Updater.DownloadAsync(r, new Progress<double>(f => progress.Report((int)(f * 100), T("Download: {0:0}%", f * 100))), cts.Token);
            }
            catch (Exception ex)
            {
                timer.Stop();
                progress.Finish();
                if (!(ex is OperationCanceledException))
                    MessageBox.Show(this, T("Aggiornamento non riuscito:\n{0}", ex.Message), T("Aggiornamenti"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            timer.Stop();
            progress.Finish();

            // The installer starts once the window has really closed (the user may still save or keep open photos).
            EventHandler start = (s, e) => Updater.RunInstaller(path);
            Closed += start;
            Close();
            if (IsVisible) Closed -= start;   // closing was cancelled
        }
    }
}
