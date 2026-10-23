using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TeleTV.Core
{
    /// <summary>Runs a command-line tool hidden and collects what it prints.</summary>
    public static class Proc
    {
        public static async Task<(int code, string output)> Run(string exe, string args, int timeoutMs = 60000, CancellationToken ct = default, IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            Android.ApplyEnvironment(psi);
            if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

            using var p = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                if (ct.IsCancellationRequested) throw;
                lock (sb) return (-1, sb.ToString() + "\n(timed out)");
            }
            p.WaitForExit(); // flush the output readers
            lock (sb) return (p.ExitCode, sb.ToString());
        }
    }

    /// <summary>Checks the PC can run Android at full speed, and switches on Windows Hypervisor Platform when it can't.</summary>
    public static class Hypervisor
    {
        public enum State { Ready, NeedsWindowsFeature, NeedsBios, Unknown }

        public static string Detail { get; private set; } = "";

        public static async Task<State> Check()
        {
            if (!File.Exists(Paths.EmulatorExe)) return State.Unknown;
            var (code, output) = await Proc.Run(Paths.EmulatorExe, "-accel-check", 30000);
            Detail = output.Replace("accel:", "").Trim();
            Log.Write("accel-check (" + code + "): " + Detail.Replace(Environment.NewLine, " | "));
            if (code == 0) return State.Ready;
            string o = output.ToLowerInvariant();
            if (o.Contains("bios") || o.Contains("vt-x") || o.Contains("svm") || o.Contains("virtualization is disabled") || o.Contains("not supported"))
                return State.NeedsBios;
            return State.NeedsWindowsFeature;
        }

        /// <summary>Turns on Windows Hypervisor Platform. Asks for admin rights; a restart finishes it.</summary>
        public static async Task<bool> EnableWindowsFeature()
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" +
                    "Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All -NoRestart | Out-Null; " +
                    "Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform -All -NoRestart | Out-Null\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                await p.WaitForExitAsync();
                Log.Write("Enable Hypervisor Platform exit " + p.ExitCode);
                return p.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false; // the admin prompt was declined
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't enable Hypervisor Platform: " + ex.Message);
                return false;
            }
        }

        public static void RestartWindows()
        {
            try { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Restarting to finish setting up TeleTV\"") { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { Log.Write("Restart failed: " + ex.Message); }
        }
    }

    public static class Android
    {
        public static void ApplyEnvironment(ProcessStartInfo psi)
        {
            psi.Environment["ANDROID_SDK_ROOT"] = Paths.Sdk;
            psi.Environment["ANDROID_HOME"] = Paths.Sdk;
            psi.Environment["ANDROID_AVD_HOME"] = Paths.AvdHome;
        }

        public static int AutoRamMb()
        {
            long total = 0;
            try { total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); } catch { }
            if (total >= 15000) return 4096;
            if (total >= 7500) return 3072;
            return 2048;
        }

        public static int AutoCores() => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

        public static int RamMb => Settings.Current.RamMb > 0 ? Settings.Current.RamMb : AutoRamMb();
        public static int Cores => Settings.Current.Cores > 0 ? Settings.Current.Cores : AutoCores();
        public static (int w, int h, int dpi) Screen => Settings.Current.Resolution == 720 ? (1280, 720, 213) : (1920, 1080, 320);

        /// <summary>Writes the Android TV virtual device (AVD) files. Safe to call before every start: settings changes take effect.</summary>
        public static void WriteAvd()
        {
            var s = Settings.Current;
            if (string.IsNullOrEmpty(s.InstalledImagePath)) throw new InvalidOperationException("Android TV isn't installed yet.");
            Directory.CreateDirectory(Paths.AvdDir);
            var (w, h, dpi) = Screen;
            bool google = s.InstalledImageTag == "google-tv";
            string sysdir = $"system-images\\android-{s.InstalledImageApi}\\{s.InstalledImageTag}\\{s.InstalledImageAbi}\\";
            string gpu = s.Graphics switch { "host" => "host", "software" => "swiftshader_indirect", _ => "auto" };

            var cfg = new List<string>
            {
                "AvdId=" + Paths.AvdName,
                "avd.ini.displayname=TeleTV",
                "avd.ini.encoding=UTF-8",
                "PlayStore.enabled=" + (google ? "true" : "false"),
                "abi.type=" + s.InstalledImageAbi,
                "hw.cpu.arch=" + s.InstalledImageAbi,
                "hw.cpu.ncore=" + Cores,
                "hw.ramSize=" + RamMb,
                "vm.heapSize=256",
                "disk.dataPartition.size=16G",
                "hw.lcd.width=" + w,
                "hw.lcd.height=" + h,
                "hw.lcd.density=" + dpi,
                "hw.initialOrientation=landscape",
                "hw.device.name=" + (h == 720 ? "tv_720p" : "tv_1080p"),
                "hw.device.manufacturer=Google",
                "hw.keyboard=yes",
                "hw.dPad=yes",
                "hw.mainKeys=yes",
                "hw.trackBall=no",
                "hw.audioInput=no",
                "hw.audioOutput=yes",
                "hw.battery=no",
                "hw.accelerometer=no",
                "hw.gyroscope=no",
                "hw.sensors.orientation=no",
                "hw.sensors.proximity=no",
                "hw.gps=no",
                "hw.camera.back=none",
                "hw.camera.front=none",
                "hw.sdCard=no",
                "hw.gpu.enabled=yes",
                "hw.gpu.mode=" + gpu,
                "fastboot.forceColdBoot=" + (s.FastStart ? "no" : "yes"),
                "fastboot.forceFastBoot=" + (s.FastStart ? "yes" : "no"),
                "image.sysdir.1=" + sysdir,
                "tag.id=" + s.InstalledImageTag,
                "tag.display=" + (google ? "Google TV" : "Android TV"),
                "runtime.network.latency=none",
                "runtime.network.speed=full",
                "showDeviceFrame=no",
                "skin.dynamic=yes",
                "skin.name=" + w + "x" + h,
                "skin.path=_no_skin"
            };
            File.WriteAllLines(Path.Combine(Paths.AvdDir, "config.ini"), cfg);
            File.WriteAllLines(Paths.AvdIni, new[]
            {
                "avd.ini.encoding=UTF-8",
                "path=" + Paths.AvdDir,
                "target=android-" + s.InstalledImageApi
            });
        }

        /// <summary>Removes the saved quick-start state, so the next start is a full boot (needed after a system update or setting change).</summary>
        public static void DropQuickStart()
        {
            try
            {
                string snaps = Path.Combine(Paths.AvdDir, "snapshots");
                if (Directory.Exists(snaps)) Directory.Delete(snaps, true);
            }
            catch (Exception ex) { Log.Write("Couldn't remove quick-start state: " + ex.Message); }
        }

        /// <summary>Erases apps and data inside Android, like a factory reset. The system image is kept.</summary>
        public static void FactoryReset()
        {
            DropQuickStart();
            foreach (var f in new[] { "userdata-qemu.img", "userdata-qemu.img.qcow2", "cache.img", "cache.img.qcow2", "encryptionkey.img", "encryptionkey.img.qcow2", "sdcard.img" })
                try { File.Delete(Path.Combine(Paths.AvdDir, f)); } catch { }
        }

        /// <summary>adb only finds emulators on console ports 5554 to 5584 by itself, so pick one in that range.</summary>
        public static int FreeConsolePort()
        {
            foreach (int p in new[] { 5580, 5582, 5584 }.Concat(Enumerable.Range(0, 13).Select(i => 5554 + i * 2)))
                if (IsFree(p) && IsFree(p + 1)) return p;
            return 5580;
        }

        public static int FreePort(int start)
        {
            for (int p = start; p < start + 200; p += 2)
            {
                if (IsFree(p) && IsFree(p + 1)) return p;
            }
            return start;
        }

        public static bool IsFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>adb: installs and manages apps inside Android.</summary>
    public static class Adb
    {
        public static string Serial { get; set; } = "emulator-5580";

        public static Task<(int code, string output)> Run(string args, int timeoutMs = 60000, CancellationToken ct = default) =>
            Proc.Run(Paths.AdbExe, "-s " + Serial + " " + args, timeoutMs, ct);

        public static Task StartServer() => Proc.Run(Paths.AdbExe, "start-server", 30000);

        public static async Task<bool> IsBooted()
        {
            var (code, o) = await Run("shell getprop sys.boot_completed", 8000);
            return code == 0 && o.Trim() == "1";
        }

        public static async Task<List<string>> ThirdPartyPackages()
        {
            var (_, o) = await Run("shell pm list packages -3", 20000);
            return o.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("package:"))
                    .Select(l => l.Substring(8)).OrderBy(x => x).ToList();
        }

        public class AppInfo
        {
            public string Package;
            public string Version;
            public DateTime Updated;
            public string Name => FriendlyName(Package);
        }

        public static string FriendlyName(string package)
        {
            var parts = (package ?? "").Split('.').Where(p => p.Length > 0 && !new[] { "com", "org", "net", "tv", "app", "android", "io", "me", "uk", "co" }.Contains(p)).ToList();
            string n = parts.Count > 0 ? parts.Last() : package;
            if (parts.Count > 1 && new[] { "tv", "android", "mobile", "app", "free", "pro", "player" }.Contains(n.ToLowerInvariant())) n = parts[^2] + " " + n;
            n = n.Replace('_', ' ').Replace('-', ' ');
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : package;
        }

        public static async Task<List<AppInfo>> Apps()
        {
            var pkgs = await ThirdPartyPackages();
            var result = pkgs.Select(p => new AppInfo { Package = p }).ToList();
            var (_, dump) = await Run("shell dumpsys package packages", 30000);
            AppInfo cur = null;
            foreach (var raw in dump.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("Package [") && l.Contains("]"))
                {
                    string name = l.Substring(9, l.IndexOf(']') - 9);
                    cur = result.FirstOrDefault(a => a.Package == name);
                }
                else if (cur != null && l.StartsWith("versionName=")) cur.Version ??= l.Substring(12);
                else if (cur != null && l.StartsWith("lastUpdateTime=") && DateTime.TryParse(l.Substring(15), out var t)) cur.Updated = t;
            }
            return result;
        }

        public static async Task<string> MostRecentlyUpdated()
        {
            var apps = await Apps();
            return apps.OrderByDescending(a => a.Updated).FirstOrDefault()?.Package;
        }

        /// <summary>Installs an APK. Returns null on success or a plain-English reason.</summary>
        public static async Task<string> Install(string apk, CancellationToken ct = default)
        {
            var (code, o) = await Run("install -r -g -t \"" + apk + "\"", 10 * 60 * 1000, ct);
            Log.Write("Install " + Path.GetFileName(apk) + ": " + o.Trim().Replace(Environment.NewLine, " | "));
            if (code == 0 && o.Contains("Success")) return null;
            return Explain(o);
        }

        public static string Explain(string output)
        {
            string o = output ?? "";
            if (o.Contains("INSTALL_FAILED_NO_MATCHING_ABIS")) return "This app is built for a different kind of processor (usually ARM-only) and can't run here.";
            if (o.Contains("INSTALL_FAILED_OLDER_SDK")) return "This app needs a newer version of Android.";
            if (o.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return "Android is out of storage. Remove some apps and try again.";
            if (o.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || o.Contains("INSTALL_FAILED_VERSION_DOWNGRADE"))
                return "A different version of this app is already installed. Uninstall it first in Apps.";
            if (o.Contains("INSTALL_PARSE_FAILED") || o.Contains("INSTALL_FAILED_INVALID_APK")) return "This file isn't a valid APK, or it's damaged.";
            if (o.Contains("INSTALL_FAILED_MISSING_SHARED_LIBRARY")) return "This app needs parts of Android that Android TV doesn't have.";
            if (o.Contains("device offline") || o.Contains("not found")) return "Android isn't running.";
            string last = o.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "Unknown error";
            return "Android refused the install: " + last;
        }

        /// <summary>Opens an app, preferring its TV launcher entry.</summary>
        public static async Task<bool> Launch(string package)
        {
            var (_, o) = await Run("shell monkey -p " + package + " -c android.intent.category.LEANBACK_LAUNCHER 1", 20000);
            if (!o.Contains("No activities found") && !o.Contains("aborted")) return true;
            (_, o) = await Run("shell monkey -p " + package + " -c android.intent.category.LAUNCHER 1", 20000);
            return !o.Contains("No activities found") && !o.Contains("aborted");
        }

        public static Task Uninstall(string package) => Run("uninstall " + package, 60000);
        public static Task ClearData(string package) => Run("shell pm clear " + package, 30000);
        public static Task ForceStop(string package) => Run("shell am force-stop " + package, 15000);
        public static Task Home() => Run("shell input keyevent KEYCODE_HOME", 10000);

        public static Task Push(string local, string remote) => Run("push \"" + local + "\" \"" + remote + "\"", 30 * 60 * 1000);
    }
}
