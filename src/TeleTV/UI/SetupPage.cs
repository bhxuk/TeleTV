using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>First launch: downloads Android TV from Google, checks virtualisation and starts it.</summary>
    public class SetupPage : Grid, IPage
    {
        private readonly StackPanel _body = new StackPanel { Width = 600, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 36) };
        private readonly TextBlock _subtitle = Theme.T("First launch only. After this TeleTV opens in seconds.", 15, Theme.Muted, wrap: true);
        private readonly StackPanel _area = new StackPanel();
        private SdkPlan _plan;
        private CancellationTokenSource _cts;
        private bool _running;

        private enum StepState { Waiting, Active, Done, Failed }
        private class Step
        {
            public string Title;
            public TextBlock Label;
            public TextBlock Note;
            public Border Mark;
            public Border Row;
        }
        private readonly List<Step> _steps = new List<Step>();
        private readonly ProgressBar _bar = new ProgressBar { Minimum = 0, Maximum = 1 };
        private readonly TextBlock _barText = Theme.T("", 13, Theme.Muted);
        private readonly StackPanel _notice = new StackPanel();

        public SetupPage()
        {
            Background = Theme.Bg;
            var badge = new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(24),
                Background = Theme.AccentBg,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = Theme.GTv, FontFamily = Theme.Icons, FontSize = 46, Foreground = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            _body.Children.Add(badge);
            var title = Theme.T("Getting TeleTV ready", 30, Theme.Text, FontWeights.Bold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 24, 0, 8);
            _body.Children.Add(title);
            _subtitle.TextAlignment = TextAlignment.Center;
            _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            _body.Children.Add(_subtitle);
            _area.Margin = new Thickness(0, 28, 0, 0);
            _body.Children.Add(_area);
            Children.Add(Theme.Scroll(_body));
        }

        public void OnShown()
        {
            if (_running || _plan != null) return;
            _ = LoadPlan();
        }

        // ---------------- Step 1: what to download ----------------

        private async Task LoadPlan()
        {
            _area.Children.Clear();
            var row = Theme.Row(12, Theme.Spinner(20), Theme.T("Getting the download list from Google", 15, Theme.Sub));
            row.HorizontalAlignment = HorizontalAlignment.Center;
            _area.Children.Add(row);
            try
            {
                _plan = await Sdk.Plan(Settings.Current.ImageTag, CancellationToken.None);
                ShowLicence();
            }
            catch (Exception ex)
            {
                _plan = null;
                Log.Write("Plan failed: " + ex.Message);
                ShowError(ex.Message, () => _ = LoadPlan());
            }
        }

        private string SystemName(SdkPackage image) =>
            (image.Tag == "google-tv" ? "Google TV" : "Android TV") + " (Android " + MainWindow.AndroidVersion(image.Api) + ")";

        // ---------------- Step 2: licence ----------------

        private void ShowLicence()
        {
            _area.Children.Clear();
            var img = _plan.Image;
            long need = _plan.TotalSize * 3 + 8L * 1024 * 1024 * 1024; // zips + unpacked + room for apps
            long free = Sdk.FreeSpace();

            var content = new StackPanel();
            content.Children.Add(Theme.T("TeleTV downloads Android from Google", 18, Theme.Text, FontWeights.Bold));
            var what = Theme.T(SystemName(img) + ", the Android emulator and adb tools. " + Format.Size(_plan.TotalSize) + " to download, straight from Google's servers.", 14, Theme.Sub, wrap: true);
            what.Margin = new Thickness(0, 6, 0, 0);
            content.Children.Add(what);
            if (Settings.Current.ImageTag == "google-tv" && img.Tag != "google-tv")
                content.Children.Add(Note("Google doesn't publish Google TV for this yet, so TeleTV will use Android TV. Apps come in through Import APK and the Downloader.", Theme.Orange));
            if (img.Tag == "google-tv")
                content.Children.Add(Note("Google TV includes the Play Store. You'll sign in with a Google account inside Android the first time.", Theme.AccentSoft));
            if (free < need)
                content.Children.Add(Note("Only " + Format.Size(free) + " free on this drive. TeleTV needs about " + Format.Size(need) + ".", Theme.Red));

            var label = Theme.T("Google's licence for these downloads", 13, Theme.Muted);
            label.Margin = new Thickness(0, 18, 0, 6);
            content.Children.Add(label);
            var text = new TextBox
            {
                Text = _plan.LicenceText.Length > 0 ? _plan.LicenceText : "Android Software Development Kit License Agreement (see developer.android.com/studio/terms).",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 190,
                FontSize = 12,
                Padding = new Thickness(10, 8, 10, 8),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            content.Children.Add(text);

            var accept = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            accept.Style = (Style)Application.Current.FindResource("Switch");
            var go = Theme.Btn("Download and set up", "Primary", (o, e) => _ = RunSetup(), Theme.GDownload);
            go.IsEnabled = false;
            accept.Checked += (o, e) => go.IsEnabled = true;
            accept.Unchecked += (o, e) => go.IsEnabled = false;
            if (Settings.Current.LicenceAccepted) accept.IsChecked = true;

            var row = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var acc = Theme.Row(10, accept, Theme.T("I accept Google's licence", 14, Theme.Sub));
            row.Children.Add(acc);
            Grid.SetColumn(go, 1);
            row.Children.Add(go);
            content.Children.Add(row);

            _area.Children.Add(Theme.Card(content));
        }

        private static TextBlock Note(string text, Brush brush)
        {
            var t = Theme.T(text, 13, brush, wrap: true);
            t.Margin = new Thickness(0, 10, 0, 0);
            return t;
        }

        // ---------------- Step 3: download, install, check, start ----------------

        private async Task RunSetup()
        {
            if (_running) return;
            _running = true;
            Settings.Current.LicenceAccepted = true;
            Settings.Save();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            BuildChecklist();
            Sdk.ClearLeftovers();

            Task<Hypervisor.State> accel = null;
            try
            {
                var packages = new[] { _plan.Emulator, _plan.PlatformTools, _plan.Image };
                long total = packages.Where(p => !Sdk.IsInstalled(p)).Sum(p => Math.Max(0, p.Size));
                long before = 0;
                var started = DateTime.UtcNow;

                for (int i = 0; i < packages.Length; i++)
                {
                    var p = packages[i];
                    Mark(i, StepState.Active);
                    if (!Sdk.IsInstalled(p))
                    {
                        long offset = before;
                        var prog = new Progress<Sdk.Progress>(pr =>
                        {
                            if (pr.Step.StartsWith("Downloading") && pr.Total > 0)
                            {
                                long done = offset + pr.Done;
                                _bar.Value = total > 0 ? Math.Min(1, done / (double)total) : pr.Fraction;
                                double secs = Math.Max(1, (DateTime.UtcNow - started).TotalSeconds);
                                double rate = done / secs;
                                string left = rate > 1024 && total > done ? " · about " + Minutes((total - done) / rate) + " left" : "";
                                _barText.Text = Format.Size(done) + " of " + Format.Size(total) + left;
                                _steps[i].Note.Text = Format.Size(pr.Done) + " of " + Format.Size(pr.Total);
                            }
                            else
                            {
                                _steps[i].Note.Text = pr.Step.StartsWith("Unpacking") ? "Unpacking " + (int)(pr.Fraction * 100) + "%" : "Checking";
                            }
                        });
                        string zip = await Sdk.Download(p, prog, ct);
                        before += Math.Max(0, p.Size);
                        await Sdk.Install(p, zip, prog, ct);
                    }
                    _steps[i].Note.Text = p == _plan.Image ? Format.Size(Math.Max(0, p.Size)) : "Version " + p.Version;
                    Mark(i, StepState.Done);

                    // As soon as the emulator is in place, check virtualisation while the rest downloads.
                    if (p == _plan.Emulator)
                    {
                        Mark(3, StepState.Active);
                        accel = Hypervisor.Check();
                    }
                }
                _bar.Value = 1;
                _barText.Text = "Downloads complete";

                var state = accel != null ? await accel : await Hypervisor.Check();
                if (state != Hypervisor.State.Ready && state != Hypervisor.State.Unknown)
                {
                    Mark(3, StepState.Failed);
                    _steps[3].Note.Text = "Needs attention";
                    Settings.Current.SetupComplete = true; // everything is downloaded; only virtualisation is left
                    Settings.Save();
                    Engine.MarkInstalled(true);
                    ShowVirtualisationHelp(state);
                    _running = false;
                    return;
                }
                Mark(3, StepState.Done);
                _steps[3].Note.Text = "Ready";

                Settings.Current.SetupComplete = true;
                Settings.Save();
                Engine.MarkInstalled(true);

                Mark(4, StepState.Active);
                _steps[4].Note.Text = "Booting";
                _barText.Text = "Starting Android TV. The first start takes a minute or two.";
                await Engine.Start();
                Mark(4, StepState.Done);
                _running = false;
                MainWindow.Instance.Forget(PageId.Setup);
                MainWindow.Instance.Show(PageId.Tv);
            }
            catch (OperationCanceledException)
            {
                _running = false;
                ShowLicence();
            }
            catch (Exception ex)
            {
                _running = false;
                Log.Write("Setup failed: " + ex);
                for (int i = 0; i < _steps.Count; i++)
                    if (_steps[i].Mark.Tag is StepState s && s == StepState.Active) Mark(i, StepState.Failed);
                ShowNotice(Theme.GError, "Setup stopped", ex.Message + (Settings.Current.SetupComplete ? "" : " Downloads resume from where they stopped."),
                    Theme.Btn("Try again", "Primary", (o, e) =>
                    {
                        if (Settings.Current.SetupComplete) { MainWindow.Instance.Forget(PageId.Setup); MainWindow.Instance.Show(PageId.Tv); _ = TvPage.StartSafe(); }
                        else _ = RunSetup();
                    }));
            }
        }

        private static string Minutes(double seconds)
        {
            if (seconds < 90) return "1 minute";
            return Math.Ceiling(seconds / 60) + " minutes";
        }

        private void BuildChecklist()
        {
            _area.Children.Clear();
            _steps.Clear();
            var list = new StackPanel();
            foreach (var t in new[] { "Download the Android emulator", "Download adb tools", "Download " + SystemName(_plan.Image), "Check virtualisation", "Start Android TV" })
            {
                var step = new Step { Title = t };
                step.Mark = new Border { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
                step.Label = Theme.T(t, 15, Theme.Muted);
                step.Note = Theme.T("", 13, Theme.Muted);
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(step.Mark);
                step.Label.Margin = new Thickness(14, 0, 12, 0);
                step.Label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(step.Label, 1);
                g.Children.Add(step.Label);
                step.Note.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(step.Note, 2);
                g.Children.Add(step.Note);
                step.Row = new Border { Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10), Child = g };
                list.Children.Add(step.Row);
                _steps.Add(step);
            }
            for (int i = 0; i < _steps.Count; i++) Mark(i, StepState.Waiting);
            _area.Children.Add(Theme.Card(list, new Thickness(10)));

            _bar.Margin = new Thickness(0, 22, 0, 8);
            _bar.Value = 0;
            _area.Children.Add(_bar);
            _barText.HorizontalAlignment = HorizontalAlignment.Center;
            _area.Children.Add(_barText);
            _notice.Margin = new Thickness(0, 20, 0, 0);
            _notice.Children.Clear();
            _area.Children.Add(_notice);

            var cancel = Theme.Btn("Cancel", "Ghost", (o, e) => _cts?.Cancel());
            cancel.HorizontalAlignment = HorizontalAlignment.Center;
            cancel.Margin = new Thickness(0, 12, 0, 0);
            _area.Children.Add(cancel);
        }

        private void Mark(int i, StepState s)
        {
            var step = _steps[i];
            step.Mark.Tag = s;
            step.Row.Background = s == StepState.Active ? Theme.B("#1E2633") : Brushes.Transparent;
            step.Label.Foreground = s == StepState.Waiting ? Theme.Muted : Theme.Text;
            step.Label.FontWeight = s == StepState.Active ? FontWeights.SemiBold : FontWeights.Normal;
            step.Mark.Child = s switch
            {
                StepState.Active => Theme.Spinner(20),
                StepState.Done => Theme.Icon(Theme.GTick, 16, Theme.Green),
                StepState.Failed => Theme.Icon(Theme.GError, 16, Theme.Red),
                _ => new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, Stroke = Theme.BorderStrong, StrokeThickness = 2 }
            };
            if (step.Mark.Child is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        }

        private void ShowVirtualisationHelp(Hypervisor.State state)
        {
            if (state == Hypervisor.State.NeedsBios)
            {
                ShowNotice(Theme.GInfo, "Virtualisation is switched off in this PC's BIOS",
                    "Android needs the processor's virtualisation feature (Intel VT-x or AMD-V/SVM). Restart into the BIOS or UEFI settings, switch it on, save, then open TeleTV again. Everything is already downloaded.\n\n" + Hypervisor.Detail,
                    Theme.Btn("Check again", "Secondary", async (o, e) => await Recheck()));
                return;
            }
            ShowNotice(Theme.GInfo, "One more thing: switch on Windows Hypervisor Platform",
                "This Windows feature lets Android run at full speed. TeleTV can switch it on for you. Windows asks for permission, then needs a restart. Everything is already downloaded, so TeleTV starts straight away afterwards.",
                Theme.Row(10,
                    Theme.Btn("Switch on and restart", "Primary", async (o, e) =>
                    {
                        ((Button)o).IsEnabled = false;
                        if (await Hypervisor.EnableWindowsFeature())
                        {
                            var r = MessageBox.Show(MainWindow.Instance, "Done. Restart Windows now to finish?", "TeleTV", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (r == MessageBoxResult.Yes) Hypervisor.RestartWindows();
                        }
                        else MessageBox.Show(MainWindow.Instance, "Windows didn't switch the feature on. You can do it by hand: Start > Turn Windows features on or off > Windows Hypervisor Platform.", "TeleTV", MessageBoxButton.OK, MessageBoxImage.Information);
                        ((Button)o).IsEnabled = true;
                    }),
                    Theme.Btn("Check again", "Secondary", async (o, e) => await Recheck())));
        }

        private async Task Recheck()
        {
            var s = await Hypervisor.Check();
            if (s == Hypervisor.State.Ready)
            {
                MainWindow.Instance.Forget(PageId.Setup);
                MainWindow.Instance.Show(PageId.Tv);
                _ = TvPage.StartSafe();
            }
            else ShowVirtualisationHelp(s);
        }

        private void ShowNotice(string glyph, string title, string body, UIElement action)
        {
            _notice.Children.Clear();
            var inner = new StackPanel();
            inner.Children.Add(Theme.Row(10, Theme.Icon(glyph, 18, Theme.AccentSoft), Theme.T(title, 16, Theme.Text, FontWeights.SemiBold)));
            var b = Theme.T(body, 14, Theme.Sub, wrap: true);
            b.Margin = new Thickness(0, 8, 0, 0);
            inner.Children.Add(b);
            if (action is FrameworkElement fe) { fe.Margin = new Thickness(0, 16, 0, 0); fe.HorizontalAlignment = HorizontalAlignment.Left; inner.Children.Add(fe); }
            _notice.Children.Add(Theme.Card(inner, null, Theme.B("#2E3A4F")));
        }

        private void ShowError(string message, Action retry)
        {
            _area.Children.Clear();
            var inner = new StackPanel();
            inner.Children.Add(Theme.Row(10, Theme.Icon(Theme.GError, 18, Theme.Red), Theme.T("Couldn't get started", 16, Theme.Text, FontWeights.SemiBold)));
            var b = Theme.T(message, 14, Theme.Sub, wrap: true);
            b.Margin = new Thickness(0, 8, 0, 16);
            inner.Children.Add(b);
            var again = Theme.Btn("Try again", "Primary", (o, e) => retry());
            again.HorizontalAlignment = HorizontalAlignment.Left;
            inner.Children.Add(again);
            _area.Children.Add(Theme.Card(inner));
        }
    }
}
