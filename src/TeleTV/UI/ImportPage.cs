using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>Import APK: drag and drop or browse for APK files on this PC and install them into Android TV.</summary>
    public class ImportPage : Grid, IPage
    {
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _empty = Theme.T("Nothing imported yet.", 14, Theme.Muted);
        private readonly Border _drop;
        private readonly TextBlock _engineNote = Theme.T("", 13, Theme.Orange, wrap: true);

        public ImportPage()
        {
            Background = Theme.Deep;
            var content = new StackPanel();

            var head = Theme.Stack(6,
                Theme.T("Import APK from this PC", 24, Theme.Text, FontWeights.Bold),
                Theme.T("Apps install straight into Android TV and appear on your home screen.", 14, Theme.Muted, wrap: true));
            content.Children.Add(head);

            var dropInner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = Theme.Icon(Theme.GUpload, 30, Theme.AccentSoft);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            dropInner.Children.Add(icon);
            var t1 = Theme.T("Drag .apk files here", 15, Theme.Text, FontWeights.SemiBold);
            t1.HorizontalAlignment = HorizontalAlignment.Center;
            t1.Margin = new Thickness(0, 10, 0, 4);
            dropInner.Children.Add(t1);
            var t2 = Theme.T("or drop them anywhere on the TeleTV window. .apks and .xapk bundles work too.", 13, Theme.Muted);
            t2.HorizontalAlignment = HorizontalAlignment.Center;
            dropInner.Children.Add(t2);
            _drop = new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(12),
                Background = Theme.B("#121A26"),
                BorderBrush = Theme.B("#4A6A9A"),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 22, 0, 0),
                Child = dropInner,
                Cursor = System.Windows.Input.Cursors.Hand,
                AllowDrop = true
            };
            _drop.MouseLeftButtonUp += (o, e) => Browse();
            content.Children.Add(_drop);

            _engineNote.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(_engineNote);

            var listHead = new Grid { Margin = new Thickness(0, 22, 0, 10) };
            listHead.Children.Add(Theme.T("Imports", 16, Theme.Text, FontWeights.SemiBold));
            var clear = Theme.Btn("Clear finished", "Ghost", (o, e) => InstallQueue.ClearFinished());
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            listHead.Children.Add(clear);
            content.Children.Add(listHead);
            content.Children.Add(_empty);
            content.Children.Add(_list);

            var bottom = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            var open = Theme.Switch(Settings.Current.OpenAfterInstall, v => { Settings.Current.OpenAfterInstall = v; Settings.Save(); });
            var openRow = Theme.Row(10, open, Theme.T("Open each app when it's installed", 14, Theme.Sub));
            bottom.Children.Add(openRow);
            var buttons = Theme.Row(10,
                Theme.Btn("Done", "Secondary", (o, e) => MainWindow.Instance.Show(PageId.Tv)),
                Theme.Btn("Browse files", "Primary", (o, e) => Browse(), Theme.GFolder));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            bottom.Children.Add(buttons);
            content.Children.Add(bottom);

            var card = Theme.Card(content, new Thickness(32));
            card.MaxWidth = 720;
            card.Margin = new Thickness(24, 32, 24, 32);
            card.VerticalAlignment = VerticalAlignment.Top;
            Children.Add(Theme.Scroll(card));

            InstallQueue.Items.CollectionChanged += (o, e) => Rebuild();
            InstallQueue.Changed += i => Rebuild();
            Engine.StateChanged += () => Dispatcher.BeginInvoke(new Action(UpdateNote));
            Rebuild();
        }

        public void OnShown()
        {
            Rebuild();
            UpdateNote();
        }

        private void UpdateNote()
        {
            _engineNote.Text = Engine.IsRunning ? "" : "Android TV isn't running. Imports wait and install as soon as it starts.";
            _engineNote.Visibility = Engine.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Browse()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose apps to install",
                Filter = "Android apps (*.apk;*.apks;*.xapk)|*.apk;*.apks;*.xapk|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(MainWindow.Instance) != true) return;
            foreach (var f in dlg.FileNames.Where(InstallQueue.IsApk)) InstallQueue.Add(f, "From " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f)));
        }

        private void Rebuild()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Rebuild)); return; }
            _list.Children.Clear();
            _empty.Visibility = InstallQueue.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in InstallQueue.Items.Take(30)) _list.Children.Add(Row(item));
        }

        private static readonly Brush[] Swatches = { Theme.B("#B5473F"), Theme.B("#6A4BB8"), Theme.B("#3F7A47"), Theme.B("#2F6BC4"), Theme.B("#E8A23A"), Theme.B("#3C4452") };

        private UIElement Row(InstallItem item)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = Swatches[Math.Abs(item.Name.GetHashCode()) % Swatches.Length], VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(swatch);

            var mid = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(Theme.T(item.Name, 14, Theme.Text, FontWeights.SemiBold));
            if (item.Status == InstallStatus.Installing)
            {
                var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 7, 0, 0) };
                mid.Children.Add(bar);
            }
            else if (item.Status == InstallStatus.Failed)
                mid.Children.Add(Theme.T(item.Message, 12, Theme.Red, wrap: true));
            else
                mid.Children.Add(Theme.T(Format.Size(item.Size) + " · " + item.Source, 12, Theme.Muted));
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            FrameworkElement right = item.Status switch
            {
                InstallStatus.Installed => string.IsNullOrEmpty(item.Package)
                    ? Theme.Row(6, Theme.Icon(Theme.GTick, 14, Theme.Green), Theme.T("Installed", 13, Theme.Green))
                    : Theme.Row(10, Theme.Row(6, Theme.Icon(Theme.GTick, 14, Theme.Green), Theme.T("Installed", 13, Theme.Green)),
                        Theme.Btn("Open", "Secondary", async (o, e) => { await Adb.Launch(item.Package); MainWindow.Instance.Show(PageId.Tv); })),
                InstallStatus.Installing => Theme.T("Installing", 13, Theme.AccentSoft),
                InstallStatus.Failed => Theme.Btn("Try again", "Secondary", (o, e) => InstallQueue.Add(item.File, item.Source)),
                _ => Theme.T(Engine.IsRunning ? "Waiting" : "Waits for Android", 13, Theme.Muted)
            };
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            return new Border { Background = Theme.Surface2, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = g };
        }
    }
}
