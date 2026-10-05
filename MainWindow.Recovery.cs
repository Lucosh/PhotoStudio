using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using PhotoStudio.Core;
using static PhotoStudio.Core.Loc;

namespace PhotoStudio
{
    // The safety copy of the open tabs (Core/Recovery.cs): written after every change, offered back after a crash.
    public partial class MainWindow
    {
        DispatcherTimer _backupTimer;
        bool _backupWarned;

        void InitRecovery()
        {
            _backupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _backupTimer.Tick += (s, e) => { _backupTimer.Stop(); WriteBackup(); };
            Recovery.Written += ok => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ok) { _backupWarned = false; return; }
                if (_backupWarned) return;
                _backupWarned = true;
                Status(T("Attenzione: spazio su disco insufficiente per la copia di sicurezza delle foto aperte. Salva il lavoro."));
            }));
        }

        /// <summary>The tabs have changed: their safety copy is brought up to date shortly.</summary>
        void ScheduleBackup()
        {
            _backupTimer.Stop();
            _backupTimer.Start();
        }

        /// <summary>Takes the current state of every tab (as after its last change) and writes it in the background.</summary>
        void WriteBackup()
        {
            var items = new List<(RecoveryItem Item, bool HandEdited, long Used)>();
            int order = 0;
            foreach (var s in _sessions)
            {
                order++;
                if (s.History.Index < 0 || s.History.Index >= s.History.Entries.Count) continue;
                var st = s.History.Entries[s.History.Index].State;
                if (st.Layers.Count == 0 || st.Layers.Any(l => l.Pixels == null)) continue;
                var tab = new RecoveryTab
                {
                    Order = order,
                    Title = s.Title,
                    SourcePath = s.SourcePath,
                    FilePath = s.FilePath,
                    Developed = s.IsAsDeveloped ? s.RawSettings?.Clone() : null,
                    Modified = s.Modified,
                    Width = st.Width,
                    Height = st.Height,
                    ActiveIndex = st.ActiveIndex,
                    JpegQuality = s.JpegQuality,
                    Layers = st.Layers.Select(l => new RecoveryLayer { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity, Blend = l.Blend }).ToList(),
                };
                items.Add((new RecoveryItem(tab, st.Layers.Select(l => l.Pixels).ToArray()), !s.IsAsDeveloped && s.Modified, s.LastUsed));
            }
            // The edits made by hand first (nothing else could give them back), then the tabs used most recently.
            Recovery.SaveLater(items.OrderByDescending(i => i.HandEdited).ThenByDescending(i => i.Used).Select(i => i.Item).ToList());
        }

        /// <summary>At start: the tabs left open by a PhotoStudio that stopped without closing are offered back.</summary>
        public void OfferRecovery()
        {
            var found = Recovery.FindAbandoned();
            int tabs = found.Sum(f => f.Tabs);
            if (tabs == 0) return;
            var answer = MessageBox.Show(this,
                T("PhotoStudio non è stato chiuso normalmente, ma le {0} foto che erano aperte sono al sicuro.\n\nSì: riaprile come erano, con le ultime modifiche\nNo: eliminale\nAnnulla: decidi al prossimo avvio", tabs),
                T("Recupero delle foto"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.No)
            {
                if (MessageBox.Show(this, T("Le {0} foto recuperabili verranno eliminate definitivamente. Continuare?", tabs),
                        T("Recupero delle foto"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    foreach (var f in found) Recovery.Discard(f.Folder);
                return;
            }

            List<RecoveryItem> items;
            int lost;
            using (Busy()) items = Recovery.Load(found.Select(f => f.Folder), out lost);
            RestoreTabs(items);
            Status(lost == 0
                ? T("Recuperate {0} foto.", items.Count)
                : T("Recuperate {0} foto; {1} non erano più leggibili.", items.Count, lost));
        }

        /// <summary>Opens the recovered tabs as they were; their pixels stay on disk until each tab is shown.</summary>
        void RestoreTabs(List<RecoveryItem> items)
        {
            Session last = null;
            foreach (var (t, px) in items)
            {
                var doc = new Document(t.Width, t.Height);
                for (int i = 0; i < px.Length; i++)
                {
                    var rl = t.Layers[i];
                    var layer = Layer.FromFrozen(rl.Name, t.Width, t.Height, px[i]);
                    layer.Visible = rl.Visible;
                    layer.Opacity = rl.Opacity;
                    layer.Blend = rl.Blend;
                    doc.Layers.Add(layer);
                }
                int active = Math.Clamp(t.ActiveIndex, 0, doc.Layers.Count - 1);
                var s = new Session(doc, t.Title)
                {
                    SourcePath = t.SourcePath, FilePath = t.FilePath, JpegQuality = t.JpegQuality, ActiveIndex = active,
                };
                s.History.Push(T("Recuperata"), doc.Snapshot(active, null));
                if (t.Developed != null) s.MarkDeveloped(t.Developed);
                s.Modified = t.Modified;
                _sessions.Add(s);
                last = s;
            }
            if (last != null) ActivateSession(last);
        }
    }
}
