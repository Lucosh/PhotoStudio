using System.Linq;
using System.Windows;
using System.Windows.Threading;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    public partial class App : Application
    {
        // Before anything else, so that every text (including static tables) is created in the chosen language.
        public App() => Core.Loc.Init();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new System.Action(window.OfferRecovery));
            window.CheckForUpdatesInBackground();

            foreach (var arg in e.Args)
            {
                if (System.IO.Directory.Exists(arg))
                    Dispatcher.BeginInvoke(new System.Action(() => window.StartCulling(arg)));   // a folder: Preselezione
                else
                    window.OpenFile(arg);
            }
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;
            // The parallel loops wrap the error of each thread: one copy of each message is enough.
            System.Collections.Generic.IEnumerable<System.Exception> inner = ex is System.AggregateException a ? a.Flatten().InnerExceptions : new[] { ex };
            string text;
            if (Core.Errors.IsOutOfMemory(ex))
            {
                Core.Errors.ReleaseMemory();
                text = T("Memoria insufficiente per completare l'operazione.\nChiudi alcune schede o altri programmi e riprova.");
            }
            else
                text = T("Si è verificato un errore imprevisto:\n\n{0}", string.Join("\n", inner.Select(x => x.Message).Distinct()));
            MessageBox.Show(text, "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
