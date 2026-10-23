using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>The built-in Downloader: a browser that saves downloads to this PC and installs APKs into Android with one click.</summary>
    public class DownloaderPage : Grid, IPage
    {
        private enum Tab { Browser, Downloads, Favourites }

        private readonly TextBox _url = new TextBox { Height = 50, FontSize = 16 };
        private readonly Button _star;
        private readonly Grid _content = new Grid();
        private readonly StackPanel _cards = new StackPanel();
        private readonly Dictionary<Tab, Button> _tabs = new Dictionary<Tab, Button>();
        private WebView2 _web;
        private readonly Grid _browserHost = new Grid();
        private bool _webReady;
        private bool _webFailed;
        private Tab _tab = Tab.Browser;

        public DownloaderPage()
        {
            Background = Theme.Bg;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- Left rail ----
            var rail = new StackPanel { Margin = new Thickness(16, 24, 16, 16) };
            var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Theme.Orange, Child = Theme.Icon(Theme.GDownload, 16, Theme.B("#111111")) };
            ((TextBlock)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
            rail.Children.Add(Theme.Row(10, badge, Theme.T("Downloader", 17, Theme.Text, FontWeights.Bold)));
            rail.Children[0].SetValue(MarginProperty, new Thickness(8, 0, 0, 18));
            foreach (var (tab, tabLabel, glyph) in new[] { (Tab.Browser, "Browser", Theme.GGlobe), (Tab.Downloads, "Downloads", Theme.GDownload), (Tab.Favourites, "Favourites", Theme.GStar) })
            {
                var b = Theme.Btn(tabLabel, "Ghost", (o, e) => ShowTab(tab), glyph);
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Padding = new Thickness(14, 11, 14, 11);
                b.FontSize = 15;
                b.Margin = new Thickness(0, 0, 0, 4);
                _tabs[tab] = b;
                rail.Children.Add(b);
            }
            var railBorder = new Border { Background = Theme.Rail, BorderBrush = Theme.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
            Children.Add(railBorder);

            // ---- Main ----
            var main = new Grid { Margin = new Thickness(28, 24, 28, 20) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = Theme.T("Enter a web address or a direct APK link", 13, Theme.Muted);
            label.Margin = new Thickness(0, 0, 0, 8);
            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nav = Theme.Row(4,
                Small(Theme.GBack, "Back", (o, e) => { if (_web?.CanGoBack == true) _web.GoBack(); }),
                Small(Theme.GForward, "Forward", (o, e) => { if (_web?.CanGoForward == true) _web.GoForward(); }),
                Small(Theme.GRefresh, "Reload", (o, e) => _web?.Reload()));
            nav.Margin = new Thickness(0, 0, 10, 0);
            bar.Children.Add(nav);
            _url.Text = Settings.Current.LastUrl ?? "";
            _url.KeyDown += (o, e) => { if (e.Key == Key.Enter) { Go(); e.Handled = true; } };
            _url.GotKeyboardFocus += (o, e) => _url.SelectAll();
            Grid.SetColumn(_url, 1);
            bar.Children.Add(_url);
            _star = Small(Theme.GStar, "Add to favourites", (o, e) => ToggleFavourite());
            var go = Theme.Btn("Go", "Warm", (o, e) => Go());
            go.Height = 50;
            go.Padding = new Thickness(26, 0, 26, 0);
            go.FontSize = 16;
            var right = Theme.Row(8, _star, go);
            right.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(right, 2);
            bar.Children.Add(right);
            var top = Theme.Stack(0, label, bar);
            main.Children.Add(top);

            _cards.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(_cards, 1);
            main.Children.Add(_cards);

            _content.Margin = new Thickness(0, 16, 0, 0);
            Grid.SetRow(_content, 2);
            main.Children.Add(_content);

            Grid.SetColumn(main, 1);
            Children.Add(main);

            _browserHost.Children.Add(StartHint("Opening the browser…"));
            ShowTab(Tab.Browser);
        }

        private Button Small(string glyph, string tip, RoutedEventHandler click)
        {
            var b = Theme.Btn("", "Secondary", click, glyph);
            b.Width = 44; b.Height = 50; b.Padding = new Thickness(0);
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            return b;
        }

        public void OnShown()
        {
            if (_tab == Tab.Browser) _ = EnsureWeb();
            else ShowTab(_tab);
        }

        private void ShowTab(Tab tab)
        {
            _tab = tab;
            foreach (var kv in _tabs)
            {
                bool on = kv.Key == tab;
                kv.Value.Background = on ? Theme.White : Brushes.Transparent;
                kv.Value.Foreground = on ? Theme.Bg : Theme.Sub;
                kv.Value.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            _content.Children.Clear();
            switch (tab)
            {
                case Tab.Browser:
                    _content.Children.Add(new Border { CornerRadius = new CornerRadius(12), BorderBrush = Theme.Edge, BorderThickness = new Thickness(1), Background = Theme.Surface, ClipToBounds = true, Child = _browserHost });
                    _ = EnsureWeb();
                    break;
                case Tab.Downloads:
                    _content.Children.Add(Theme.Scroll(DownloadsList()));
                    break;
                case Tab.Favourites:
                    _content.Children.Add(Theme.Scroll(FavouritesList()));
                    break;
            }
        }

        private static UIElement StartHint(string text)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 480 };
            var t = Theme.T(text, 15, Theme.Muted, wrap: true);
            t.TextAlignment = TextAlignment.Center;
            sp.Children.Add(t);
            return sp;
        }

        // ---------------- Browser ----------------

        private async Task EnsureWeb()
        {
            if (_webReady || _webFailed || _web != null) return;
            try
            {
                _web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x17, 0x1B, 0x22) };
                var env = await CoreWebView2Environment.CreateAsync(null, Paths.Dir(Path.Combine(Paths.Root, "browser")));
                _browserHost.Children.Clear();
                _browserHost.Children.Add(_web);
                await _web.EnsureCoreWebView2Async(env);
                var core = _web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                core.DownloadStarting += OnDownloadStarting;
                core.NewWindowRequested += (o, e) => { e.Handled = true; core.Navigate(e.Uri); };
                core.SourceChanged += (o, e) => UpdateUrlBox();
                core.NavigationCompleted += (o, e) => UpdateUrlBox();
                _webReady = true;
                if (!string.IsNullOrWhiteSpace(Settings.Current.LastUrl) && Settings.Current.LastUrl.StartsWith("http")) core.Navigate(Settings.Current.LastUrl);
                else core.NavigateToString(StartPageHtml());
            }
            catch (Exception ex)
            {
                Log.Write("Browser unavailable: " + ex.Message);
                _webFailed = true;
                _web = null;
                _browserHost.Children.Clear();
                _browserHost.Children.Add(StartHint("The built-in browser needs Microsoft Edge WebView2, which isn't on this PC. Direct APK links still work: paste one above and press Go. Get WebView2 from microsoft.com/edge/webview2."));
            }
        }

        private void UpdateUrlBox()
        {
            var src = _web?.CoreWebView2?.Source;
            if (string.IsNullOrEmpty(src) || src.StartsWith("data:") || src == "about:blank") { PaintStar(""); return; }
            if (!_url.IsKeyboardFocused) _url.Text = src;
            Settings.Current.LastUrl = src;
            PaintStar(src);
        }

        private void PaintStar(string url)
        {
            bool fav = !string.IsNullOrEmpty(url) && Settings.Current.Favourites.Contains(url);
            ((TextBlock)((StackPanel)_star.Content).Children[0]).Text = fav ? Theme.GStarFilled : Theme.GStar;
            _star.Foreground = fav ? Theme.Orange : Theme.Text;
        }

        private static string Normalise(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")) return u.ToString();
            if (!text.Contains(' ') && text.Contains('.')) return "https://" + text;
            return "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
        }

        private void Go()
        {
            string url = Normalise(_url.Text);
            if (url == null) return;
            _url.Text = url;
            Settings.Current.LastUrl = url;
            Settings.Save();
            if (_tab != Tab.Browser) ShowTab(Tab.Browser);
            if (_webReady) _web.CoreWebView2.Navigate(url);
            else _ = DirectDownload(url); // no browser on this PC: fetch the link ourselves
        }

        private void ToggleFavourite()
        {
            string url = _web?.CoreWebView2?.Source;
            if (string.IsNullOrEmpty(url) || url.StartsWith("data:")) url = Normalise(_url.Text);
            if (url == null) return;
            var list = Settings.Current.Favourites.ToList();
            if (list.Contains(url)) list.Remove(url); else list.Insert(0, url);
            Settings.Current.Favourites = list.ToArray();
            Settings.Save();
            PaintStar(url);
            if (_tab == Tab.Favourites) ShowTab(Tab.Favourites);
        }

        private static string StartPageHtml() => @"<!doctype html><html><head><meta charset='utf-8'><style>
body{margin:0;background:#171B22;color:#E6EAF0;font-family:'Segoe UI',sans-serif;display:flex;align-items:center;justify-content:center;height:100vh}
.c{max-width:520px;text-align:center;padding:24px}h1{font-size:24px;margin:0 0 10px}p{color:#9AA4B2;font-size:15px;line-height:1.5;margin:0 0 8px}
</style></head><body><div class='c'><h1>Downloader</h1>
<p>Type a web address or a direct link to an APK in the box above and press Go.</p>
<p>Anything you download is saved on this PC. When it's an Android app, TeleTV offers to install it straight away.</p>
</div></body></html>";

        // ---------------- Downloads ----------------

        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            string name = SafeName(Path.GetFileName(e.ResultFilePath));
            string path = Unique(Path.Combine(Paths.Downloads, name));
            e.ResultFilePath = path;
            e.Handled = true; // no browser download bubble
            var op = e.DownloadOperation;
            var card = new DownloadCard(Path.GetFileName(path), (long)(op.TotalBytesToReceive ?? 0));
            _cards.Children.Insert(0, card);
            while (_cards.Children.Count > 2) _cards.Children.RemoveAt(_cards.Children.Count - 1);
            op.BytesReceivedChanged += (o, a) => card.SetProgress(op.BytesReceived, (long)(op.TotalBytesToReceive ?? 0));
            op.StateChanged += (o, a) =>
            {
                if (op.State == CoreWebView2DownloadState.Completed) card.Done(path, this);
                else if (op.State == CoreWebView2DownloadState.Interrupted) card.Failed("Download stopped: " + op.InterruptReason);
            };
        }

        /// <summary>Downloads a link without the browser (used when WebView2 isn't installed).</summary>
        private async Task DirectDownload(string url)
        {
            string name = SafeName(Path.GetFileName(new Uri(url).AbsolutePath));
            if (string.IsNullOrEmpty(Path.GetExtension(name))) name += ".apk";
            string path = Unique(Path.Combine(Paths.Downloads, name));
            var card = new DownloadCard(Path.GetFileName(path), 0);
            _cards.Children.Insert(0, card);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TeleTV/" + Updates.CurrentText);
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using (var src = await resp.Content.ReadAsStreamAsync())
                await using (var dst = File.Create(path))
                {
                    var buf = new byte[1 << 16];
                    long done = 0;
                    int n;
                    var last = DateTime.MinValue;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        done += n;
                        if ((DateTime.UtcNow - last).TotalMilliseconds > 150) { last = DateTime.UtcNow; card.SetProgress(done, total); }
                    }
                }
                card.Done(path, this);
            }
            catch (Exception ex)
            {
                card.Failed("Couldn't download: " + ex.Message);
                try { File.Delete(path); } catch { }
            }
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string p = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }

        private UIElement DownloadsList()
        {
            var sp = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(Theme.T("Downloads", 18, Theme.Text, FontWeights.SemiBold));
            var open = Theme.Btn("Open folder", "Ghost", (o, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Paths.Downloads + "\"") { UseShellExecute = true }), Theme.GFolder);
            open.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(open);
            sp.Children.Add(head);

            var files = new DirectoryInfo(Paths.Downloads).GetFiles().Where(f => !f.Name.EndsWith(".crdownload")).OrderByDescending(f => f.LastWriteTime).ToList();
            if (files.Count == 0) sp.Children.Add(Theme.T("Nothing downloaded yet.", 14, Theme.Muted));
            foreach (var f in files)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = Theme.Stack(2, Theme.T(f.Name, 15, Theme.Text), Theme.T(Format.Size(f.Length) + " · " + f.LastWriteTime.ToString("d MMM HH:mm"), 12, Theme.Muted));
                info.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(info);
                var file = f.FullName;
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (InstallQueue.IsApk(file))
                {
                    var inst = Theme.Btn("Install", "Warm", (o, e) => { InstallQueue.Add(file, "From Downloader"); MainWindow.Instance.Show(PageId.Import); });
                    inst.Margin = new Thickness(0, 0, 8, 0);
                    actions.Children.Add(inst);
                }
                actions.Children.Add(Theme.Btn("", "Ghost", (o, e) =>
                {
                    try { File.Delete(file); } catch { }
                    ShowTab(Tab.Downloads);
                }, Theme.GDelete));
                Grid.SetColumn(actions, 1);
                g.Children.Add(actions);
                sp.Children.Add(new Border { Background = Theme.Surface, BorderBrush = Theme.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 12, 12), Margin = new Thickness(0, 0, 0, 8), Child = g });
            }
            return sp;
        }

        private UIElement FavouritesList()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Favourites", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 12) });
            if (Settings.Current.Favourites.Length == 0)
                sp.Children.Add(Theme.T("Press the star next to the address box to save a page here.", 14, Theme.Muted, wrap: true));
            foreach (var url in Settings.Current.Favourites)
            {
                var u = url;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = new Button { Style = (Style)Application.Current.FindResource("Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, Content = Theme.T(u, 15, Theme.Text), FontSize = 15, Padding = new Thickness(4, 6, 4, 6) };
                open.Click += (o, e) => { _url.Text = u; Go(); };
                g.Children.Add(open);
                var del = Theme.Btn("", "Ghost", (o, e) =>
                {
                    Settings.Current.Favourites = Settings.Current.Favourites.Where(x => x != u).ToArray();
                    Settings.Save();
                    ShowTab(Tab.Favourites);
                }, Theme.GDelete);
                Grid.SetColumn(del, 1);
                g.Children.Add(del);
                sp.Children.Add(new Border { Background = Theme.Surface, BorderBrush = Theme.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8), Child = g });
            }
            return sp;
        }

        /// <summary>The card shown above the browser while something downloads, with Install when it's an APK.</summary>
        private class DownloadCard : Border
        {
            private readonly TextBlock _title;
            private readonly TextBlock _sub = Theme.T("Starting", 13, Theme.Muted);
            private readonly ProgressBar _bar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
            private readonly StackPanel _actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            public DownloadCard(string name, long total)
            {
                Background = Theme.Surface;
                BorderBrush = Theme.B("#2E3A4F");
                BorderThickness = new Thickness(1);
                CornerRadius = new CornerRadius(14);
                Padding = new Thickness(20, 16, 20, 16);
                Margin = new Thickness(0, 0, 0, 10);
                _title = Theme.T(name, 16, Theme.Text, FontWeights.Bold);
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
                left.Children.Add(_title);
                left.Children.Add(_sub);
                left.Children.Add(_bar);
                g.Children.Add(left);
                Grid.SetColumn(_actions, 1);
                g.Children.Add(_actions);
                Child = g;
                if (total <= 0) _bar.IsIndeterminate = true;
            }

            public void SetProgress(long done, long total) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _bar.IsIndeterminate = total <= 0;
                if (total > 0) _bar.Value = done / (double)total;
                _sub.Text = Format.Size(done) + (total > 0 ? " of " + Format.Size(total) : "");
            }));

            public void Done(string path, DownloaderPage page) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _bar.IsIndeterminate = false;
                _bar.Value = 1;
                _bar.Foreground = Theme.Green;
                long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                _actions.Children.Clear();
                if (InstallQueue.IsApk(path))
                {
                    _title.Text = Path.GetFileName(path) + " downloaded";
                    _sub.Text = Format.Size(size) + " · Android app";
                    var install = Theme.Btn("Install", "Warm", (o, e) =>
                    {
                        InstallQueue.Add(path, "From Downloader");
                        _sub.Text = "Sent to Import APK";
                        _actions.Children.Clear();
                        _actions.Children.Add(Theme.Btn("See progress", "Secondary", (o2, e2) => MainWindow.Instance.Show(PageId.Import)));
                    });
                    install.Padding = new Thickness(26, 10, 26, 10);
                    install.Margin = new Thickness(0, 0, 8, 0);
                    _actions.Children.Add(install);
                    _actions.Children.Add(Theme.Btn("Keep file only", "Secondary", (o, e) => page._cards.Children.Remove(this)));
                    install.Focus();
                }
                else
                {
                    _title.Text = Path.GetFileName(path) + " saved";
                    _sub.Text = Format.Size(size) + " · in TeleTV's Downloads folder";
                    _actions.Children.Add(Theme.Btn("Show", "Secondary", (o, e) =>
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })));
                    _actions.Children.Add(Theme.Btn("", "Ghost", (o, e) => page._cards.Children.Remove(this), Theme.GClose));
                }
            }));

            public void Failed(string message) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _bar.Visibility = Visibility.Collapsed;
                _sub.Text = message;
                _sub.Foreground = Theme.Red;
                _actions.Children.Clear();
            }));
        }
    }
}
