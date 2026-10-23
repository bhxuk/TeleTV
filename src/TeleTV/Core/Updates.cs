using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TeleTV.Core
{
    /// <summary>Keeps TeleTV itself (from GitHub releases) and Android (from Google) up to date.</summary>
    public static class Updates
    {
        public const string Repo = "bhxuk/TeleTV";

        public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(1, 0, 0);
        public static string CurrentText => Current.Major + "." + Current.Minor + "." + Current.Build;

        // ---------------- TeleTV ----------------

        public static string LatestVersion { get; private set; }
        public static string LatestNotes { get; private set; } = "";
        public static string LatestSetupUrl { get; private set; }
        public static long LatestSetupSize { get; private set; }
        public static string LatestPage { get; private set; } = "https://github.com/" + Repo + "/releases";
        public static bool AppUpdateAvailable { get; private set; }

        private static HttpClient Client(int seconds)
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("TeleTV/" + CurrentText);
            return h;
        }

        /// <summary>Looks for a newer TeleTV on GitHub. Beta includes pre-releases.</summary>
        public static async Task<bool> CheckApp()
        {
            try
            {
                using var http = Client(20);
                JsonElement rel;
                if (Settings.Current.BetaChannel)
                {
                    using var list = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases?per_page=10"));
                    var first = list.RootElement.EnumerateArray().FirstOrDefault(r => !r.GetProperty("draft").GetBoolean());
                    if (first.ValueKind == JsonValueKind.Undefined) return false;
                    rel = first.Clone();
                }
                else
                {
                    using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest"));
                    rel = doc.RootElement.Clone();
                }

                string tag = rel.GetProperty("tag_name").GetString() ?? "";
                LatestVersion = tag.TrimStart('v', 'V');
                LatestPage = rel.TryGetProperty("html_url", out var hu) ? hu.GetString() : LatestPage;
                LatestNotes = rel.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                LatestSetupUrl = null;
                if (rel.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        if ((a.GetProperty("name").GetString() ?? "").Equals("TeleTV-Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            LatestSetupUrl = a.GetProperty("browser_download_url").GetString();
                            LatestSetupSize = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        }
                Settings.Current.LastUpdateCheck = DateTime.Now;
                Settings.Save();
                AppUpdateAvailable = LatestSetupUrl != null && Version.TryParse(LatestVersion, out var v)
                                     && v > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
                return AppUpdateAvailable;
            }
            catch (Exception ex)
            {
                Log.Write("Update check failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Short "what's new" lines from the release notes, skipping the download boilerplate.</summary>
        public static List<string> NoteLines() => LatestNotes.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => l.Substring(2).Replace("**", ""))
            .Take(6).ToList();

        /// <summary>Downloads the new Setup.exe, closes Android cleanly and runs the installer, which restarts TeleTV.</summary>
        public static async Task<string> InstallApp(IProgress<double> progress)
        {
            try
            {
                if (LatestSetupUrl == null && !await CheckApp()) return "No update found.";
                using var http = Client(600);
                string dir = Paths.Dir(Path.Combine(Path.GetTempPath(), "TeleTV"));
                string path = Path.Combine(dir, "TeleTV-Setup-" + LatestVersion + ".exe");
                using (var resp = await http.GetAsync(LatestSetupUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? LatestSetupSize;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(path);
                    var buf = new byte[1 << 16];
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        done += n;
                        if (total > 0) progress?.Report(done / (double)total);
                    }
                }
                await Engine.Stop();
                Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("App update failed: " + ex.Message);
                return "Couldn't update: " + ex.Message;
            }
        }

        // ---------------- Android ----------------

        public static List<SdkPackage> SdkAvailable { get; private set; } = new List<SdkPackage>();

        /// <summary>Compares what's installed with Google's newest packages.</summary>
        public static async Task<List<SdkPackage>> CheckSdk(CancellationToken ct = default)
        {
            var s = Settings.Current;
            if (!s.SetupComplete) return new List<SdkPackage>();
            var plan = await Sdk.Plan(s.ImageTag, ct);
            var list = plan.All.Where(p => !Sdk.IsInstalled(p)).ToList();
            // Don't offer an older image or a different API level unless the person switched between Google TV and Android TV.
            list.RemoveAll(p => p.Api > 0 && p.Tag == s.InstalledImageTag && p.Api < s.InstalledImageApi);
            SdkAvailable = list;
            return list;
        }

        public static bool SdkPending => Settings.Current.PendingSdk.Count > 0;

        /// <summary>Downloads Android updates now; they're applied the next time Android starts.</summary>
        public static async Task DownloadSdk(IEnumerable<SdkPackage> packages, IProgress<Sdk.Progress> progress, CancellationToken ct)
        {
            foreach (var p in packages)
            {
                await Sdk.Download(p, progress, ct);
                var s = Settings.Current;
                s.PendingSdk.RemoveAll(x => x.Path == p.Path || (p.Api > 0 && x.Api > 0));
                s.PendingSdk.Add(p);
                Settings.Save();
            }
        }

        /// <summary>Installs downloaded Android updates. Called while Android is stopped, just before it starts.</summary>
        public static async Task ApplyPendingSdk()
        {
            var s = Settings.Current;
            if (s.PendingSdk.Count == 0) return;
            foreach (var p in s.PendingSdk.ToList())
            {
                try
                {
                    string name = Path.GetFileName(new Uri(p.Url).AbsolutePath);
                    string zip = Path.Combine(Paths.Cache, name);
                    if (!File.Exists(zip)) zip = await Sdk.Download(p, null, CancellationToken.None);
                    string oldImage = s.InstalledImagePath;
                    string oldFolder = oldImage == null ? null : Sdk.ImageFolder(new SdkPackage { Api = s.InstalledImageApi, Tag = s.InstalledImageTag, Abi = s.InstalledImageAbi });
                    await Sdk.Install(p, zip, null, CancellationToken.None);
                    if (p.Api > 0)
                    {
                        if (oldImage != p.Path)
                        {
                            Android.FactoryReset(); // a different Android can't keep the old data
                            if (oldFolder != null && Directory.Exists(oldFolder)) try { Directory.Delete(oldFolder, true); } catch { }
                        }
                        else Android.DropQuickStart();
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Couldn't apply " + p.Path + ": " + ex.Message);
                }
                s.PendingSdk.Remove(p);
                Settings.Save();
            }
        }
    }
}
