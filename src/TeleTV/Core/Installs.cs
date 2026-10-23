using System.IO.Compression;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TeleTV.Core
{
    public enum InstallStatus { Waiting, Installing, Installed, Failed }

    public class InstallItem
    {
        public string File { get; set; }
        public string Name => Path.GetFileName(File);
        public long Size { get; set; }
        public string Source { get; set; }
        public InstallStatus Status { get; set; }
        public string Message { get; set; }
        public string Package { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;
    }

    /// <summary>Installs APKs into Android one at a time. Anything can add to it: Import, drag and drop, the Downloader, double-clicking an .apk.</summary>
    public static class InstallQueue
    {
        public static ObservableCollection<InstallItem> Items { get; } = new ObservableCollection<InstallItem>();
        public static event Action<InstallItem> Changed;

        private static readonly SemaphoreSlim Signal = new SemaphoreSlim(0);
        private static readonly object Gate = new object();
        private static readonly List<InstallItem> Pending = new List<InstallItem>();
        private static bool _started;
        private static System.Windows.Threading.Dispatcher _ui;

        public static void Init(System.Windows.Threading.Dispatcher ui)
        {
            _ui = ui;
            if (_started) return;
            _started = true;
            Engine.StateChanged += () => { if (Engine.IsRunning) Signal.Release(); };
            _ = Task.Run(Worker);
        }

        public static bool IsApk(string path) =>
            !string.IsNullOrEmpty(path) && (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".apks", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xapk", StringComparison.OrdinalIgnoreCase));

        public static InstallItem Add(string file, string source)
        {
            if (!File.Exists(file)) return null;
            var item = new InstallItem { File = file, Size = new FileInfo(file).Length, Source = source, Status = InstallStatus.Waiting };
            if (!Engine.IsRunning) item.Message = "Waits for Android to start";
            OnUi(() => Items.Insert(0, item));
            lock (Gate) Pending.Add(item);
            Notify(item);
            Signal.Release();
            return item;
        }

        public static void ClearFinished() => OnUi(() =>
        {
            foreach (var i in Items.Where(i => i.Status == InstallStatus.Installed || i.Status == InstallStatus.Failed).ToList()) Items.Remove(i);
        });

        private static void OnUi(Action a)
        {
            if (_ui == null || _ui.CheckAccess()) a();
            else _ui.Invoke(a);
        }

        private static void Notify(InstallItem i)
        {
            try { OnUi(() => Changed?.Invoke(i)); } catch { }
        }

        private static async Task Worker()
        {
            while (true)
            {
                await Signal.WaitAsync();
                while (Engine.IsRunning)
                {
                    InstallItem item;
                    lock (Gate)
                    {
                        item = Pending.FirstOrDefault();
                        if (item == null) break;
                        Pending.RemoveAt(0);
                    }
                    await Run(item);
                }
            }
        }

        private static async Task Run(InstallItem item)
        {
            item.Status = InstallStatus.Installing;
            item.Message = "Installing";
            Notify(item);
            try
            {
                string error;
                if (item.File.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    error = await Adb.Install(item.File);
                else
                    error = await InstallBundle(item.File);

                if (error == null)
                {
                    item.Package = await Adb.MostRecentlyUpdated();
                    item.Status = InstallStatus.Installed;
                    item.Message = "Installed";
                    Notify(item);
                    if (Settings.Current.OpenAfterInstall && !string.IsNullOrEmpty(item.Package))
                        await Adb.Launch(item.Package);
                }
                else
                {
                    item.Status = InstallStatus.Failed;
                    item.Message = error;
                    Notify(item);
                }
            }
            catch (Exception ex)
            {
                item.Status = InstallStatus.Failed;
                item.Message = ex.Message;
                Log.Write("Install failed: " + ex);
                Notify(item);
            }
        }

        /// <summary>.apks and .xapk files are zips of split APKs. Unpack the APKs and install them together.</summary>
        private static async Task<string> InstallBundle(string file)
        {
            string dir = Path.Combine(Paths.Cache, "bundle-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(dir);
                var apks = new List<string>();
                using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
                {
                    foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)))
                    {
                        string dest = Path.Combine(dir, Path.GetFileName(e.FullName));
                        e.ExtractToFile(dest, true);
                        apks.Add(dest);
                    }
                }
                if (apks.Count == 0) return "This bundle doesn't contain any APKs.";
                string args = "install-multiple -r -g -t " + string.Join(" ", apks.Select(a => "\"" + a + "\""));
                var (code, o) = await Adb.Run(args, 10 * 60 * 1000);
                Log.Write("Install bundle " + Path.GetFileName(file) + ": " + o.Trim());
                return code == 0 && o.Contains("Success") ? null : Adb.Explain(o);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>Picks up APKs handed over by a second copy of TeleTV (double-clicked .apk files).</summary>
        public static void TakePendingFromOtherInstances()
        {
            try
            {
                if (!File.Exists(Paths.PendingInstalls)) return;
                var lines = File.ReadAllLines(Paths.PendingInstalls);
                File.Delete(Paths.PendingInstalls);
                foreach (var l in lines.Distinct()) if (IsApk(l)) Add(l.Trim(), "Opened from Windows");
            }
            catch (Exception ex) { Log.Write("Pending installs: " + ex.Message); }
        }
    }
}
