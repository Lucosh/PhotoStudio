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
            MessageBox.Show(T("Si è verificato un errore imprevisto:\n\n{0}", e.Exception.Message),
                "PhotoStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
