using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TeleTV.Core
{
    /// <summary>One downloadable package from Google's Android SDK repository.</summary>
    public class SdkPackage
    {
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public int Revision { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Checksum { get; set; }
        public string ChecksumType { get; set; } = "sha1";
        public string LicenceId { get; set; }

        // System images only
        public int Api { get; set; }
        public string Tag { get; set; }
        public string Abi { get; set; }
    }

    public class SdkPlan
    {
        public SdkPackage Emulator { get; set; }
        public SdkPackage PlatformTools { get; set; }
        public SdkPackage Image { get; set; }
        public Dictionary<string, string> Licences { get; } = new Dictionary<string, string>();

        public IEnumerable<SdkPackage> All => new[] { Emulator, PlatformTools, Image }.Where(p => p != null);
        public long TotalSize => All.Sum(p => p.Size);

        /// <summary>The licence text for every package in the plan, one after another.</summary>
        public string LicenceText
        {
            get
            {
                var ids = All.Select(p => p.LicenceId).Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
                if (ids.Count == 0) ids = Licences.Keys.Where(k => k == "android-sdk-license").ToList();
                return string.Join("\n\n----------\n\n", ids.Where(Licences.ContainsKey).Select(i => Licences[i].Trim()));
            }
        }
    }

    /// <summary>Reads Google's Android SDK repository and downloads the emulator, adb and the TV system image straight from Google.</summary>
    public static class Sdk
    {
        private const string Base = "https://dl.google.com/android/repository/";
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("TeleTV/" + Updates.CurrentText);
            return h;
        }

        private static async Task<XDocument> GetXml(string url, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(40));
            string text = await Http.GetStringAsync(url, cts.Token);
            return XDocument.Parse(text);
        }

        private static async Task<(XDocument doc, string url)> FirstXml(IEnumerable<string> urls, CancellationToken ct)
        {
            Exception last = null;
            foreach (var u in urls)
            {
                try { return (await GetXml(u, ct), u); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { last = ex; }
            }
            throw new Exception("Couldn't reach Google's Android download server. Check the internet connection. (" + last?.Message + ")");
        }

        private static IEnumerable<XElement> Named(XContainer x, string local) => x.Descendants().Where(e => e.Name.LocalName == local);
        private static XElement Child(XElement x, string local) => x?.Elements().FirstOrDefault(e => e.Name.LocalName == local);

        private static void ReadLicences(XDocument doc, SdkPlan plan)
        {
            foreach (var l in doc.Root.Elements().Where(e => e.Name.LocalName == "license"))
            {
                string id = (string)l.Attribute("id");
                if (!string.IsNullOrEmpty(id) && !plan.Licences.ContainsKey(id)) plan.Licences[id] = l.Value;
            }
        }

        private static bool IsStable(XElement pkg)
        {
            var ch = Child(pkg, "channelRef");
            return ch == null || ((string)ch.Attribute("ref") ?? "channel-0") == "channel-0";
        }

        private static int Major(XElement pkg) => int.TryParse(Child(Child(pkg, "revision"), "major")?.Value, out int m) ? m : 0;

        private static string VersionText(XElement pkg)
        {
            var r = Child(pkg, "revision");
            if (r == null) return "";
            string s = Child(r, "major")?.Value ?? "0";
            if (Child(r, "minor") != null) s += "." + Child(r, "minor").Value;
            if (Child(r, "micro") != null) s += "." + Child(r, "micro").Value;
            return s;
        }

        private static long VersionKey(XElement pkg)
        {
            var r = Child(pkg, "revision");
            long Get(string n) => long.TryParse(Child(r, n)?.Value, out long v) ? v : 0;
            return Get("major") * 1_000_000_000L + Get("minor") * 1_000_000L + Get("micro") * 1000L + Get("preview");
        }

        /// <summary>Picks the archive for Windows x64 (or the host-neutral one for system images).</summary>
        private static SdkPackage ToPackage(XElement pkg, string baseUrl)
        {
            XElement chosen = null;
            foreach (var a in Named(pkg, "archive"))
            {
                string os = Child(a, "host-os")?.Value;
                string arch = Child(a, "host-arch")?.Value;
                if (os != null && os != "windows") continue;
                if (arch != null && arch != "x64") continue;
                chosen = a;
                if (os == "windows") break;
            }
            if (chosen == null) return null;
            var complete = Child(chosen, "complete");
            if (complete == null) return null;
            var sum = Child(complete, "checksum");
            string url = Child(complete, "url")?.Value?.Trim();
            if (string.IsNullOrEmpty(url)) return null;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = baseUrl + url;
            return new SdkPackage
            {
                Path = (string)pkg.Attribute("path"),
                DisplayName = Child(pkg, "display-name")?.Value ?? (string)pkg.Attribute("path"),
                Version = VersionText(pkg),
                Revision = Major(pkg),
                Url = url,
                Size = long.TryParse(Child(complete, "size")?.Value, out long sz) ? sz : -1,
                Checksum = sum?.Value?.Trim().ToLowerInvariant(),
                ChecksumType = ((string)sum?.Attribute("type") ?? "sha1").ToLowerInvariant(),
                LicenceId = (string)Child(pkg, "uses-license")?.Attribute("ref")
            };
        }

        private static string BaseOf(string url) => url.Substring(0, url.LastIndexOf('/') + 1);

        /// <summary>Works out what to download: the newest stable emulator, platform-tools and TV system image.</summary>
        public static async Task<SdkPlan> Plan(string preferredTag, CancellationToken ct)
        {
            var plan = new SdkPlan();

            var (main, mainUrl) = await FirstXml(new[] { Base + "repository2-3.xml", Base + "repository2-2.xml", Base + "repository2-1.xml" }, ct);
            ReadLicences(main, plan);
            var packages = main.Root.Elements().Where(e => e.Name.LocalName == "remotePackage").ToList();

            XElement Newest(string path) => packages
                .Where(p => (string)p.Attribute("path") == path && IsStable(p) && ToPackage(p, BaseOf(mainUrl)) != null)
                .OrderByDescending(VersionKey).FirstOrDefault();

            var emu = Newest("emulator");
            var pt = Newest("platform-tools");
            if (emu == null || pt == null) throw new Exception("Google's download list didn't include the Android emulator for Windows.");
            plan.Emulator = ToPackage(emu, BaseOf(mainUrl));
            plan.PlatformTools = ToPackage(pt, BaseOf(mainUrl));

            // Find the TV system image catalogues from the add-ons list, with known fallbacks.
            var sites = new List<string>();
            try
            {
                var (addons, addonsUrl) = await FirstXml(new[] { Base + "addons_list-5.xml", Base + "addons_list-4.xml", Base + "addons_list-3.xml" }, ct);
                foreach (var s in Named(addons, "site"))
                {
                    string u = Child(s, "url")?.Value?.Trim();
                    string name = Child(s, "displayName")?.Value ?? "";
                    if (string.IsNullOrEmpty(u)) continue;
                    if (u.IndexOf("tv", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("TV", StringComparison.Ordinal) < 0) continue;
                    sites.Add(u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : BaseOf(addonsUrl) + u);
                }
            }
            catch (Exception ex) { Log.Write("Add-ons list unavailable: " + ex.Message); }
            foreach (var f in new[] { "sys-img/google-tv/sys-img2-3.xml", "sys-img/android-tv/sys-img2-3.xml", "sys-img/android-tv/sys-img2-1.xml" })
                if (!sites.Any(s => s.EndsWith(f, StringComparison.OrdinalIgnoreCase))) sites.Add(Base + f);

            var images = new List<SdkPackage>();
            foreach (var site in sites.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                XDocument doc;
                try { doc = await GetXml(site, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { continue; }
                ReadLicences(doc, plan);
                foreach (var p in doc.Root.Elements().Where(e => e.Name.LocalName == "remotePackage"))
                {
                    if (!IsStable(p)) continue;
                    string path = (string)p.Attribute("path") ?? "";
                    var parts = path.Split(';');
                    if (parts.Length != 4 || parts[0] != "system-images") continue;
                    string apiPart = parts[1].StartsWith("android-") ? parts[1].Substring(8) : parts[1];
                    if (!int.TryParse(apiPart, out int api)) continue; // skip previews and extension images
                    string tag = parts[2], abi = parts[3];
                    if (tag != "google-tv" && tag != "android-tv") continue;
                    if (abi != "x86_64" && abi != "x86") continue;
                    var pkg = ToPackage(p, BaseOf(site));
                    if (pkg == null) continue;
                    pkg.Api = api; pkg.Tag = tag; pkg.Abi = abi;
                    if (images.Any(i => i.Path == pkg.Path && i.Revision >= pkg.Revision)) continue;
                    images.RemoveAll(i => i.Path == pkg.Path);
                    images.Add(pkg);
                }
            }

            SdkPackage Best(string tag) => images.Where(i => i.Tag == tag)
                .OrderByDescending(i => i.Api).ThenByDescending(i => i.Abi == "x86_64").ThenByDescending(i => i.Revision).FirstOrDefault();

            plan.Image = Best(preferredTag) ?? Best(preferredTag == "google-tv" ? "android-tv" : "google-tv");
            if (plan.Image == null) throw new Exception("Google's download list didn't include an Android TV system image.");
            Log.Write($"Plan: emulator {plan.Emulator.Version}, platform-tools {plan.PlatformTools.Version}, image {plan.Image.Path} r{plan.Image.Revision}");
            return plan;
        }

        public static string ImageFolder(SdkPackage image) =>
            System.IO.Path.Combine(Paths.Sdk, "system-images", "android-" + image.Api, image.Tag, image.Abi);

        public static bool IsInstalled(SdkPackage p)
        {
            var s = Settings.Current;
            if (p.Path == "emulator") return File.Exists(Paths.EmulatorExe) && s.InstalledEmulatorVersion == p.Version;
            if (p.Path == "platform-tools") return File.Exists(Paths.AdbExe) && s.InstalledPlatformToolsVersion == p.Version;
            return s.InstalledImagePath == p.Path && s.InstalledImageRevision == p.Revision && File.Exists(System.IO.Path.Combine(ImageFolder(p), "system.img"));
        }

        public class Progress
        {
            public string Step;
            public long Done;
            public long Total;
            public double Fraction => Total > 0 ? Math.Min(1, Done / (double)Total) : 0;
        }

        /// <summary>Downloads a package into the cache (resuming a partial download) and checks its checksum.</summary>
        public static async Task<string> Download(SdkPackage p, IProgress<Progress> progress, CancellationToken ct)
        {
            string name = System.IO.Path.GetFileName(new Uri(p.Url).AbsolutePath);
            string file = System.IO.Path.Combine(Paths.Cache, name);
            string part = file + ".part";

            if (File.Exists(file) && (p.Size <= 0 || new FileInfo(file).Length == p.Size) && await Verify(file, p, ct))
                return file;
            if (File.Exists(file)) File.Delete(file);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (p.Size > 0 && have > p.Size) { File.Delete(part); have = 0; }
                    using var req = new HttpRequestMessage(HttpMethod.Get, p.Url);
                    if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (have > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;
                    resp.EnsureSuccessStatusCode();
                    long total = p.Size > 0 ? p.Size : (resp.Content.Headers.ContentLength ?? -1) + have;

                    using (var src = await resp.Content.ReadAsStreamAsync(ct))
                    using (var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        var buf = new byte[1 << 20];
                        long done = have;
                        var last = DateTime.MinValue;
                        int n;
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        while (true)
                        {
                            idle.CancelAfter(TimeSpan.FromSeconds(60)); // stalled connection
                            n = await src.ReadAsync(buf, 0, buf.Length, idle.Token);
                            if (n <= 0) break;
                            await dst.WriteAsync(buf, 0, n, ct);
                            done += n;
                            if ((DateTime.UtcNow - last).TotalMilliseconds > 150)
                            {
                                last = DateTime.UtcNow;
                                progress?.Report(new Progress { Step = "Downloading " + p.DisplayName, Done = done, Total = total });
                            }
                        }
                    }
                    if (p.Size > 0 && new FileInfo(part).Length != p.Size)
                        throw new IOException("The download finished early.");
                    progress?.Report(new Progress { Step = "Checking " + p.DisplayName, Done = 1, Total = 1 });
                    if (!await Verify(part, p, ct))
                    {
                        File.Delete(part);
                        throw new IOException("The download of " + p.DisplayName + " was damaged.");
                    }
                    File.Move(part, file, true);
                    return file;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (attempt < 4)
                {
                    Log.Write($"Download of {p.Path} failed (try {attempt}): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
                }
            }
        }

        private static async Task<bool> Verify(string file, SdkPackage p, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(p.Checksum)) return true;
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            byte[] hash = p.ChecksumType.Contains("256")
                ? await SHA256.HashDataAsync(fs, ct)
                : await SHA1.HashDataAsync(fs, ct);
            return Convert.ToHexString(hash).Equals(p.Checksum, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Unpacks a downloaded zip into the right place in the SDK folder, replacing any older copy.</summary>
        public static async Task Install(SdkPackage p, string zip, IProgress<Progress> progress, CancellationToken ct)
        {
            string target = p.Path switch
            {
                "emulator" => System.IO.Path.Combine(Paths.Sdk, "emulator"),
                "platform-tools" => System.IO.Path.Combine(Paths.Sdk, "platform-tools"),
                _ => ImageFolder(p)
            };
            string temp = System.IO.Path.Combine(Paths.Sdk, ".unpack-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            await Task.Run(() =>
            {
                Directory.CreateDirectory(temp);
                using (var archive = ZipFile.OpenRead(zip))
                {
                    long total = archive.Entries.Sum(e => e.Length), done = 0;
                    var last = DateTime.MinValue;
                    string root = System.IO.Path.GetFullPath(temp) + System.IO.Path.DirectorySeparatorChar;
                    foreach (var e in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(temp, e.FullName));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the folder
                        if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                        done += e.Length;
                        if ((DateTime.UtcNow - last).TotalMilliseconds > 150)
                        {
                            last = DateTime.UtcNow;
                            progress?.Report(new Progress { Step = "Unpacking " + p.DisplayName, Done = done, Total = total });
                        }
                    }
                }

                // Zips hold a single top folder (emulator\, platform-tools\, x86_64\). Move its contents into place.
                var dirs = Directory.GetDirectories(temp);
                string source = dirs.Length == 1 && Directory.GetFiles(temp).Length == 0 ? dirs[0] : temp;

                if (Directory.Exists(target))
                {
                    string old = target + ".old-" + DateTime.Now.Ticks;
                    Directory.Move(target, old);
                    try { Directory.Delete(old, true); } catch (Exception ex) { Log.Write("Couldn't remove old files: " + ex.Message); }
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                Directory.Move(source, target);
                if (Directory.Exists(temp)) try { Directory.Delete(temp, true); } catch { }
            }, ct);

            var s = Settings.Current;
            if (p.Path == "emulator") s.InstalledEmulatorVersion = p.Version;
            else if (p.Path == "platform-tools") s.InstalledPlatformToolsVersion = p.Version;
            else
            {
                s.InstalledImagePath = p.Path;
                s.InstalledImageRevision = p.Revision;
                s.InstalledImageApi = p.Api;
                s.InstalledImageTag = p.Tag;
                s.InstalledImageAbi = p.Abi;
            }
            Settings.Save();
            try { File.Delete(zip); } catch { }
            Log.Write("Installed " + p.Path + " " + p.Version);
        }

        /// <summary>Free space on the drive that holds TeleTV's files.</summary>
        public static long FreeSpace()
        {
            try { return new DriveInfo(System.IO.Path.GetPathRoot(Paths.Root)).AvailableFreeSpace; }
            catch { return long.MaxValue; }
        }

        public static void ClearLeftovers()
        {
            try
            {
                foreach (var d in Directory.GetDirectories(Paths.Sdk, ".unpack-*")) try { Directory.Delete(d, true); } catch { }
                foreach (var d in Directory.GetDirectories(Paths.Sdk, "*.old-*", SearchOption.AllDirectories)) try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }
    }
}
