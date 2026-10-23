using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>Settings: General, Android TV, Updates and About.</summary>
    public class SettingsPage : Grid, IPage
    {
        private enum Section { General, Android, Updates, About }

        private readonly Dictionary<Section, Button> _nav = new Dictionary<Section, Button>();
        private readonly ContentControl _host = new ContentControl();
        private Section _section = Section.General;
        private bool _needsRestart;
        private CancellationTokenSource _sdkCts;

        public SettingsPage()
        {
            Background = Theme.Bg;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new StackPanel { Margin = new Thickness(16, 28, 16, 16) };
            var head = Theme.T("SETTINGS", 12, Theme.Muted, FontWeights.SemiBold);
            head.Margin = new Thickness(12, 0, 0, 12);
            rail.Children.Add(head);
            foreach (var (s, label) in new[] { (Section.General, "General"), (Section.Android, "Android TV"), (Section.Updates, "Updates"), (Section.About, "About") })
            {
                var b = Theme.Btn(label, "Ghost", (o, e) => Show(s));
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.FontSize = 14;
                b.Padding = new Thickness(12, 10, 12, 10);
                b.Margin = new Thickness(0, 0, 0, 4);
                _nav[s] = b;
                rail.Children.Add(b);
            }
            Children.Add(new Border { Background = Theme.Rail, BorderBrush = Theme.Border, BorderThickness = new Thickness(0, 0, 1, 0), Child = rail });
            Grid.SetColumn(_host, 1);
            Children.Add(_host);
        }

        public void OnShown() => Show(_section);

        public void ShowUpdates() => Show(Section.Updates);

        private void Show(Section s)
        {
            _section = s;
            foreach (var kv in _nav) kv.Value.Style = (Style)FindResource(kv.Key == s ? "NavActive" : "Ghost");
            var page = new StackPanel { Margin = new Thickness(40, 30, 40, 40), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
            switch (s)
            {
                case Section.General: BuildGeneral(page); break;
                case Section.Android: BuildAndroid(page); break;
                case Section.Updates: BuildUpdates(page); break;
                case Section.About: BuildAbout(page); break;
            }
            _host.Content = Theme.Scroll(page);
        }

        private static TextBlock H1(string t)
        {
            var x = Theme.T(t, 26, Theme.Text, FontWeights.Bold);
            x.Margin = new Thickness(0, 0, 0, 20);
            return x;
        }

        private static Border Group(params UIElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Theme.Border, Margin = new Thickness(-24, 18, -24, 18) });
                sp.Children.Add(rows[i]);
            }
            var c = Theme.Card(sp);
            c.Margin = new Thickness(0, 0, 0, 18);
            return c;
        }

        // ---------------- General ----------------

        private void BuildGeneral(StackPanel p)
        {
            var s = Settings.Current;
            p.Children.Add(H1("General"));
            p.Children.Add(Group(
                Theme.SettingRow("Open in full screen", "TeleTV fills the screen when it opens. Press F11 to switch at any time.",
                    Theme.Switch(s.StartFullScreen, v => { s.StartFullScreen = v; Settings.Save(); })),
                Theme.SettingRow("Open apps after installing", "Imported and downloaded apps open as soon as they're installed.",
                    Theme.Switch(s.OpenAfterInstall, v => { s.OpenAfterInstall = v; Settings.Save(); })),
                Theme.SettingRow("Quick start", "Android saves where you were when TeleTV closes and opens in seconds next time. Turn off for a full start every time.",
                    Theme.Switch(s.FastStart, v => { s.FastStart = v; Settings.Save(); }))));

            var keys = new StackPanel();
            keys.Children.Add(Theme.T("Keyboard, mouse and remote", 16, Theme.Text, FontWeights.SemiBold));
            foreach (var (k, d) in new[]
            {
                ("Arrow keys", "Move around, like the D-pad on a remote"),
                ("Enter", "Select"),
                ("Esc or mouse back button", "Back"),
                ("Home or middle click", "Android TV home screen"),
                ("F5", "Recent apps"),
                ("F11 or Alt+Enter", "Full screen on and off"),
                ("Ctrl+V", "Paste text from Windows into Android"),
                ("Ctrl+O", "Import an APK"),
                ("Ctrl+1 to 4, Ctrl+,", "Home, Import APK, Downloader, Apps, Settings"),
                ("Media keys", "Play, pause, next and previous work as on a remote")
            })
            {
                var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(Theme.T(k, 14, Theme.Text, FontWeights.Medium));
                var dd = Theme.T(d, 14, Theme.Sub, wrap: true);
                Grid.SetColumn(dd, 1);
                g.Children.Add(dd);
                keys.Children.Add(g);
            }
            var hint = Theme.T("Bluetooth TV remotes and air mice that act as a keyboard work the same way.", 13, Theme.Muted, wrap: true);
            hint.Margin = new Thickness(0, 14, 0, 0);
            keys.Children.Add(hint);
            p.Children.Add(Group(keys));
        }

        // ---------------- Android TV ----------------

        private void BuildAndroid(StackPanel p)
        {
            var s = Settings.Current;
            p.Children.Add(H1("Android TV"));

            if (_needsRestart && Engine.IsRunning)
            {
                var banner = Theme.Card(Theme.Row(16,
                    Theme.T("Restart Android to use the new settings.", 14, Theme.Text),
                    Theme.Btn("Restart now", "Primary", async (o, e) => { _needsRestart = false; MainWindow.Instance.Show(PageId.Tv); try { await Engine.Restart(); } catch { } })),
                    new Thickness(18, 12, 18, 12), Theme.Accent);
                banner.Margin = new Thickness(0, 0, 0, 18);
                p.Children.Add(banner);
            }

            string system = s.InstalledImageApi > 0
                ? (s.InstalledImageTag == "google-tv" ? "Google TV" : "Android TV") + " · Android " + MainWindow.AndroidVersion(s.InstalledImageApi) + " · revision " + s.InstalledImageRevision
                : "Not installed yet";
            var power = Engine.IsRunning
                ? Theme.Btn("Restart Android", "Secondary", async (o, e) => { MainWindow.Instance.Show(PageId.Tv); try { await Engine.Restart(); } catch { } }, Theme.GRefresh)
                : Theme.Btn("Start Android", "Primary", (o, e) => { MainWindow.Instance.Show(PageId.Tv); _ = TvPage.StartSafe(); }, Theme.GPlay);
            if (!s.SetupComplete) power.IsEnabled = false;

            void Changed() { _needsRestart = true; Settings.Save(); MainWindow.Instance.RefreshFooter(); }

            int autoRam = Android.AutoRamMb();
            var ramOptions = new List<(string, int)> { ("Auto (" + autoRam / 1024 + " GB)", 0), ("2 GB", 2048), ("3 GB", 3072), ("4 GB", 4096), ("6 GB", 6144), ("8 GB", 8192) };
            var coreOptions = new List<(string, int)> { ("Auto (" + Android.AutoCores() + ")", 0) };
            foreach (var c in new[] { 2, 4, 6, 8 }) if (c <= Environment.ProcessorCount) coreOptions.Add((c.ToString(), c));

            p.Children.Add(Group(
                Theme.SettingRow("System", system, power),
                Stacked("Memory", "How much of the PC's memory Android can use. More helps heavy apps; leave enough for Windows.",
                    Theme.Segmented(ramOptions, s.RamMb, v => { s.RamMb = v; Changed(); Show(Section.Android); })),
                Stacked("Processor cores", "Cores Android can use at once.",
                    Theme.Segmented(coreOptions, s.Cores, v => { s.Cores = v; Changed(); Show(Section.Android); })),
                Stacked("Picture", "Android's screen resolution. 720p is lighter on older PCs.",
                    Theme.Segmented(new[] { ("1080p", 1080), ("720p", 720) }, s.Resolution, v => { s.Resolution = v; Android.DropQuickStart(); Changed(); Show(Section.Android); })),
                Stacked("Graphics", "Auto uses the graphics card when it can. Choose Software if the picture is black or Android won't start.",
                    Theme.Segmented(new[] { ("Auto", "auto"), ("Graphics card", "host"), ("Software", "software") }, s.Graphics, v => { s.Graphics = v; Android.DropQuickStart(); Changed(); Show(Section.Android); }))));

            // Switch between Google TV and Android TV
            var other = s.InstalledImageTag == "google-tv" ? "android-tv" : "google-tv";
            p.Children.Add(Group(
                Theme.SettingRow("Virtualisation", "If Android won't start, TeleTV can check and switch on Windows Hypervisor Platform.",
                    Theme.Btn("Fix virtualisation", "Secondary", async (o, e) => await FixVirtualisation((Button)o))),
                Theme.SettingRow(other == "android-tv" ? "Switch to Android TV" : "Switch to Google TV",
                    other == "android-tv" ? "Plain Android TV without the Play Store. Lighter and quicker. Your apps and data inside Android are erased." : "Google TV with the Play Store, where Google publishes it. Your apps and data inside Android are erased.",
                    Theme.Btn("Switch", "Secondary", async (o, e) => await SwitchSystem(other))),
                Theme.SettingRow("Reset Android", "Erases every app and setting inside Android, like a factory reset. TeleTV keeps the downloaded system.",
                    Theme.Btn("Reset", "Danger", async (o, e) => await Reset()))));
        }

        private static UIElement Stacked(string title, string hint, UIElement control)
        {
            var sp = new StackPanel();
            sp.Children.Add(Theme.T(title, 15, Theme.Text, FontWeights.Medium));
            var h = Theme.T(hint, 13, Theme.Muted, wrap: true);
            h.Margin = new Thickness(0, 2, 0, 12);
            sp.Children.Add(h);
            sp.Children.Add(control);
            return sp;
        }

        private async Task FixVirtualisation(Button b)
        {
            b.IsEnabled = false;
            try
            {
                var state = await Hypervisor.Check();
                if (state == Hypervisor.State.Ready)
                {
                    MessageBox.Show(MainWindow.Instance, "Virtualisation is working. If Android still won't start, try Graphics: Software.", "TeleTV");
                    return;
                }
                if (state == Hypervisor.State.NeedsBios)
                {
                    MessageBox.Show(MainWindow.Instance, "Virtualisation is switched off in this PC's BIOS or UEFI settings. Switch on Intel VT-x or AMD-V (SVM) there, save and restart.\n\n" + Hypervisor.Detail, "TeleTV");
                    return;
                }
                if (MessageBox.Show(MainWindow.Instance, "TeleTV will switch on Windows Hypervisor Platform. Windows asks for permission, then needs a restart. Continue?", "TeleTV", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                if (await Hypervisor.EnableWindowsFeature())
                {
                    if (MessageBox.Show(MainWindow.Instance, "Done. Restart Windows now to finish?", "TeleTV", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        await Engine.Stop();
                        Hypervisor.RestartWindows();
                    }
                }
                else MessageBox.Show(MainWindow.Instance, "Windows didn't switch it on. Do it by hand: Start > Turn Windows features on or off > Windows Hypervisor Platform.", "TeleTV");
            }
            finally { b.IsEnabled = true; }
        }

        private async Task Reset()
        {
            if (MessageBox.Show(MainWindow.Instance, "Erase every app and setting inside Android TV? This can't be undone.", "Reset Android", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            MainWindow.Instance.Show(PageId.Tv);
            await Engine.Stop();
            Android.FactoryReset();
            await TvPage.StartSafe(true);
        }

        private async Task SwitchSystem(string tag)
        {
            string name = tag == "google-tv" ? "Google TV" : "Android TV";
            if (MessageBox.Show(MainWindow.Instance, "Download " + name + " and switch to it? Apps and data inside Android are erased. The download is about 1 to 2 GB.", "TeleTV", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Settings.Current.ImageTag = tag;
            Settings.Save();
            Show(Section.Updates);
            await CheckAndDownloadSdk(true);
        }

        // ---------------- Updates ----------------

        private StackPanel _appCard, _sdkCard;

        private void BuildUpdates(StackPanel p)
        {
            var s = Settings.Current;
            p.Children.Add(H1("Updates"));
            _appCard = new StackPanel();
            _sdkCard = new StackPanel();
            var a = Theme.Card(_appCard, null, Updates.AppUpdateAvailable ? Theme.B("#2E3A4F") : null);
            a.Margin = new Thickness(0, 0, 0, 18);
            p.Children.Add(a);
            var b = Theme.Card(_sdkCard);
            b.Margin = new Thickness(0, 0, 0, 18);
            p.Children.Add(b);
            PaintApp(null);
            PaintSdk(null);

            p.Children.Add(Group(
                Theme.SettingRow("Check for updates automatically", "Checks each time TeleTV opens.",
                    Theme.Switch(s.AutoCheckUpdates, v => { s.AutoCheckUpdates = v; Settings.Save(); })),
                Theme.SettingRow("Download Android updates in the background", "You still choose when they're applied.",
                    Theme.Switch(s.BackgroundDownload, v => { s.BackgroundDownload = v; Settings.Save(); })),
                Theme.SettingRow("Update channel", "Beta gets new TeleTV features first.",
                    Theme.Segmented(new[] { ("Stable", false), ("Beta", true) }, s.BetaChannel, v => { s.BetaChannel = v; Settings.Save(); _ = CheckApp(); }))));

            var last = Theme.T(s.LastUpdateCheck > DateTime.MinValue ? "Last checked " + s.LastUpdateCheck.ToString("d MMM HH:mm") + " · releases from github.com/" + Updates.Repo : "Releases from github.com/" + Updates.Repo, 13, Theme.Muted);
            p.Children.Add(last);
        }

        private void PaintApp(string status)
        {
            if (_appCard == null) return;
            _appCard.Children.Clear();
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            if (Updates.AppUpdateAvailable)
            {
                left.Children.Add(Theme.T("UPDATE AVAILABLE", 12, Theme.AccentSoft, FontWeights.SemiBold));
                var t = Theme.T("TeleTV " + Updates.LatestVersion, 19, Theme.Text, FontWeights.Bold);
                t.Margin = new Thickness(0, 4, 0, 6);
                left.Children.Add(t);
                foreach (var line in Updates.NoteLines()) left.Children.Add(Theme.T("•  " + line, 14, Theme.Sub, wrap: true));
                var sz = Theme.T((Updates.LatestSetupSize > 0 ? Format.Size(Updates.LatestSetupSize) + " · " : "") + "TeleTV restarts, your apps and settings stay put", 13, Theme.Muted);
                sz.Margin = new Thickness(0, 8, 0, 0);
                left.Children.Add(sz);
            }
            else
            {
                left.Children.Add(Theme.T("TeleTV " + Updates.CurrentText, 17, Theme.Text, FontWeights.SemiBold));
                left.Children.Add(Theme.T(status ?? (Updates.LatestVersion != null ? "You're up to date." : "Check to see if there's a newer version."), 14, Theme.Muted));
            }
            g.Children.Add(left);
            Button btn = Updates.AppUpdateAvailable
                ? Theme.Btn("Update now", "Primary", async (o, e) => await InstallApp((Button)o))
                : Theme.Btn("Check now", "Secondary", async (o, e) => await CheckApp());
            btn.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(btn, 1);
            g.Children.Add(btn);
            _appCard.Children.Add(g);
        }

        private async Task CheckApp()
        {
            PaintApp("Checking…");
            await Updates.CheckApp();
            PaintApp(Updates.LatestVersion == null ? "Couldn't reach GitHub. Check the internet connection." : null);
        }

        private async Task InstallApp(Button b)
        {
            b.IsEnabled = false;
            var progress = new Progress<double>(f => b.Content = "Downloading " + (int)(f * 100) + "%");
            string err = await Updates.InstallApp(progress);
            if (err == null) Application.Current.Shutdown();
            else { b.IsEnabled = true; b.Content = "Update now"; PaintApp(err); }
        }

        private void PaintSdk(string status, double? fraction = null)
        {
            if (_sdkCard == null) return;
            _sdkCard.Children.Clear();
            var s = Settings.Current;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Theme.T("Android TV system", 17, Theme.Text, FontWeights.SemiBold));
            string text;
            Button btn;
            if (Updates.SdkPending)
            {
                text = status ?? "Updates downloaded: " + string.Join(", ", s.PendingSdk.Select(Describe)) + ". They're applied the next time Android starts.";
                btn = Theme.Btn("Restart Android now", "Primary", async (o, e) => { MainWindow.Instance.Show(PageId.Tv); try { await Engine.Restart(); } catch { } });
            }
            else if (Updates.SdkAvailable.Count > 0)
            {
                text = status ?? "Available: " + string.Join(", ", Updates.SdkAvailable.Select(Describe)) + " · " + Format.Size(Updates.SdkAvailable.Sum(x => Math.Max(0, x.Size)));
                btn = Theme.Btn("Download", "Primary", async (o, e) => await CheckAndDownloadSdk(false));
            }
            else
            {
                text = status ?? "Emulator " + (s.InstalledEmulatorVersion ?? "?") + " · adb " + (s.InstalledPlatformToolsVersion ?? "?") + (s.InstalledImageRevision > 0 ? " · system revision " + s.InstalledImageRevision : "");
                btn = Theme.Btn("Check now", "Secondary", async (o, e) => await CheckSdkOnly());
            }
            if (_sdkCts != null)
            {
                btn = Theme.Btn("Cancel", "Secondary", (o, e) => _sdkCts?.Cancel());
            }
            left.Children.Add(Theme.T(text, 14, Theme.Muted, wrap: true));
            if (fraction.HasValue)
            {
                var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = fraction.Value, Margin = new Thickness(0, 10, 20, 0) };
                left.Children.Add(bar);
            }
            g.Children.Add(left);
            btn.VerticalAlignment = VerticalAlignment.Top;
            btn.IsEnabled = s.SetupComplete;
            Grid.SetColumn(btn, 1);
            g.Children.Add(btn);
            _sdkCard.Children.Add(g);
        }

        private static string Describe(SdkPackage p) =>
            p.Path == "emulator" ? "emulator " + p.Version
            : p.Path == "platform-tools" ? "adb " + p.Version
            : (p.Tag == "google-tv" ? "Google TV" : "Android TV") + " (Android " + MainWindow.AndroidVersion(p.Api) + ", revision " + p.Revision + ")";

        private async Task CheckSdkOnly()
        {
            PaintSdk("Checking Google's servers…");
            try
            {
                await Updates.CheckSdk();
                PaintSdk(Updates.SdkAvailable.Count == 0 ? "Android is up to date." : null);
            }
            catch (Exception ex) { PaintSdk("Couldn't check: " + ex.Message); }
        }

        private async Task CheckAndDownloadSdk(bool check)
        {
            if (_sdkCts != null) return;
            _sdkCts = new CancellationTokenSource();
            try
            {
                if (check || Updates.SdkAvailable.Count == 0)
                {
                    PaintSdk("Checking Google's servers…");
                    await Updates.CheckSdk(_sdkCts.Token);
                }
                var list = Updates.SdkAvailable.ToList();
                if (list.Count == 0) { _sdkCts = null; PaintSdk("Android is up to date."); return; }
                long total = list.Sum(x => Math.Max(0, x.Size)), before = 0;
                foreach (var pkg in list)
                {
                    long offset = before;
                    var prog = new Progress<Sdk.Progress>(pr =>
                    {
                        if (pr.Step.StartsWith("Downloading") && total > 0)
                            PaintSdk("Downloading " + Describe(pkg) + " · " + Format.Size(offset + pr.Done) + " of " + Format.Size(total), (offset + pr.Done) / (double)total);
                    });
                    await Updates.DownloadSdk(new[] { pkg }, prog, _sdkCts.Token);
                    before += Math.Max(0, pkg.Size);
                }
                Updates.SdkAvailable.Clear();
                _sdkCts = null;
                PaintSdk(null);
            }
            catch (OperationCanceledException)
            {
                _sdkCts = null;
                PaintSdk("Download paused. It carries on from where it stopped next time.");
            }
            catch (Exception ex)
            {
                _sdkCts = null;
                PaintSdk("Couldn't download: " + ex.Message);
            }
        }

        // ---------------- About ----------------

        private void BuildAbout(StackPanel p)
        {
            p.Children.Add(H1("About"));
            var logo = new Image { Width = 64, Height = 64 };
            try { logo.Source = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/teletv.ico")); } catch { }
            var who = Theme.Stack(4,
                Theme.T("TeleTV " + Updates.CurrentText, 20, Theme.Text, FontWeights.Bold),
                Theme.T("Android TV in a Windows app.", 14, Theme.Sub));
            who.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(Group(Theme.Row(18, logo, who)));

            p.Children.Add(Group(
                Theme.SettingRow("Project page", "Source code, releases and the user guide.",
                    Theme.Btn("Open", "Secondary", (o, e) => OpenUrl("https://github.com/" + Updates.Repo))),
                Theme.SettingRow("User guide", null,
                    Theme.Btn("Open", "Secondary", (o, e) => OpenUrl("https://github.com/" + Updates.Repo + "/blob/main/docs/USER-GUIDE.md"))),
                Theme.SettingRow("Report a problem", "Attach the log from TeleTV's files folder if you can.",
                    Theme.Btn("Open", "Secondary", (o, e) => OpenUrl("https://github.com/" + Updates.Repo + "/issues"))),
                Theme.SettingRow("TeleTV's files", Paths.Root,
                    Theme.Btn("Show", "Secondary", (o, e) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.Root + "\"") { UseShellExecute = true })))));

            var legal = Theme.T(
                "Android, Android TV, Google TV and Google Play are trademarks of Google LLC. TeleTV downloads the Android emulator, platform-tools and system images directly from Google under the Android Software Development Kit License Agreement, which you accept during setup. TeleTV isn't made by or affiliated with Google. The emulator control interface definition is Apache 2.0, copyright The Android Open Source Project.",
                12, Theme.Muted, wrap: true);
            p.Children.Add(legal);
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
