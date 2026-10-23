using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>The apps you've added to Android TV, with open, stop, clear data and uninstall.</summary>
    public class AppsPage : Grid, IPage
    {
        private readonly WrapPanel _grid = new WrapPanel();
        private readonly TextBlock _note = Theme.T("", 14, Theme.Muted, wrap: true);
        private bool _loading;

        private static readonly Brush[] Swatches = { Theme.B("#B5473F"), Theme.B("#6A4BB8"), Theme.B("#3F7A47"), Theme.B("#2F6BC4"), Theme.B("#C07F22"), Theme.B("#3C4452"), Theme.B("#1F7A7A") };

        public AppsPage()
        {
            Background = Theme.Bg;
            var content = new StackPanel { Margin = new Thickness(40, 30, 40, 30) };
            var head = new Grid();
            head.Children.Add(Theme.Stack(4,
                Theme.T("Your apps", 26, Theme.Text, FontWeights.Bold),
                Theme.T("Everything you've installed in Android TV. Built-in apps live on the TV home screen.", 14, Theme.Muted, wrap: true)));
            var buttons = Theme.Row(8,
                Theme.Btn("Refresh", "Secondary", async (o, e) => await Load(), Theme.GRefresh),
                Theme.Btn("Import APK", "Primary", (o, e) => MainWindow.Instance.Show(PageId.Import), Theme.GImport));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.VerticalAlignment = VerticalAlignment.Top;
            head.Children.Add(buttons);
            content.Children.Add(head);
            _note.Margin = new Thickness(0, 20, 0, 0);
            content.Children.Add(_note);
            _grid.Margin = new Thickness(0, 16, 0, 0);
            content.Children.Add(_grid);
            Children.Add(Theme.Scroll(content));

            Engine.StateChanged += () => Dispatcher.BeginInvoke(new Action(() => { if (MainWindow.Instance.Current == PageId.Apps) _ = Load(); }));
            InstallQueue.Changed += i => { if (i.Status == InstallStatus.Installed && MainWindow.Instance.Current == PageId.Apps) _ = Load(); };
        }

        public void OnShown() => _ = Load();

        private async Task Load()
        {
            if (_loading) return;
            if (!Engine.IsRunning)
            {
                _grid.Children.Clear();
                _note.Text = "Start Android TV to see your apps.";
                return;
            }
            _loading = true;
            _note.Text = "Loading…";
            try
            {
                var apps = await Adb.Apps();
                _grid.Children.Clear();
                _note.Text = apps.Count == 0 ? "No apps added yet. Use Import APK or the Downloader to add some." : apps.Count + (apps.Count == 1 ? " app" : " apps");
                foreach (var a in apps.OrderBy(a => a.Name)) _grid.Children.Add(Tile(a));
            }
            catch (Exception ex)
            {
                _note.Text = "Couldn't list apps: " + ex.Message;
            }
            finally { _loading = false; }
        }

        private UIElement Tile(Adb.AppInfo app)
        {
            var colour = Swatches[Math.Abs(app.Package.GetHashCode()) % Swatches.Length];
            var initial = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = colour,
                Child = new TextBlock { Text = app.Name.Substring(0, 1).ToUpperInvariant(), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var info = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            info.Children.Add(Theme.T(app.Name, 16, Theme.Text, FontWeights.SemiBold));
            info.Children.Add(Theme.T(app.Package, 12, Theme.Muted));
            info.Children.Add(Theme.T((string.IsNullOrEmpty(app.Version) ? "" : "Version " + app.Version) + (app.Updated > DateTime.MinValue ? " · updated " + app.Updated.ToString("d MMM") : ""), 12, Theme.Muted));

            var open = Theme.Btn("Open", "Primary", async (o, e) =>
            {
                MainWindow.Instance.Show(PageId.Tv);
                if (!await Adb.Launch(app.Package)) MainWindow.Instance.Toast("That app has no screen to open on a TV.");
            }, Theme.GPlay);
            var more = Theme.Btn("", "Secondary", null, Theme.GMore);
            more.ToolTip = "More";
            System.Windows.Automation.AutomationProperties.SetName(more, "More options for " + app.Name);
            var menu = new ContextMenu();
            menu.Items.Add(Item("Force stop", async () => { await Adb.ForceStop(app.Package); MainWindow.Instance.Toast(app.Name + " stopped"); }));
            menu.Items.Add(Item("Clear data", async () =>
            {
                if (Confirm("Clear all data for " + app.Name + "? It will start fresh, as if newly installed."))
                {
                    await Adb.ClearData(app.Package);
                    MainWindow.Instance.Toast(app.Name + " data cleared");
                }
            }));
            menu.Items.Add(Item("Uninstall", async () =>
            {
                if (Confirm("Uninstall " + app.Name + "?"))
                {
                    await Adb.Uninstall(app.Package);
                    await Load();
                }
            }));
            more.Click += (o, e) => { menu.PlacementTarget = more; menu.IsOpen = true; };

            var actions = Theme.Row(8, open, more);
            actions.Margin = new Thickness(0, 14, 0, 0);

            var body = new StackPanel();
            body.Children.Add(initial);
            body.Children.Add(info);
            body.Children.Add(actions);
            return new Border
            {
                Width = 270,
                Margin = new Thickness(0, 0, 16, 16),
                Background = Theme.Surface,
                BorderBrush = Theme.Edge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Child = body
            };
        }

        private static MenuItem Item(string text, Func<Task> run)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += async (o, e) =>
            {
                try { await run(); }
                catch (Exception ex) { MainWindow.Instance.Toast(ex.Message); }
            };
            return mi;
        }

        private static bool Confirm(string text) =>
            MessageBox.Show(MainWindow.Instance, text, "TeleTV", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }
}
