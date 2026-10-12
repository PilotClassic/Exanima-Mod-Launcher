// Self-update of the launcher from GitHub releases.
//
//   Repository: ExanimaLauncher.ini next to the launcher, [Update] Repo=owner/name. While it is
//   empty the launcher never goes online.
//   Release: the repository's latest GitHub release (drafts and pre-releases are ignored). Its
//   tag is the version (v1.2.0 or 1.2.0); it carries the asset ExanimaLauncher.exe and,
//   optionally, ExanimaLauncher.exe.sha256 (checked when present; build.ps1 writes it).
//   Installing: the new file is downloaded next to the launcher and checked; then the running
//   launcher renames itself to ExanimaLauncher.exe.old (Windows allows renaming a running
//   program), the new file takes its name, and the new launcher starts with the same
//   arguments. The .old file is deleted on the next start.
//   Skipped versions are remembered in %LOCALAPPDATA%\ExanimaLauncher\update.ini.
//
// Only the launcher updates itself; the mod loader and the mods are updated by hand.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag, Title, Notes, PageUrl;
        public string ExeUrl, ShaUrl;
        public long ExeSize;
    }

    static class Updater
    {
        const string ExeName = "ExanimaLauncher.exe";

        public static Version Current
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string CurrentText
        {
            get { var v = Current; return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        }

        static string ExePath { get { return Application.ExecutablePath; } }
        static string ConfigPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "ExanimaLauncher.ini"); } }
        static string StatePath { get { return Path.Combine(GameAssets.CacheDir, "update.ini"); } }

        // owner/name from ExanimaLauncher.ini, or null.
        public static string Repository
        {
            get
            {
                string repo = ReadIni(ConfigPath, "Repo");
                if (string.IsNullOrEmpty(repo)) return null;
                repo = repo.Trim().Trim('/');
                var parts = repo.Split('/');
                bool ok = parts.Length == 2 && parts.All(p => p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'));
                return ok ? repo : null;
            }
        }

        // GitHub's API, or for testing a local stand-in (ApiBase=http://localhost:port in the ini;
        // plain http is only accepted for localhost).
        static string ApiBase
        {
            get
            {
                string b = ReadIni(ConfigPath, "ApiBase");
                Uri u;
                if (!string.IsNullOrEmpty(b) && Uri.TryCreate(b.TrimEnd('/'), UriKind.Absolute, out u) &&
                    (u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback)))
                    return b.TrimEnd('/');
                return "https://api.github.com";
            }
        }

        // Deletes what an earlier update left behind.
        public static void CleanUp()
        {
            foreach (string leftover in new[] { ExePath + ".old", ExePath + ".download" })
                try { if (File.Exists(leftover)) File.Delete(leftover); } catch { }
        }

        // Asks GitHub for the latest release. Returns null when there is nothing newer (or the
        // check failed; then `error` says why). Runs on a background thread.
        public static ReleaseInfo Check(out string error)
        {
            error = null;
            string repo = Repository;
            if (repo == null) return null;
            try
            {
                string json;
                using (var response = Request(ApiBase + "/repos/" + repo + "/releases/latest", "application/vnd.github+json").GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
                var release = new JavaScriptSerializer { MaxJsonLength = 8 << 20 }.DeserializeObject(json) as Dictionary<string, object>;
                if (release == null) { error = "unexpected answer from GitHub"; return null; }

                var info = new ReleaseInfo
                {
                    Tag = Str(release, "tag_name"), Title = Str(release, "name"), Notes = Str(release, "body"),
                    PageUrl = Str(release, "html_url"),
                };
                info.Version = ParseVersion(info.Tag);
                if (info.Version == null) { error = "the latest release's tag (" + info.Tag + ") is not a version"; return null; }
                var assets = release.ContainsKey("assets") ? release["assets"] as object[] : null;
                foreach (var a in (assets ?? new object[0]).OfType<Dictionary<string, object>>())
                {
                    string name = Str(a, "name"), url = Str(a, "browser_download_url");
                    if (string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.ExeUrl = url;
                        info.ExeSize = a.ContainsKey("size") ? Convert.ToInt64(a["size"]) : 0;
                    }
                    else if (string.Equals(name, ExeName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        info.ShaUrl = url;
                }
                if (info.Version <= Normalize(Current)) return null;
                if (info.ExeUrl == null) { error = "release " + info.Tag + " has no " + ExeName; return null; }
                if (!Secure(info.ExeUrl) || (info.ShaUrl != null && !Secure(info.ShaUrl))) { error = "the release's download is not https"; return null; }
                if (string.Equals(ReadIni(StatePath, "Skip"), info.Version.ToString(), StringComparison.OrdinalIgnoreCase)) return null;
                return info;
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                error = http != null && http.StatusCode == HttpStatusCode.NotFound ? "no release found in " + repo : ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        // One line per check in %LOCALAPPDATA%\ExanimaLauncher\update.log (kept short).
        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(GameAssets.CacheDir);
                string path = Path.Combine(GameAssets.CacheDir, "update.log");
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
                File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Count - 50)));
            }
            catch { }
        }

        public static void Skip(ReleaseInfo info)
        {
            try
            {
                Directory.CreateDirectory(GameAssets.CacheDir);
                File.WriteAllText(StatePath, "[Update]\r\nSkip=" + info.Version + "\r\n");
            }
            catch { }
        }

        // Downloads and checks the new launcher next to the running one. Background thread.
        // Returns the downloaded file's path; throws with a readable message on failure.
        public static string Download(ReleaseInfo info, Action<int> progress)
        {
            string target = ExePath + ".download";
            using (var response = Request(info.ExeUrl, "application/octet-stream").GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = File.Create(target))
            {
                long total = info.ExeSize > 0 ? info.ExeSize : response.ContentLength, done = 0;
                var buffer = new byte[65536];
                int n, last = -1;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last && progress != null) progress(last = pct);
                }
            }

            var file = new FileInfo(target);
            byte[] head = new byte[2];
            using (var f = file.OpenRead()) f.Read(head, 0, 2);
            if (head[0] != 'M' || head[1] != 'Z') Fail(target, "the download is not a program");
            if (info.ExeSize > 0 && file.Length != info.ExeSize) Fail(target, "the download is incomplete");
            if (info.ShaUrl != null)
            {
                string expected;
                using (var response = Request(info.ShaUrl, "text/plain").GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    expected = reader.ReadToEnd().Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                string actual;
                using (var sha = SHA256.Create())
                using (var f = file.OpenRead())
                    actual = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (actual != expected) Fail(target, "the download does not match the release's checksum");
            }
            return target;
        }

        // Puts the downloaded launcher in place and starts it. UI thread; the caller exits after.
        public static void Install(string downloaded, string[] args)
        {
            string exe = ExePath, old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(downloaded, exe);
            }
            catch
            {
                File.Move(old, exe);   // put the running launcher back
                throw;
            }
            Process.Start(new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) { UseShellExecute = false });
        }

        public static bool GameRunning
        {
            get { return Process.GetProcessesByName("Exanima").Length > 0; }
        }

        // ------------------------------------------------------------------ helpers

        static HttpWebRequest Request(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "ExanimaLauncher/" + CurrentText;
            r.Accept = accept;
            r.Timeout = 15000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        static bool Secure(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback));
        }

        static void Fail(string file, string why)
        {
            try { File.Delete(file); } catch { }
            throw new InvalidDataException(why);
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        // "v1.2.0", "1.2", "launcher-1.2.3" -> 1.2.0 / 1.2.0 / 1.2.3
        static Version ParseVersion(string tag)
        {
            int start = 0;
            while (start < tag.Length && !char.IsDigit(tag[start])) start++;
            int end = start;
            while (end < tag.Length && (char.IsDigit(tag[end]) || tag[end] == '.')) end++;
            Version v;
            return Version.TryParse(tag.Substring(start, end - start).TrimEnd('.'), out v) ? Normalize(v) : null;
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        static string Quote(string a)
        {
            return a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        static string ReadIni(string path, string key)
        {
            try
            {
                if (!File.Exists(path)) return null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return null;
        }
    }
}
