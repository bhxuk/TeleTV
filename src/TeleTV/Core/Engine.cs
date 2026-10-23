using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using TeleTV.Emu;
using Empty = Google.Protobuf.WellKnownTypes.Empty;

namespace TeleTV.Core
{
    public enum EngineState { NotInstalled, Stopped, Starting, Running, Stopping, Failed }

    /// <summary>One screen image from Android, as packed RGB (3 bytes a pixel).</summary>
    public class ScreenFrame
    {
        public byte[] Pixels;
        public int Width;
        public int Height;
    }

    /// <summary>Starts and stops the Android TV virtual machine and talks to it: screen, keys, mouse.</summary>
    public static class Engine
    {
        public static EngineState State { get; private set; } = EngineState.Stopped;
        public static string StatusText { get; private set; } = "";
        public static string LastError { get; private set; } = "";

        public static event Action StateChanged;
        public static event Action<ScreenFrame> FrameReady;
        /// <summary>Progress during start-up, for the loading screen.</summary>
        public static event Action<string> Status;

        public static int ScreenWidth { get; private set; } = 1920;
        public static int ScreenHeight { get; private set; } = 1080;

        private static Process _emu;
        private static GrpcChannel _channel;
        private static EmulatorController.EmulatorControllerClient _client;
        private static Metadata _auth = new Metadata();
        private static CancellationTokenSource _run;
        private static CancellationTokenSource _stream;
        private static int _grpcPort, _consolePort;
        private static int _qemuPid;
        private static DateTime _launcherExit;
        private static int _wantW, _wantH;
        private static readonly Queue<string> _tail = new Queue<string>();
        private static bool _stopping;
        private static AsyncClientStreamingCall<WheelEvent, Empty> _wheel;
        private static readonly SemaphoreSlim _wheelGate = new SemaphoreSlim(1, 1);

        public static bool IsRunning => State == EngineState.Running;

        private static void SetState(EngineState s, string text = null)
        {
            State = s;
            if (text != null) StatusText = text;
            try { StateChanged?.Invoke(); } catch (Exception ex) { Log.Write("StateChanged handler: " + ex.Message); }
        }

        private static void Say(string text)
        {
            StatusText = text;
            try { Status?.Invoke(text); } catch { }
        }

        public static void MarkInstalled(bool installed)
        {
            if (!installed) SetState(EngineState.NotInstalled, "Android TV isn't installed yet");
            else if (State == EngineState.NotInstalled) SetState(EngineState.Stopped, "Ready to start");
        }

        /// <summary>Starts Android. Returns when the home screen is ready, or throws with a plain-English reason.</summary>
        public static async Task Start(bool wipeData = false)
        {
            if (State == EngineState.Starting || State == EngineState.Running) return;
            _stopping = false;
            LastError = "";
            _run?.Cancel();
            _run = new CancellationTokenSource();
            var ct = _run.Token;
            SetState(EngineState.Starting, "Starting Android TV");

            try
            {
                Say("Preparing Android TV");
                await Updates.ApplyPendingSdk();
                Android.WriteAvd();
                var (w, h, _) = Android.Screen;
                ScreenWidth = w; ScreenHeight = h;

                await Adb.StartServer();
                await KillLeftovers();

                _consolePort = Android.FreeConsolePort();
                _grpcPort = Android.FreePort(8580);
                _qemuPid = 0;
                _launcherExit = DateTime.MinValue;
                Adb.Serial = "emulator-" + _consolePort;

                string gpu = Settings.Current.Graphics switch { "host" => "host", "software" => "swiftshader_indirect", _ => "auto" };
                var args = new List<string>
                {
                    "-avd " + Paths.AvdName,
                    "-port " + _consolePort,
                    "-grpc " + _grpcPort,
                    "-grpc-use-token",
                    "-qt-hide-window",
                    "-no-boot-anim",
                    "-gpu " + gpu,
                    "-memory " + Android.RamMb,
                    "-cores " + Android.Cores,
                    "-netfast",
                    "-no-metrics"
                };
                if (!Settings.Current.FastStart) args.Add("-no-snapshot");
                if (wipeData) args.Add("-wipe-data");

                var psi = new ProcessStartInfo(Paths.EmulatorExe, string.Join(" ", args))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(Paths.EmulatorExe)
                };
                Android.ApplyEnvironment(psi);
                Log.Write("Starting: emulator " + psi.Arguments);
                _tail.Clear();
                _emu = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _emu.OutputDataReceived += (s, e) => Capture(e.Data);
                _emu.ErrorDataReceived += (s, e) => Capture(e.Data);
                _emu.Exited += OnEmulatorExited;
                _emu.Start();
                _emu.BeginOutputReadLine();
                _emu.BeginErrorReadLine();
                TryLowerPriority();

                Say("Starting Android TV");
                await Connect(ct);

                Say("Waiting for the home screen");
                var deadline = DateTime.UtcNow.AddMinutes(6);
                bool booted = false;
                while (!booted)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Alive()) throw new Exception(ExitReason());
                    if (DateTime.UtcNow > deadline) throw new Exception("Android took too long to start. Try Settings > Android TV > Restart Android, or switch Graphics to Software.");
                    try
                    {
                        var st = await _client.getStatusAsync(new Empty(), _auth, DateTime.UtcNow.AddSeconds(5), ct);
                        booted = st.Booted;
                    }
                    catch (RpcException) { }
                    if (!booted && File.Exists(Paths.AdbExe)) booted = await Adb.IsBooted();
                    if (!booted) await Task.Delay(1500, ct);
                }

                StartStream();
                SetState(EngineState.Running, "Android TV running");
                Log.Write("Android booted");
            }
            catch (Exception ex)
            {
                bool cancelled = ex is OperationCanceledException && (_stopping || ct.IsCancellationRequested);
                if (!cancelled) Log.Write("Start failed: " + ex);
                await StopProcess(true);
                if (cancelled) { SetState(EngineState.Stopped, "Stopped"); return; }
                LastError = ex is RpcException ? "Couldn't talk to Android. " + ExitReason() : ex.Message;
                SetState(EngineState.Failed, LastError);
                throw new Exception(LastError, ex);
            }
        }

        private static void TryLowerPriority()
        {
            try { _emu.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
        }

        private static void Capture(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_tail)
            {
                _tail.Enqueue(line);
                while (_tail.Count > 60) _tail.Dequeue();
            }
            if (line.Contains("ERROR") || line.Contains("FATAL") || line.Contains("WARNING")) Log.Write("emulator: " + line);
        }

        private static string ExitReason()
        {
            string[] lines;
            lock (_tail) lines = _tail.ToArray();
            var errors = lines.Where(l => l.Contains("ERROR") || l.Contains("FATAL") || l.Contains("PANIC") || l.Contains("error:")).ToList();
            string a = string.Join("\n", errors).ToLowerInvariant();
            if (a.Contains("whpx") || a.Contains("hypervisor") || a.Contains("aehd") || a.Contains("accel") || a.Contains("vt-x") || a.Contains("svm"))
                return "Windows Hypervisor Platform isn't available. Open Settings > Android TV and choose Fix virtualisation.";
            if (a.Contains("not enough space") || a.Contains("no space") || a.Contains("disk full"))
                return "The drive is full. Free some space and try again.";
            if (a.Contains("opengl") || a.Contains("vulkan") || a.Contains("gpu") || a.Contains("graphics"))
                return "The graphics driver couldn't start Android. In Settings > Android TV set Graphics to Software.";
            if (a.Contains("is already running") || a.Contains("multiinstance"))
                return "Another copy of this Android is still closing. Wait a few seconds and try again.";
            string err = errors.LastOrDefault();
            return "Android stopped unexpectedly." + (err != null ? " " + err.Trim() : "");
        }

        /// <summary>emulator.exe can hand over to its qemu process, so Android is alive while either is running.</summary>
        private static bool Alive()
        {
            var e = _emu;
            if (e == null) return false;
            if (!e.HasExited) return true;
            if (_qemuPid > 0) return ProcessAlive(_qemuPid);
            // The launcher has exited and we don't know about qemu yet: allow a short grace period if it exited cleanly.
            try { return e.ExitCode == 0 && (DateTime.UtcNow - _launcherExit).TotalSeconds < 30; } catch { return false; }
        }

        private static bool ProcessAlive(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch { return false; }
        }

        private static void OnEmulatorExited(object sender, EventArgs e)
        {
            _launcherExit = DateTime.UtcNow;
            if (_stopping) return;
            _ = Task.Run(async () =>
            {
                // Give a handed-over qemu process the chance to show up, then watch that instead.
                for (int i = 0; i < 30 && _qemuPid == 0 && !_stopping; i++)
                {
                    var (pid, _) = ReadDiscovery(_grpcPort);
                    if (pid > 0) _qemuPid = pid;
                    else await Task.Delay(1000);
                }
                while (!_stopping && _qemuPid > 0 && ProcessAlive(_qemuPid)) await Task.Delay(2000);
                if (_stopping) return;
                Log.Write("Android exited unexpectedly: " + ExitReason());
                _stream?.Cancel();
                if (State == EngineState.Running)
                {
                    LastError = ExitReason();
                    SetState(EngineState.Failed, LastError);
                }
            });
        }

        /// <summary>Waits for the emulator's control port, then connects with the access token it publishes.</summary>
        private static async Task Connect(CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!Alive()) throw new Exception(ExitReason());
                if (DateTime.UtcNow > deadline) throw new Exception("Android didn't start. " + ExitReason());

                var (pid, token) = ReadDiscovery(_grpcPort);
                if (pid > 0) _qemuPid = pid;
                _auth = new Metadata();
                if (!string.IsNullOrEmpty(token)) _auth.Add("authorization", "Bearer " + token);

                _channel?.Dispose();
                _channel = GrpcChannel.ForAddress("http://127.0.0.1:" + _grpcPort, new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = 64 * 1024 * 1024,
                    MaxSendMessageSize = 16 * 1024 * 1024,
                    HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, ConnectTimeout = TimeSpan.FromSeconds(3) }
                });
                _client = new EmulatorController.EmulatorControllerClient(_channel);
                try
                {
                    await _client.getStatusAsync(new Empty(), _auth, DateTime.UtcNow.AddSeconds(4), ct);
                    Log.Write("Connected to Android on port " + _grpcPort + (string.IsNullOrEmpty(token) ? " (no token)" : ""));
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated || ex.StatusCode == StatusCode.PermissionDenied)
                {
                    // The token file may not be written yet.
                }
                catch (RpcException) { }
                await Task.Delay(1000, ct);
            }
        }

        /// <summary>
        /// The emulator writes a discovery file (pid_NNNN.ini, named after its qemu process) with its gRPC port and token.
        /// Find the one for our port.
        /// </summary>
        private static (int pid, string token) ReadDiscovery(int port)
        {
            var dirs = new[]
            {
                Path.Combine(Path.GetTempPath(), "avd", "running"),
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Temp", "avd", "running"),
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".android", "avd", "running"),
                Path.Combine(Paths.AvdHome, "running")
            };
            foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d, "pid_*.ini"))
                    {
                        string token = null;
                        bool ours = false;
                        foreach (var line in File.ReadAllLines(f))
                        {
                            if (line.StartsWith("grpc.token=")) token = line.Substring(11).Trim();
                            else if (line.Trim() == "grpc.port=" + port) ours = true;
                        }
                        if (!ours) continue;
                        string name = Path.GetFileNameWithoutExtension(f);
                        int.TryParse(name.Substring(4), out int pid);
                        return (pid, token);
                    }
                }
                catch { }
            }
            return (0, null);
        }

        /// <summary>Closes any emulator left over from a crash so the device name and files are free.</summary>
        private static async Task KillLeftovers()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("qemu-system-x86_64").Concat(Process.GetProcessesByName("emulator")))
                {
                    try
                    {
                        string path = p.MainModule?.FileName ?? "";
                        if (path.StartsWith(Paths.Sdk, StringComparison.OrdinalIgnoreCase)) { p.Kill(true); await Task.Delay(500); }
                    }
                    catch { }
                }
                foreach (var lockFile in new[] { "hardware-qemu.ini.lock", "multiinstance.lock" })
                {
                    string f = Path.Combine(Paths.AvdDir, lockFile);
                    if (File.Exists(f)) try { File.Delete(f); } catch { }
                    if (Directory.Exists(f)) try { Directory.Delete(f, true); } catch { }
                }
            }
            catch { }
        }

        /// <summary>Saves Android's state for a quick start next time, then closes it.</summary>
        public static async Task Stop()
        {
            if (State == EngineState.Stopped || State == EngineState.NotInstalled) return;
            _stopping = true;
            _run?.Cancel();
            SetState(EngineState.Stopping, "Closing Android TV");
            await StopProcess(false);
            SetState(Settings.Current.SetupComplete ? EngineState.Stopped : EngineState.NotInstalled, "Stopped");
        }

        private static async Task StopProcess(bool force)
        {
            _stopping = true;
            _stream?.Cancel();
            try { _wheel?.Dispose(); } catch { }
            _wheel = null;
            var p = _emu;
            if (!force && Alive())
            {
                try { await Proc.Run(Paths.AdbExe, "-s " + Adb.Serial + " emu kill", 15000); } catch { }
                // Saving the quick-start state can take a while.
                for (int i = 0; i < 90 && Alive(); i++) await Task.Delay(500);
            }
            if (p != null) try { if (!p.HasExited) p.Kill(true); } catch { }
            if (_qemuPid > 0 && ProcessAlive(_qemuPid))
                try { using var q = Process.GetProcessById(_qemuPid); q.Kill(true); } catch { }
            _emu = null;
            _channel?.Dispose();
            _channel = null;
            _client = null;
        }

        /// <summary>Called when the app closes: waits briefly so Android can save its state.</summary>
        public static void StopBlocking()
        {
            try { Task.Run(Stop).Wait(TimeSpan.FromSeconds(50)); } catch { }
            try { if (_emu != null && !_emu.HasExited) _emu.Kill(true); } catch { }
        }

        public static async Task Restart(bool wipeData = false)
        {
            await Stop();
            await Start(wipeData);
        }

        // ---------------- Screen ----------------

        /// <summary>Tells the stream what size the view is, so frames arrive already scaled.</summary>
        public static void SetViewSize(int w, int h)
        {
            w = Math.Clamp(w, 320, ScreenWidth);
            h = Math.Clamp(h, 180, ScreenHeight);
            if (Math.Abs(w - _wantW) < 32 && Math.Abs(h - _wantH) < 18) return;
            _wantW = w; _wantH = h;
            if (IsRunning) StartStream();
        }

        private static void StartStream()
        {
            _stream?.Cancel();
            var cts = new CancellationTokenSource();
            _stream = cts;
            var client = _client;
            int w = _wantW > 0 ? _wantW : ScreenWidth, h = _wantH > 0 ? _wantH : ScreenHeight;
            _ = Task.Run(async () =>
            {
                int failures = 0;
                while (!cts.IsCancellationRequested && client != null)
                {
                    try
                    {
                        var fmt = new ImageFormat { Format = ImageFormat.Types.ImgFormat.Rgb888, Width = (uint)w, Height = (uint)h };
                        using var call = client.streamScreenshot(fmt, _auth, cancellationToken: cts.Token);
                        await foreach (var img in call.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            failures = 0;
                            int iw = (int)(img.Format?.Width ?? img.Width), ih = (int)(img.Format?.Height ?? img.Height);
                            if (iw <= 0 || ih <= 0 || img.Image_.Length < iw * ih * 3) continue;
                            FrameReady?.Invoke(new ScreenFrame { Pixels = img.Image_.ToByteArray(), Width = iw, Height = ih });
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { return; }
                    catch (Exception ex)
                    {
                        if (++failures % 5 == 1) Log.Write("Screen stream: " + ex.Message);
                    }
                    try { await Task.Delay(Math.Min(2000, 200 * failures), cts.Token); } catch { return; }
                }
            });
        }

        /// <summary>A PNG of the screen at full size, used to check the raw frames' row order.</summary>
        public static async Task<byte[]> ScreenshotPng(int w, int h)
        {
            var c = _client;
            if (c == null) return null;
            var img = await c.getScreenshotAsync(new ImageFormat { Format = ImageFormat.Types.ImgFormat.Png, Width = (uint)w, Height = (uint)h }, _auth, DateTime.UtcNow.AddSeconds(10));
            return img.Image_.ToByteArray();
        }

        // ---------------- Input ----------------

        private static void Fire(Func<EmulatorController.EmulatorControllerClient, Task> send)
        {
            var c = _client;
            if (c == null || !IsRunning) return;
            _ = Task.Run(async () =>
            {
                try { await send(c); }
                catch (Exception ex) { Log.Write("Input: " + ex.Message); }
            });
        }

        // Keys must arrive in order, so they go through one queue.
        private static readonly SemaphoreSlim _keyGate = new SemaphoreSlim(1, 1);

        private static void SendKeyEvent(KeyboardEvent ev)
        {
            var c = _client;
            if (c == null || !IsRunning) return;
            _ = Task.Run(async () =>
            {
                await _keyGate.WaitAsync();
                try { await c.sendKeyAsync(ev, _auth, DateTime.UtcNow.AddSeconds(5)); }
                catch (Exception ex) { Log.Write("Key: " + ex.Message); }
                finally { _keyGate.Release(); }
            });
        }

        /// <summary>A key press or release by Windows scan code (0xE0xx for extended keys like the arrows).</summary>
        public static void KeyScan(int scanCode, bool down) => SendKeyEvent(new KeyboardEvent
        {
            CodeType = KeyboardEvent.Types.KeyCodeType.Win,
            EventType = down ? KeyboardEvent.Types.KeyEventType.Keydown : KeyboardEvent.Types.KeyEventType.Keyup,
            KeyCode = scanCode
        });

        /// <summary>A named key such as GoBack, GoHome, AppSwitch, MediaPlayPause, ArrowUp, Enter.</summary>
        public static void KeyNamed(string key) => SendKeyEvent(new KeyboardEvent { EventType = KeyboardEvent.Types.KeyEventType.Keypress, Key = key });

        /// <summary>A named key held down or let go, so holding an arrow repeats like a remote.</summary>
        public static void KeyNamed(string key, bool down) => SendKeyEvent(new KeyboardEvent
        {
            EventType = down ? KeyboardEvent.Types.KeyEventType.Keydown : KeyboardEvent.Types.KeyEventType.Keyup,
            Key = key
        });

        public static void Text(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            SendKeyEvent(new KeyboardEvent { Text = text });
        }

        public static void Mouse(int x, int y, int buttons) =>
            Fire(c => c.sendMouseAsync(new MouseEvent { X = x, Y = y, Buttons = buttons }, _auth, DateTime.UtcNow.AddSeconds(5)).ResponseAsync);

        public static void Wheel(int dy)
        {
            var c = _client;
            if (c == null || !IsRunning) return;
            _ = Task.Run(async () =>
            {
                await _wheelGate.WaitAsync();
                try
                {
                    _wheel ??= c.injectWheel(_auth);
                    await _wheel.RequestStream.WriteAsync(new WheelEvent { Dy = dy });
                }
                catch (Exception ex)
                {
                    Log.Write("Wheel: " + ex.Message);
                    try { _wheel?.Dispose(); } catch { }
                    _wheel = null;
                }
                finally { _wheelGate.Release(); }
            });
        }

        public static void SetClipboard(string text) =>
            Fire(c => c.setClipboardAsync(new ClipData { Text = text ?? "" }, _auth, DateTime.UtcNow.AddSeconds(5)).ResponseAsync);

        public static async Task<string> GetClipboard()
        {
            var c = _client;
            if (c == null) return null;
            try { return (await c.getClipboardAsync(new Empty(), _auth, DateTime.UtcNow.AddSeconds(5))).Text; }
            catch { return null; }
        }
    }
}
