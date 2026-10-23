using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeleTV.Core
{
    /// <summary>Where TeleTV keeps its files. Everything lives under %LOCALAPPDATA%\TeleTV so no admin rights are needed.</summary>
    public static class Paths
    {
        public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeleTV");

        /// <summary>The Android SDK layout the emulator expects (emulator, platform-tools, system-images).</summary>
        public static string Sdk => Dir(Path.Combine(Root, "sdk"));
        public static string AvdHome => Dir(Path.Combine(Root, "avd"));
        public static string EmulatorHome => Dir(Path.Combine(Root, "home"));
        public static string Downloads => Dir(Path.Combine(Root, "downloads"));
        public static string Cache => Dir(Path.Combine(Root, "cache"));
        public static string Logs => Dir(Path.Combine(Root, "logs"));
        public static string SettingsFile => Path.Combine(Dir(Root), "settings.json");
        public static string PendingInstalls => Path.Combine(Dir(Root), "pending-installs.txt");

        public static string EmulatorExe => Path.Combine(Sdk, "emulator", "emulator.exe");
        public static string AdbExe => Path.Combine(Sdk, "platform-tools", "adb.exe");

        public const string AvdName = "TeleTV";
        public static string AvdDir => Path.Combine(AvdHome, AvdName + ".avd");
        public static string AvdIni => Path.Combine(AvdHome, AvdName + ".ini");

        public static string Dir(string p)
        {
            try { Directory.CreateDirectory(p); } catch { }
            return p;
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();

        public static string File => Path.Combine(Paths.Logs, "teletv.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    var fi = new FileInfo(File);
                    if (fi.Exists && fi.Length > 2_000_000)
                    {
                        string old = File + ".old";
                        if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                        System.IO.File.Move(File, old);
                    }
                    System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>User settings, saved as JSON.</summary>
    public class Settings
    {
        // Android
        /// <summary>"google-tv" (with Play Store) or "android-tv".</summary>
        public string ImageTag { get; set; } = "google-tv";
        /// <summary>The SDK path of the installed system image, e.g. system-images;android-34;google-tv;x86_64.</summary>
        public string InstalledImagePath { get; set; }
        public int InstalledImageRevision { get; set; }
        public int InstalledImageApi { get; set; }
        public string InstalledImageTag { get; set; }
        public string InstalledImageAbi { get; set; }
        public string InstalledEmulatorVersion { get; set; }
        public string InstalledPlatformToolsVersion { get; set; }
        public bool LicenceAccepted { get; set; }
        public bool SetupComplete { get; set; }

        /// <summary>0 means pick automatically from the PC's memory.</summary>
        public int RamMb { get; set; }
        /// <summary>0 means pick automatically.</summary>
        public int Cores { get; set; }
        /// <summary>1080 or 720.</summary>
        public int Resolution { get; set; } = 1080;
        /// <summary>auto, host or software.</summary>
        public string Graphics { get; set; } = "auto";

        // General
        public bool StartFullScreen { get; set; }
        public bool OpenAfterInstall { get; set; } = true;
        public bool FastStart { get; set; } = true;

        // Updates
        public bool AutoCheckUpdates { get; set; } = true;
        public bool BackgroundDownload { get; set; } = true;
        public bool BetaChannel { get; set; }
        public DateTime LastUpdateCheck { get; set; }
        /// <summary>Android packages downloaded and waiting to be applied the next time Android starts.</summary>
        public System.Collections.Generic.List<SdkPackage> PendingSdk { get; set; } = new System.Collections.Generic.List<SdkPackage>();

        // Window
        public double WinLeft { get; set; } = double.NaN;
        public double WinTop { get; set; } = double.NaN;
        public double WinWidth { get; set; } = 1280;
        public double WinHeight { get; set; } = 800;
        public bool WinMaximised { get; set; }

        // Downloader
        public string LastUrl { get; set; } = "";
        public string[] Favourites { get; set; } = Array.Empty<string>();

        [JsonIgnore]
        public static Settings Current { get; private set; } = new Settings();

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static void Load()
        {
            try
            {
                if (System.IO.File.Exists(Paths.SettingsFile))
                    Current = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(Paths.SettingsFile), Opts) ?? new Settings();
            }
            catch (Exception ex)
            {
                Log.Write("Settings unreadable, using defaults: " + ex.Message);
                Current = new Settings();
            }
            Current.Favourites ??= Array.Empty<string>();
            Current.PendingSdk ??= new System.Collections.Generic.List<SdkPackage>();
        }

        public static void Save()
        {
            try
            {
                string tmp = Paths.SettingsFile + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
                System.IO.File.Move(tmp, Paths.SettingsFile, true);
            }
            catch (Exception ex) { Log.Write("Couldn't save settings: " + ex.Message); }
        }
    }

    public static class Format
    {
        public static string Size(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
            if (bytes >= 1L << 10) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }
    }
}
