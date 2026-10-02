using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio.Dialogs
{
    /// <summary>
    /// Modifica ▸ Spazio usato dall'AI: how much disk the saved AI results and the downloaded networks take,
    /// with a button to empty each.
    /// </summary>
    public sealed class AiStorageDialog : DialogWindow
    {
        readonly TextBlock _results, _models;
        readonly Button _clearResults, _clearModels;

        public AiStorageDialog(Window owner) : base(owner, T("Spazio usato dall'AI"))
        {
            OkButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = T("Chiudi");
            Width = 460;
            SizeToContent = SizeToContent.Height;

            Body.Children.Add(Heading(T("Risultati salvati")));
            _results = Text();
            Body.Children.Add(_results);
            Body.Children.Add(Note(T("La riduzione rumore e la rimessa a fuoco AI salvano il loro risultato, così riaprire una foto è immediato. Se li elimini, vengono rifatti quando servono.")));
            _clearResults = new Button { Content = T("Elimina i risultati salvati"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0) };
            _clearResults.Click += (s, e) => { Delete(RestoreCache.Folder, "*.*"); Refresh(); };
            Body.Children.Add(_clearResults);

            Body.Children.Add(Heading(T("Modelli AI scaricati")));
            _models = Text();
            Body.Children.Add(_models);
            Body.Children.Add(Note(T("Se li elimini, vengono scaricati di nuovo (dopo averlo chiesto) la prossima volta che usi lo strumento.")));
            _clearModels = new Button { Content = T("Elimina i modelli scaricati"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 8) };
            _clearModels.Click += (s, e) =>
            {
                if (MessageBox.Show(this, T("Eliminare i modelli AI scaricati?\nPer usare di nuovo gli strumenti AI andranno riscaricati."), Title,
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                foreach (var m in AiModels.Downloadable) Delete(m.Path);
                Refresh();
            };
            Body.Children.Add(_clearModels);
            Refresh();
        }

        static TextBlock Heading(string text) => new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        static TextBlock Text() => new TextBlock { TextWrapping = TextWrapping.Wrap };
        static TextBlock Note(string text) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextDimBrush"),
        };

        static string Size(long bytes) => bytes >= 1L << 30 ? T("{0:0.0} GB", bytes / (double)(1L << 30)) : T("{0:0} MB", bytes / 1048576.0);

        void Refresh()
        {
            var results = Directory.Exists(RestoreCache.Folder) ? new DirectoryInfo(RestoreCache.Folder).GetFiles("*.bin") : Array.Empty<FileInfo>();
            long rb = results.Sum(f => f.Length);
            _results.Text = results.Length == 0 ? T("Nessun risultato salvato.") : T("{0} in {1} file (al massimo 4 GB: oltre, si eliminano i più vecchi).", Size(rb), results.Length);
            _clearResults.IsEnabled = results.Length > 0;

            var installed = AiModels.Downloadable.Where(m => File.Exists(m.Path)).ToList();
            _models.Text = installed.Count == 0
                ? T("Nessun modello scaricato.")
                : string.Join("\n", installed.Select(m => $"{ModelName(m)}: {Size(new FileInfo(m.Path).Length)}"));
            _clearModels.IsEnabled = installed.Count > 0;
        }

        static string ModelName(AiModels.Model m) =>
            m == AiModels.Denoise ? T("Riduzione rumore AI") : m == AiModels.Deblur ? T("Rimessa a fuoco AI")
            : m == AiModels.Inpaint ? T("Rimuovi con AI") : T("Soggetto AI (maschere)");

        static void Delete(string folder, string pattern)
        {
            try { if (Directory.Exists(folder)) foreach (var f in Directory.GetFiles(folder, pattern)) Delete(f); } catch { }
        }

        static void Delete(string file)
        {
            try { File.Delete(file); } catch { }
        }
    }
}
