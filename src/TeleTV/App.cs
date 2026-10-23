using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TeleTV.Core;
using TeleTV.UI;

namespace TeleTV
{
    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            var apks = args.Where(a => !a.StartsWith("--") && InstallQueue.IsApk(a) && File.Exists(a)).Select(Path.GetFullPath).ToList();

            _mutex = new Mutex(true, "TeleTV.SingleInstance.bhxuk", out bool isNew);
            if (!isNew && args.Any(a => a == "--restarting"))
            {
                for (int i = 0; i < 50 && !isNew; i++)
                {
                    Thread.Sleep(200);
                    _mutex.Dispose();
                    _mutex = new Mutex(true, "TeleTV.SingleInstance.bhxuk", out isNew);
                }
            }
            if (!isNew)
            {
                // Already open: hand over any APKs and bring that window forward.
                try
                {
                    if (apks.Count > 0) File.AppendAllLines(Paths.PendingInstalls, apks);
                    using var ev = EventWaitHandle.OpenExisting("TeleTV.Show.bhxuk");
                    ev.Set();
                }
                catch { }
                return 0;
            }

            var app = new App { StartupApks = apks };
            int code = app.Run();
            GC.KeepAlive(_mutex);
            return code;
        }
    }

    public class App : Application
    {
        public System.Collections.Generic.List<string> StartupApks { get; set; } = new System.Collections.Generic.List<string>();
        private EventWaitHandle _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += (s, ex) =>
            {
                Log.Write("Unhandled: " + ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("Fatal: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Write("Task: " + ex.Exception?.GetBaseException().Message); ex.SetObserved(); };

            Log.Write("TeleTV " + Updates.CurrentText + " starting on " + Environment.OSVersion);
            Settings.Load();
            Theme.Install(this);
            InstallQueue.Init(Dispatcher);
            Engine.MarkInstalled(Settings.Current.SetupComplete);

            var win = new MainWindow();
            MainWindow = win;
            win.Show();

            foreach (var a in StartupApks) InstallQueue.Add(a, "Opened from Windows");
            if (StartupApks.Count > 0 && Settings.Current.SetupComplete) win.Show(PageId.Import);

            // Another copy of TeleTV signals this when it's opened again.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TeleTV.Show.bhxuk");
            var t = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        win.BringToFront();
                        int before = InstallQueue.Items.Count;
                        InstallQueue.TakePendingFromOtherInstances();
                        if (InstallQueue.Items.Count > before && Settings.Current.SetupComplete) win.Show(PageId.Import);
                    }));
                }
            }) { IsBackground = true, Name = "ShowListener" };
            t.Start();

            if (Settings.Current.SetupComplete)
            {
                Sdk.ClearLeftovers();
                _ = TvPage.StartSafe();
            }
            _ = BackgroundUpdateCheck();
        }

        private static async Task BackgroundUpdateCheck()
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            var s = Settings.Current;
            if (!s.AutoCheckUpdates) return;
            try
            {
                if (await Updates.CheckApp())
                    Current.Dispatcher.Invoke(() => UI.MainWindow.Instance?.Toast("TeleTV " + Updates.LatestVersion + " is ready. Open Settings > Updates to install it."));

                if (!s.SetupComplete) return;
                var sdk = await Updates.CheckSdk();
                if (sdk.Count > 0 && s.BackgroundDownload && !Updates.SdkPending)
                {
                    await Updates.DownloadSdk(sdk, null, CancellationToken.None);
                    Updates.SdkAvailable.Clear();
                    Current.Dispatcher.Invoke(() => UI.MainWindow.Instance?.Toast("An Android TV update is downloaded. It's applied next time Android starts."));
                }
            }
            catch (Exception ex) { Log.Write("Background update: " + ex.Message); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Engine.StopBlocking();
            Settings.Save();
            base.OnExit(e);
        }
    }
}
