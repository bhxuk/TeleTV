using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using TeleTV.Core;

namespace TeleTV.UI
{
    public enum PageId { Tv, Setup, Import, Downloader, Apps, Settings }

    /// <summary>The TeleTV window: a slim title bar with the main sections, the page in the middle and a status bar.</summary>
    public class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private readonly Grid _titleBar;
        private readonly Border _footer;
        private readonly ContentControl _host = new ContentControl { Focusable = false };
        private readonly Dictionary<PageId, Button> _nav = new Dictionary<PageId, Button>();
        private readonly Dictionary<PageId, FrameworkElement> _pages = new Dictionary<PageId, FrameworkElement>();
        private readonly Ellipse _dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _statusText = Theme.T("", 12, Theme.Sub);
        private readonly TextBlock _footerLeft = Theme.T("", 12, Theme.Muted);
        private readonly TextBlock _footerRight = Theme.T("", 12, Theme.Muted);
        private readonly Button _maxBtn;
        private readonly Button _fullBtn;
        private readonly Grid _overlay;
        private readonly TextBlock _overlayText = Theme.T("", 16, Theme.Text, FontWeights.SemiBold);
        private bool _fullScreen;
        private WindowState _stateBeforeFull;
        private bool _closingForReal;

        public PageId Current { get; private set; } = PageId.Tv;
        public TvPage Tv { get; }

        public MainWindow()
        {
            Instance = this;
            Title = "TeleTV";
            Background = Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Font;
            MinWidth = 820;
            MinHeight = 520;
            AllowDrop = true;
            UseLayoutRounding = true;
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/teletv.ico")); } catch { }

            var s = Settings.Current;
            Width = s.WinWidth > 0 ? s.WinWidth : 1280;
            Height = s.WinHeight > 0 ? s.WinHeight : 800;
            if (!double.IsNaN(s.WinLeft) && !double.IsNaN(s.WinTop) && OnScreen(s.WinLeft, s.WinTop))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WinLeft;
                Top = s.WinTop;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (s.WinMaximised) WindowState = WindowState.Maximized;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                UseAeroCaptionButtons = false
            });

            // ---- Title bar ----
            _titleBar = new Grid { Height = 40, Background = Theme.Chrome };
            _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            var logo = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/teletv.ico")); } catch { }
            left.Children.Add(logo);
            left.Children.Add(new TextBlock { Text = "TeleTV", FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Foreground = Theme.Text });
            foreach (var (id, label) in new[] { (PageId.Tv, "Home"), (PageId.Import, "Import APK"), (PageId.Downloader, "Downloader"), (PageId.Apps, "Apps"), (PageId.Settings, "Settings") })
            {
                var b = Theme.Btn(label, "Ghost", (o, e) => Show(id));
                b.Margin = new Thickness(0, 0, 4, 0);
                b.VerticalAlignment = VerticalAlignment.Center;
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                _nav[id] = b;
                left.Children.Add(b);
            }
            _titleBar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(_dot);
            status.Children.Add(_statusText);
            right.Children.Add(status);
            _fullBtn = Caption(Theme.GFull, "Full screen (F11)", (o, e) => ToggleFullScreen());
            right.Children.Add(_fullBtn);
            right.Children.Add(Caption(Theme.GMin, "Minimise", (o, e) => WindowState = WindowState.Minimized));
            _maxBtn = Caption(Theme.GMax, "Maximise", (o, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            right.Children.Add(_maxBtn);
            var close = Caption(Theme.GClose, "Close", (o, e) => Close());
            close.Style = (Style)FindResource("CaptionClose");
            right.Children.Add(close);
            Grid.SetColumn(right, 2);
            _titleBar.Children.Add(right);
            _titleBar.Children.Add(new Border { Height = 1, Background = Theme.Border, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            Grid.SetColumnSpan(_titleBar.Children[^1], 3);

            // ---- Footer ----
            var fg = new Grid { Margin = new Thickness(14, 0, 14, 0) };
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _footerLeft.VerticalAlignment = VerticalAlignment.Center;
            _footerRight.VerticalAlignment = VerticalAlignment.Center;
            fg.Children.Add(_footerLeft);
            Grid.SetColumn(_footerRight, 1);
            fg.Children.Add(_footerRight);
            _footer = new Border { Height = 28, Background = Theme.Chrome, BorderBrush = Theme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = fg };

            // ---- Closing overlay ----
            _overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0E, 0x11, 0x16)), Visibility = Visibility.Collapsed };
            var ov = Theme.Stack(16, Theme.Spinner(36), _overlayText);
            ov.HorizontalAlignment = HorizontalAlignment.Center;
            ov.VerticalAlignment = VerticalAlignment.Center;
            _overlayText.HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)ov.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            _overlay.Children.Add(ov);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(_titleBar);
            Grid.SetRow(_host, 1);
            root.Children.Add(_host);
            Grid.SetRow(_footer, 2);
            root.Children.Add(_footer);
            Grid.SetRowSpan(_overlay, 3);
            root.Children.Add(_overlay);
            Content = root;

            Tv = new TvPage();
            _pages[PageId.Tv] = Tv;

            Engine.StateChanged += () => Dispatcher.BeginInvoke(new Action(RefreshStatus));
            InstallQueue.Changed += i => RefreshFooter();
            StateChanged += (o, e) =>
            {
                _maxBtn.Content = WindowState == WindowState.Maximized ? Theme.GRestore : Theme.GMax;
                // Keep the window inside the screen when maximised without the standard frame.
                ((Grid)Content).Margin = WindowState == WindowState.Maximized && !_fullScreen ? new Thickness(7) : new Thickness(0);
            };
            PreviewKeyDown += OnPreviewKeyDown;
            Drop += OnDrop;
            DragOver += (o, e) =>
            {
                e.Effects = Files(e).Any(InstallQueue.IsApk) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Closing += OnClosing;
            Loaded += (o, e) =>
            {
                Show(Settings.Current.SetupComplete ? PageId.Tv : PageId.Setup);
                if (Settings.Current.StartFullScreen && Settings.Current.SetupComplete) ToggleFullScreen();
            };
            RefreshStatus();
        }

        private static bool OnScreen(double left, double top) =>
            left > SystemParameters.VirtualScreenLeft - 50 && top > SystemParameters.VirtualScreenTop - 20 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

        private Button Caption(string glyph, string tip, RoutedEventHandler click)
        {
            var b = new Button { Style = (Style)FindResource("Caption"), Content = glyph, ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += click;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        public void Show(PageId id)
        {
            if (id != PageId.Setup && !Settings.Current.SetupComplete && id != PageId.Settings) id = PageId.Setup;
            if (!_pages.TryGetValue(id, out var page))
            {
                page = id switch
                {
                    PageId.Setup => new SetupPage(),
                    PageId.Import => new ImportPage(),
                    PageId.Downloader => new DownloaderPage(),
                    PageId.Apps => new AppsPage(),
                    PageId.Settings => new SettingsPage(),
                    _ => Tv
                };
                _pages[id] = page;
            }
            Current = id;
            _host.Content = page;
            foreach (var kv in _nav)
                kv.Value.Style = (Style)FindResource(kv.Key == id ? "NavActive" : "Ghost");
            foreach (var kv in _nav) kv.Value.Visibility = id == PageId.Setup ? Visibility.Collapsed : Visibility.Visible;
            (page as IPage)?.OnShown();
            if (id == PageId.Tv) Tv.FocusScreen();
            RefreshFooter();
        }

        /// <summary>Drops a page so it's rebuilt next time (after setup finishes, for example).</summary>
        public void Forget(PageId id)
        {
            if (id != PageId.Tv) _pages.Remove(id);
        }

        public void RefreshStatus()
        {
            switch (Engine.State)
            {
                case EngineState.Running: _dot.Fill = Theme.Green; _statusText.Text = "Android TV running"; break;
                case EngineState.Starting: _dot.Fill = Theme.Orange; _statusText.Text = "Starting Android TV"; break;
                case EngineState.Stopping: _dot.Fill = Theme.Orange; _statusText.Text = "Closing Android TV"; break;
                case EngineState.Failed: _dot.Fill = Theme.Red; _statusText.Text = "Android TV stopped"; break;
                case EngineState.NotInstalled: _dot.Fill = Theme.Muted; _statusText.Text = "Setting up"; break;
                default: _dot.Fill = Theme.Muted; _statusText.Text = "Android TV off"; break;
            }
            RefreshFooter();
        }

        public void RefreshFooter()
        {
            var s = Settings.Current;
            string sys = s.InstalledImageApi > 0 ? (s.InstalledImageTag == "google-tv" ? "Google TV" : "Android TV") + " " + AndroidVersion(s.InstalledImageApi) : "Android TV";
            _footerRight.Text = sys + " · " + (Android.RamMb / 1024.0).ToString("0.#") + " GB RAM · TeleTV " + Updates.CurrentText;
            int busy = InstallQueue.Items.Count(i => i.Status == InstallStatus.Installing || i.Status == InstallStatus.Waiting);
            _footerLeft.Text = busy > 0
                ? "Installing apps: " + busy + " to go"
                : Current switch
                {
                    PageId.Tv => "Arrow keys move · Enter selects · Esc goes back · Home key for home · F11 full screen",
                    PageId.Import => "Drop .apk files anywhere on the TeleTV window to install them",
                    PageId.Downloader => "Downloads save to your PC · APKs offer Install straight away",
                    PageId.Apps => "Apps you've added to Android TV",
                    PageId.Settings => "Logs and files: " + Paths.Root,
                    _ => "First launch only. After this TeleTV opens in seconds."
                };
        }

        public static string AndroidVersion(int api) => api switch
        {
            >= 36 => "16",
            35 => "15",
            34 => "14",
            33 => "13",
            32 or 31 => "12",
            30 => "11",
            29 => "10",
            28 => "9",
            _ => "(API " + api + ")"
        };

        public void ToggleFullScreen()
        {
            _fullScreen = !_fullScreen;
            if (_fullScreen)
            {
                _stateBeforeFull = WindowState;
                _titleBar.Visibility = Visibility.Collapsed;
                _footer.Visibility = Visibility.Collapsed;
                WindowChrome.GetWindowChrome(this).CaptionHeight = 0;
                WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(0);
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // re-maximise so it covers the taskbar
                WindowState = WindowState.Maximized;
                ((Grid)Content).Margin = new Thickness(0);
                Topmost = true;
                Show(PageId.Tv);
            }
            else
            {
                Topmost = false;
                _titleBar.Visibility = Visibility.Visible;
                _footer.Visibility = Visibility.Visible;
                WindowChrome.GetWindowChrome(this).CaptionHeight = 40;
                WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(6);
                WindowState = _stateBeforeFull == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                ((Grid)Content).Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            }
            _fullBtn.Content = _fullScreen ? Theme.GExitFull : Theme.GFull;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F11 || (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt))
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                PageId? jump = key switch
                {
                    Key.D1 => PageId.Tv,
                    Key.D2 => PageId.Import,
                    Key.D3 => PageId.Downloader,
                    Key.D4 => PageId.Apps,
                    Key.OemComma => PageId.Settings,
                    _ => null
                };
                if (jump.HasValue && Settings.Current.SetupComplete) { Show(jump.Value); e.Handled = true; return; }
                if (key == Key.O && Settings.Current.SetupComplete) { Show(PageId.Import); ((ImportPage)_pages[PageId.Import]).Browse(); e.Handled = true; return; }
            }
            if (Current == PageId.Tv) Tv.HandleKey(e, true);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
            if (Current == PageId.Tv) Tv.HandleKey(e, false);
        }

        private static IEnumerable<string> Files(DragEventArgs e) =>
            e.Data.GetDataPresent(DataFormats.FileDrop) ? (string[])e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>() : Array.Empty<string>();

        private void OnDrop(object sender, DragEventArgs e)
        {
            var apks = Files(e).Where(InstallQueue.IsApk).ToList();
            if (apks.Count == 0 || !Settings.Current.SetupComplete) return;
            foreach (var f in apks) InstallQueue.Add(f, "Dropped on TeleTV");
            if (Current != PageId.Import) Show(PageId.Import);
            e.Handled = true;
        }

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            SaveWindow();
            if (_closingForReal) return;
            if (Engine.State == EngineState.Running || Engine.State == EngineState.Starting)
            {
                e.Cancel = true;
                _closingForReal = true;
                _overlayText.Text = Settings.Current.FastStart ? "Saving Android so it opens quickly next time" : "Closing Android TV";
                _overlay.Visibility = Visibility.Visible;
                try { await Engine.Stop(); } catch { }
                Application.Current.Shutdown();
            }
        }

        private void SaveWindow()
        {
            var s = Settings.Current;
            if (_fullScreen) { s.WinMaximised = _stateBeforeFull == WindowState.Maximized; }
            else
            {
                s.WinMaximised = WindowState == WindowState.Maximized;
                var r = RestoreBounds;
                if (!r.IsEmpty && r.Width > 100)
                {
                    s.WinLeft = r.Left; s.WinTop = r.Top; s.WinWidth = r.Width; s.WinHeight = r.Height;
                }
            }
            Settings.Save();
        }

        /// <summary>Brings the window to the front (another copy of TeleTV asked us to).</summary>
        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = _fullScreen;
            Focus();
        }

        public void Toast(string message) => _footerLeft.Text = message;
    }

    /// <summary>Pages that want to know when they come on screen.</summary>
    public interface IPage
    {
        void OnShown();
    }
}
