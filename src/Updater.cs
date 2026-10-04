using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TaskPad
{
    /// Checks GitHub releases, downloads the new TaskPad.exe, verifies its SHA-256 (from the release notes),
    /// swaps it in place (a running exe can be renamed on Windows) and restarts with the same files.
    public static class Updater
    {
        const string Repo = "ddatunashvili/TaskPad";
        const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        static bool _busy, _installed;
        static DispatcherTimer _timer;

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        static string ExePath => Assembly.GetExecutingAssembly().Location;

        public static void CleanupOld()
        {
            try { File.Delete(ExePath + ".old"); } catch { }
        }

        public static void Start()
        {
            if (Workspace.Settings.AutoUpdate == "off") return;
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            first.Tick += (s, e) => { first.Stop(); Check(silent: true); };
            first.Start();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _timer.Tick += (s, e) => Check(silent: true);
            _timer.Start();
        }

        sealed class Release { public Version Version; public string Tag, Url, Sha; }

        public static void Check(bool silent)
        {
            if (_busy || _installed) return;
            _busy = true;
            Task.Run(() => Fetch()).ContinueWith(t =>
            {
                _busy = false;
                var w = Workspace.LastActive;
                if (w == null) return;
                var r = t.IsFaulted ? null : t.Result;
                if (r == null)
                {
                    if (!silent) w.Toast("Couldn't check for updates", "Open releases", OpenReleases);
                    return;
                }
                if (r.Version <= Current)
                {
                    if (!silent) w.Toast($"TaskPad {Short(Current)} is up to date ✓");
                    return;
                }
                if (Workspace.Settings.AutoUpdate == "auto" && silent) Install(r, restartPrompt: true);
                else w.Toast($"TaskPad {r.Tag} is available", "Update now", () => Install(r, restartPrompt: false), seconds: 15);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static Release Fetch()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(Api);
            req.UserAgent = "TaskPad/" + Short(Current);
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            string json;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();

            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"(?<t>[^\"]+)\"").Groups["t"].Value;
            var url = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"(?<u>[^\"]+)\"").Cast<Match>()
                .Select(m => m.Groups["u"].Value).FirstOrDefault(u => u.EndsWith("/TaskPad.exe", StringComparison.OrdinalIgnoreCase));
            var sha = Regex.Match(json, "SHA-256:\\W*(?<h>[0-9a-fA-F]{64})").Groups["h"].Value;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v) || url == null) return null;
            return new Release { Version = Normalize(v), Tag = tag, Url = url, Sha = sha.Length == 64 ? sha.ToLowerInvariant() : null };
        }

        static void Install(Release r, bool restartPrompt)
        {
            var w = Workspace.LastActive;
            w?.Toast($"Downloading TaskPad {r.Tag}…", seconds: 30);
            Task.Run(() =>
            {
                var tmp = Path.Combine(Path.GetTempPath(), "TaskPad-" + r.Tag + ".exe");
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "TaskPad/" + Short(Current);
                    wc.DownloadFile(r.Url, tmp);
                }
                if (r.Sha != null)
                {
                    string got;
                    using (var s = File.OpenRead(tmp)) using (var h = SHA256.Create())
                        got = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                    if (got != r.Sha) { File.Delete(tmp); throw new InvalidDataException("checksum mismatch"); }
                }
                var exe = ExePath;
                var old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);          // allowed while running
                try { File.Copy(tmp, exe); }
                catch { File.Move(old, exe); throw; }
                File.Delete(tmp);
            }).ContinueWith(t =>
            {
                var win = Workspace.LastActive;
                if (t.IsFaulted)
                {
                    var why = t.Exception?.InnerException is UnauthorizedAccessException ? "no write access to the exe folder" : t.Exception?.InnerException?.Message;
                    win?.Toast("Update failed: " + why, "Download page", OpenReleases, seconds: 10);
                    return;
                }
                _installed = true;
                win?.Toast($"TaskPad updated to {r.Tag} in the background" + (restartPrompt ? " — restart now, or it applies next time you open TaskPad" : ""), "Restart", Restart, seconds: restartPrompt ? 15 : 30);
                if (!restartPrompt) Restart();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// Closes every window (saving / asking as usual) and starts the new exe with the same files.
        /// Closes every window without prompts; the session keeps all tabs and unsaved text (used by the installer).
        public static void QuitAll()
        {
            if (Session.Enabled)
            {
                Session.Save();
                Session.Quitting = true;
            }
            foreach (var w in Workspace.Windows.ToList()) w.Close();
            if (Workspace.Windows.Count > 0) { Session.Quitting = false; return; }
            Application.Current.Shutdown();
        }

        public static void Restart()
        {
            var files = Workspace.Docs.Where(d => d.Path != null).Select(d => "\"" + d.Path + "\"").Distinct().ToList();
            if (Session.Enabled)
            {
                // the session (incl. unsaved text) brings everything back; no prompts
                Session.Save();
                Session.Quitting = true;
                files.Clear();
            }
            foreach (var w in Workspace.Windows.ToList()) w.Close();
            if (Workspace.Windows.Count > 0) { Session.Quitting = false; return; } // user cancelled a save prompt
            Program.ReleaseSingleInstance();
            if (Program.Standalone) files.Insert(0, "--standalone");
            Process.Start(new ProcessStartInfo(ExePath, string.Join(" ", files)) { UseShellExecute = false });
            Application.Current.Shutdown();
        }

        public static void OpenReleases()
        {
            try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); } catch { }
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        public static string Short(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }
}
