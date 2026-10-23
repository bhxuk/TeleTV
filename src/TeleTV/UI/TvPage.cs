using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TeleTV.Core;

namespace TeleTV.UI
{
    /// <summary>The Android TV screen. Shows the live picture and sends keyboard, mouse and remote input into Android.</summary>
    public class TvPage : Grid, IPage
    {
        private readonly Image _screen = new Image { Stretch = Stretch.Uniform, Focusable = false, SnapsToDevicePixels = true };
        private readonly Border _surface;
        private readonly Grid _cover = new Grid { Background = Theme.Bg };
        private readonly StackPanel _coverBody = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        private WriteableBitmap _bitmap;
        private ScreenFrame _latest;
        private int _pendingDraw;
        private bool _flipChecked;
        private bool _flipped;
        private DateTime _lastMove;
        private int _buttons;

        public TvPage()
        {
            Background = Brushes.Black;
            Focusable = false;
            RenderOptions.SetBitmapScalingMode(_screen, BitmapScalingMode.HighQuality);
            _screen.RenderTransformOrigin = new Point(0.5, 0.5);

            _surface = new Border
            {
                Background = Brushes.Black,
                Child = _screen,
                Focusable = true,
                FocusVisualStyle = null,
                Cursor = Cursors.Arrow
            };
            _surface.MouseDown += OnMouseDown;
            _surface.MouseUp += OnMouseUp;
            _surface.MouseMove += OnMouseMove;
            _surface.MouseWheel += (o, e) => { Engine.Wheel(e.Delta); e.Handled = true; };
            _surface.TextInput += (o, e) => { }; // text is sent as key presses, see HandleKey
            Children.Add(_surface);

            _cover.Children.Add(_coverBody);
            Children.Add(_cover);

            SizeChanged += (o, e) => PushViewSize();
            Engine.FrameReady += OnFrame;
            Engine.StateChanged += () => Dispatcher.BeginInvoke(new Action(Refresh));
            Engine.Status += t => Dispatcher.BeginInvoke(new Action(() => { if (Engine.State == EngineState.Starting) ShowStarting(t); }));
            Refresh();
        }

        public void OnShown()
        {
            Refresh();
            FocusScreen();
        }

        public void FocusScreen()
        {
            Dispatcher.BeginInvoke(new Action(() => { _surface.Focus(); Keyboard.Focus(_surface); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void PushViewSize()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Engine.SetViewSize((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
        }

        // ---------------- Cover states ----------------

        private void Refresh()
        {
            switch (Engine.State)
            {
                case EngineState.Running:
                    _cover.Visibility = Visibility.Collapsed;
                    PushViewSize();
                    FocusScreen();
                    break;
                case EngineState.Starting:
                    _flipChecked = false;
                    ShowStarting(Engine.StatusText);
                    break;
                case EngineState.Stopping:
                    ShowMessage(Theme.GPower, "Closing Android TV", "Saving where you were so it opens quickly next time.", null, spinner: true);
                    break;
                case EngineState.Failed:
                    ShowMessage(Theme.GError, "Android TV stopped", Engine.LastError,
                        Theme.Row(10,
                            Theme.Btn("Try again", "Primary", async (o, e) => await StartSafe()),
                            Theme.Btn("Settings", "Secondary", (o, e) => MainWindow.Instance.Show(PageId.Settings))));
                    break;
                case EngineState.NotInstalled:
                    ShowMessage(Theme.GTv, "Android TV isn't set up yet", "TeleTV downloads it from Google the first time.",
                        Theme.Btn("Set up now", "Primary", (o, e) => MainWindow.Instance.Show(PageId.Setup)));
                    break;
                default:
                    ShowMessage(Theme.GTv, "Android TV is off", "Start it to pick up where you left off.",
                        Theme.Btn("Start Android TV", "Primary", async (o, e) => await StartSafe(), Theme.GPlay));
                    break;
            }
        }

        public static async Task StartSafe(bool wipe = false)
        {
            try { await Engine.Start(wipe); }
            catch { /* the Failed state shows the reason */ }
        }

        private void ShowStarting(string text)
        {
            ShowMessage(Theme.GTv, "Starting Android TV", text + "…", null, spinner: true);
        }

        private void ShowMessage(string glyph, string title, string body, UIElement action, bool spinner = false)
        {
            _cover.Visibility = Visibility.Visible;
            _coverBody.Children.Clear();
            var badge = new Border
            {
                Width = 88,
                Height = 88,
                CornerRadius = new CornerRadius(22),
                Background = Theme.AccentBg,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = spinner ? Theme.Spinner(40) : (UIElement)new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 40, Foreground = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            if (spinner) ((FrameworkElement)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
            _coverBody.Children.Add(badge);
            var t = Theme.T(title, 26, Theme.Text, FontWeights.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 24, 0, 8);
            _coverBody.Children.Add(t);
            if (!string.IsNullOrEmpty(body))
            {
                var b = Theme.T(body, 15, Theme.Muted, wrap: true);
                b.TextAlignment = TextAlignment.Center;
                b.HorizontalAlignment = HorizontalAlignment.Center;
                _coverBody.Children.Add(b);
            }
            if (action is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                fe.Margin = new Thickness(0, 24, 0, 0);
                _coverBody.Children.Add(fe);
            }
        }

        // ---------------- Picture ----------------

        private void OnFrame(ScreenFrame f)
        {
            Interlocked.Exchange(ref _latest, f);
            if (Interlocked.Exchange(ref _pendingDraw, 1) == 0)
                Dispatcher.BeginInvoke(new Action(Draw), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void Draw()
        {
            Interlocked.Exchange(ref _pendingDraw, 0);
            var f = Interlocked.Exchange(ref _latest, null);
            if (f == null) return;
            if (_bitmap == null || _bitmap.PixelWidth != f.Width || _bitmap.PixelHeight != f.Height)
            {
                _bitmap = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Rgb24, null);
                _screen.Source = _bitmap;
            }
            try { _bitmap.WritePixels(new Int32Rect(0, 0, f.Width, f.Height), f.Pixels, f.Width * 3, 0); }
            catch (Exception ex) { Log.Write("Draw: " + ex.Message); }
            if (!_flipChecked) { _flipChecked = true; _ = CheckRowOrder(f); }
        }

        /// <summary>The raw frame format doesn't say for sure which way up its rows are. Compare once with a PNG screenshot and flip if needed.</summary>
        private async Task CheckRowOrder(ScreenFrame raw)
        {
            try
            {
                await Task.Delay(800);
                var png = await Engine.ScreenshotPng(raw.Width, raw.Height);
                if (png == null) return;
                var decoder = BitmapDecoder.Create(new System.IO.MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var conv = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Rgb24, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                if (w != raw.Width || h != raw.Height) return;
                var cur = _latest ?? raw;
                if (cur.Width != w || cur.Height != h) cur = raw;
                var pngPixels = new byte[w * h * 3];
                conv.CopyPixels(pngPixels, w * 3, 0);

                long same = 0, flipped = 0;
                for (int i = 0; i < 24; i++)
                {
                    int y = (int)((h - 1) * (i + 0.5) / 24.0);
                    for (int x = 0; x < w; x += Math.Max(1, w / 64))
                    {
                        int p = (y * w + x) * 3, q = ((h - 1 - y) * w + x) * 3;
                        for (int c = 0; c < 3; c++)
                        {
                            same += Math.Abs(cur.Pixels[p + c] - pngPixels[p + c]);
                            flipped += Math.Abs(cur.Pixels[q + c] - pngPixels[p + c]);
                        }
                    }
                }
                bool flip = flipped * 1.5 < same;
                Log.Write($"Row order check: same={same} flipped={flipped} -> {(flip ? "flip" : "keep")}");
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _flipped = flip;
                    _screen.RenderTransform = flip ? new ScaleTransform(1, -1) : Transform.Identity;
                }));
            }
            catch (Exception ex) { Log.Write("Row order check: " + ex.Message); }
        }

        // ---------------- Input ----------------

        /// <summary>Called by the window for every key while the TV page is showing.</summary>
        public void HandleKey(KeyEventArgs e, bool down)
        {
            if (!Engine.IsRunning) return;
            if (Keyboard.FocusedElement is TextBox) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Leave Windows shortcuts alone.
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && key != Key.LeftAlt && key != Key.RightAlt) return;
            if (key == Key.LWin || key == Key.RWin || key == Key.F11) return;

            e.Handled = true;
            switch (key)
            {
                case Key.Escape:
                case Key.BrowserBack:
                    if (down) Engine.KeyNamed("GoBack");
                    return;
                case Key.Home when Keyboard.Modifiers == ModifierKeys.None:
                case Key.BrowserHome:
                    if (down) Engine.KeyNamed("GoHome");
                    return;
                case Key.Apps:
                    if (down) Engine.KeyNamed("ContextMenu");
                    return;
                case Key.F5:
                    if (down) Engine.KeyNamed("AppSwitch");
                    return;
                case Key.V when down && Keyboard.Modifiers == ModifierKeys.Control:
                    // Paste: copy the Windows clipboard into Android, then press Ctrl+V there.
                    try { if (Clipboard.ContainsText()) Engine.SetClipboard(Clipboard.GetText()); } catch { }
                    break;
            }

            // Media and volume keys have no keyboard position, so send them by name.
            string named = key switch
            {
                Key.MediaPlayPause => "MediaPlayPause",
                Key.MediaNextTrack => "MediaTrackNext",
                Key.MediaPreviousTrack => "MediaTrackPrevious",
                Key.MediaStop => "MediaStop",
                Key.VolumeUp => "AudioVolumeUp",
                Key.VolumeDown => "AudioVolumeDown",
                Key.VolumeMute => "AudioVolumeMute",
                _ => null
            };
            if (named != null)
            {
                if (down) Engine.KeyNamed(named);
                return;
            }

            // The emulator wants Windows scan codes (where the key sits on the keyboard), not virtual-key numbers.
            // Extended keys such as the arrows come back as 0xE0xx, which is the form it expects.
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0) return;
            int scan = (int)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
            if (scan > 0) Engine.KeyScan(scan, down);
            else if (down) Log.Write("No scan code for key " + key);
        }

        private const uint MAPVK_VK_TO_VSC_EX = 4;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private (int x, int y)? ToDevice(Point p)
        {
            if (_bitmap == null || _screen.ActualWidth <= 0) return null;
            var pos = _surface.TranslatePoint(p, _screen);
            double fx = pos.X / _screen.ActualWidth, fy = pos.Y / _screen.ActualHeight;
            if (_flipped) fy = 1 - fy;
            if (fx < 0 || fy < 0 || fx > 1 || fy > 1) return null;
            return ((int)(fx * Engine.ScreenWidth), (int)(fy * Engine.ScreenHeight));
        }

        private static int Buttons(MouseEventArgs e) =>
            (e.LeftButton == MouseButtonState.Pressed ? 1 : 0) | (e.RightButton == MouseButtonState.Pressed ? 2 : 0);

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _surface.Focus();
            if (!Engine.IsRunning) return;
            if (e.ChangedButton == MouseButton.XButton1) { Engine.KeyNamed("GoBack"); return; }
            if (e.ChangedButton == MouseButton.Middle) { Engine.KeyNamed("GoHome"); return; }
            var d = ToDevice(e.GetPosition(_surface));
            if (d == null) return;
            _surface.CaptureMouse();
            _buttons = Buttons(e);
            Engine.Mouse(d.Value.x, d.Value.y, _buttons);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!Engine.IsRunning) return;
            var d = ToDevice(e.GetPosition(_surface));
            _buttons = Buttons(e);
            if (_buttons == 0) _surface.ReleaseMouseCapture();
            if (d != null) Engine.Mouse(d.Value.x, d.Value.y, _buttons);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!Engine.IsRunning) return;
            if ((DateTime.UtcNow - _lastMove).TotalMilliseconds < 14) return;
            _lastMove = DateTime.UtcNow;
            var d = ToDevice(e.GetPosition(_surface));
            if (d != null) Engine.Mouse(d.Value.x, d.Value.y, Buttons(e));
        }
    }
}
