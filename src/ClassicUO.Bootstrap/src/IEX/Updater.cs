// In Ex Xen auto-updater. Runs in the bootstrap before cuo.dll is loaded, so every file the
// installer ships (client, Razor, UO data) can be replaced, including this exe.
//
// Manifest (UTF-8 text, one entry per line, tab separated), published on each GitHub release:
//   version <tab> 1.1.0.0
//   file <tab> sha256-hex <tab> size <tab> relative/path <tab> https://.../asset.gz
// Each asset is the gzip of the file. Unchanged files keep pointing at the release that first
// shipped them. Files not in the manifest (settings.json, profiles, Razor settings) are never touched.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClassicUO.IEX
{
    static class Updater
    {
        const string ManifestUrl = "https://github.com/JilliansSoftware/InExXenclient/releases/latest/download/manifest.txt";
        const string CacheFile = "iexupdate.cache";
        const string LogFile = "Logs/update.log";
        const string OldSuffix = ".iexold";
        const string TempSuffix = ".iexdl";

        sealed class Entry
        {
            public string Hash, Path, Url;
            public long Size;
        }

        // Shared between the worker thread and the progress window's timer.
        static volatile string _status = "Checking for updates...";
        static volatile int _percent = -1;
        static volatile bool _done;

        static string _root;

        // Testing only: IEX_UPDATE_MANIFEST=http://127.0.0.1:8000/manifest.txt points the updater at a
        // local server and also allows http://127.0.0.1 asset URLs.
        static readonly string _testManifest = Environment.GetEnvironmentVariable("IEX_UPDATE_MANIFEST");

        /// <summary>Returns true when the updated exe was relaunched and this process should exit.</summary>
        public static bool Run(string[] args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return false;

            _root = AppContext.BaseDirectory;

            if (args.Any(a => a.Equals("-noupdate", StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(_root, "noupdate")))
                return false;

            DeleteLeftovers();

            // Several clients can start at once (multi-boxing); only one updates, the rest wait.
            using var mutex = new Mutex(false, "InExXenClientUpdater");
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromMinutes(10)))
                    return false;
            }
            catch (AbandonedMutexException) { }

            try
            {
                bool relaunch = false;
                var worker = new Thread(() =>
                {
                    try { relaunch = Update(args); }
                    catch (Exception e) { Log("update failed: " + e); }
                    finally { _done = true; }
                }) { IsBackground = true, Name = "IEX updater" };
                worker.Start();

                // Most launches finish the check instantly; only show a window when there is work.
                if (!worker.Join(700))
                {
                    // WinForms wants an STA thread; the bootstrap's main thread is MTA.
                    var ui = new Thread(ShowProgressWindow) { Name = "IEX updater UI" };
                    ui.SetApartmentState(ApartmentState.STA);
                    ui.Start();
                    ui.Join();
                }

                worker.Join();
                return relaunch;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        static bool Update(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            string text;
            try
            {
                text = DownloadString(string.IsNullOrEmpty(_testManifest) ? ManifestUrl : _testManifest);
            }
            catch (Exception e)
            {
                Log("manifest unavailable, starting without update: " + e.Message);
                return false;
            }

            var entries = ParseManifest(text);
            if (entries.Count == 0)
                return false;

            _status = "Checking game files...";
            var cache = LoadCache();
            var needed = new List<Entry>();

            for (int i = 0; i < entries.Count; i++)
            {
                _percent = i * 100 / entries.Count;
                var e = entries[i];
                if (!Matches(e, cache))
                    needed.Add(e);
            }
            SaveCache(cache);

            if (needed.Count == 0)
                return false;

            long total = needed.Sum(e => e.Size), doneBytes = 0;
            Log($"updating {needed.Count} file(s), {total} bytes");

            // Download everything first; nothing is replaced until all files verified.
            try
            {
                foreach (var e in needed)
                {
                    _status = $"Downloading {e.Path} ({needed.IndexOf(e) + 1} of {needed.Count})";
                    DownloadVerified(e, doneBytes, total);
                    doneBytes += e.Size;
                }
            }
            catch
            {
                foreach (var e in needed)
                    TryDelete(Full(e.Path) + TempSuffix);
                throw;
            }

            _status = "Installing update...";
            _percent = 100;
            var swapped = new List<Entry>();
            try
            {
                foreach (var e in needed)
                {
                    var target = Full(e.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                    {
                        TryDelete(target + OldSuffix);
                        File.Move(target, target + OldSuffix); // renaming works even for the running exe
                    }
                    File.Move(target + TempSuffix, target);
                    swapped.Add(e);
                }
            }
            catch (Exception ex)
            {
                // Roll back so the install stays consistent, e.g. when another client holds a file.
                Log("install failed, rolling back: " + ex);
                foreach (var e in swapped)
                {
                    var target = Full(e.Path);
                    TryDelete(target);
                    if (File.Exists(target + OldSuffix))
                        File.Move(target + OldSuffix, target);
                }
                foreach (var e in needed)
                    TryDelete(Full(e.Path) + TempSuffix);
                _status = "Close other In Ex Xen windows to finish updating.";
                Thread.Sleep(3000);
                return false;
            }

            foreach (var e in needed)
            {
                var info = new FileInfo(Full(e.Path));
                cache[e.Path] = new CacheEntry { Size = info.Length, Ticks = info.LastWriteTimeUtc.Ticks, Hash = e.Hash };
            }
            SaveCache(cache);
            Log("update installed");

            var self = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            if (needed.Any(e => e.Path.Equals(self, StringComparison.OrdinalIgnoreCase)))
            {
                Process.Start(new ProcessStartInfo(Full(self), string.Join(" ", args.Select(Quote)))
                {
                    WorkingDirectory = _root,
                    UseShellExecute = false
                });
                return true;
            }

            return false;
        }

        static List<Entry> ParseManifest(string text)
        {
            var list = new List<Entry>();
            foreach (var raw in text.Split('\n'))
            {
                var parts = raw.TrimEnd('\r').Split('\t');
                if (parts.Length != 5 || parts[0] != "file")
                    continue;

                var path = parts[3].Replace('\\', '/');
                // Never write outside the install folder.
                if (path.Length == 0 || path.StartsWith("/") || path.Contains(":") || path.Split('/').Any(s => s == ".." || s == "."))
                    continue;
                if (!long.TryParse(parts[2], out var size) || parts[1].Length != 64 || !(parts[4].StartsWith("https://") || (!string.IsNullOrEmpty(_testManifest) && parts[4].StartsWith("http://127.0.0.1"))))
                    continue;

                list.Add(new Entry { Hash = parts[1].ToLowerInvariant(), Size = size, Path = path, Url = parts[4] });
            }
            return list;
        }

        static bool Matches(Entry e, Dictionary<string, CacheEntry> cache)
        {
            var file = new FileInfo(Full(e.Path));
            if (!file.Exists || file.Length != e.Size)
                return false;

            var ticks = file.LastWriteTimeUtc.Ticks;
            if (!cache.TryGetValue(e.Path, out var c) || c.Size != file.Length || c.Ticks != ticks)
            {
                c = new CacheEntry { Size = file.Length, Ticks = ticks, Hash = HashFile(file.FullName) };
                cache[e.Path] = c;
            }
            return c.Hash == e.Hash;
        }

        static void DownloadVerified(Entry e, long doneBefore, long total)
        {
            var temp = Full(e.Path) + TempSuffix;
            Directory.CreateDirectory(Path.GetDirectoryName(temp));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var sha = SHA256.Create())
                    {
                        var request = (HttpWebRequest)WebRequest.Create(e.Url);
                        request.UserAgent = "InExXenClient-Updater";
                        request.Timeout = 30000;
                        request.ReadWriteTimeout = 60000;

                        using (var response = request.GetResponse())
                        using (var gz = new GZipStream(response.GetResponseStream(), CompressionMode.Decompress))
                        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                        {
                            var buffer = new byte[81920];
                            long written = 0;
                            int read;
                            while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, read);
                                sha.TransformBlock(buffer, 0, read, null, 0);
                                written += read;
                                if (total > 0)
                                    _percent = (int)((doneBefore + written) * 100 / total);
                            }
                            sha.TransformFinalBlock(buffer, 0, 0);

                            if (written != e.Size || Hex(sha.Hash) != e.Hash)
                                throw new InvalidDataException($"{e.Path}: downloaded file does not match the manifest");
                        }
                    }
                    return;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    Log($"download attempt {attempt} for {e.Path} failed: {ex.Message}");
                    Thread.Sleep(1000 * attempt);
                }
            }
        }

        static string DownloadString(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "InExXenClient-Updater";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        sealed class CacheEntry
        {
            public long Size, Ticks;
            public string Hash;
        }

        static Dictionary<string, CacheEntry> LoadCache()
        {
            var cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(_root, CacheFile);
                if (!File.Exists(path))
                    return cache;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split('\t');
                    if (p.Length == 4 && long.TryParse(p[1], out var size) && long.TryParse(p[2], out var ticks))
                        cache[p[0]] = new CacheEntry { Size = size, Ticks = ticks, Hash = p[3] };
                }
            }
            catch (Exception e) { Log("cache unreadable: " + e.Message); }
            return cache;
        }

        static void SaveCache(Dictionary<string, CacheEntry> cache)
        {
            try
            {
                File.WriteAllLines(Path.Combine(_root, CacheFile),
                    cache.Select(kv => $"{kv.Key}\t{kv.Value.Size}\t{kv.Value.Ticks}\t{kv.Value.Hash}"));
            }
            catch (Exception e) { Log("cache not saved: " + e.Message); }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                return Hex(sha.ComputeHash(stream));
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static string Full(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        static string Quote(string arg) => arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

        static void DeleteLeftovers()
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(_root, "*" + OldSuffix, SearchOption.AllDirectories))
                    TryDelete(f); // still in use by another running client: removed next launch
            }
            catch { }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        static void Log(string message)
        {
            try
            {
                var path = Path.Combine(_root, LogFile);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { }
        }

        static void ShowProgressWindow()
        {
            if (_done)
                return;

            Application.EnableVisualStyles();

            var form = new Form
            {
                Text = "In Ex Xen",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = true,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(460, 90),
                Font = new Font("Segoe UI", 9f)
            };
            try { form.Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); } catch { }

            var label = new Label { Left = 16, Top = 14, Width = 428, Height = 22, AutoEllipsis = true };
            var bar = new ProgressBar { Left = 16, Top = 44, Width = 428, Height = 22, Maximum = 100 };
            form.Controls.Add(label);
            form.Controls.Add(bar);

            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (s, e) =>
            {
                label.Text = _status;
                var p = _percent;
                bar.Style = p < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                if (p >= 0)
                    bar.Value = Math.Max(0, Math.Min(100, p));
                if (_done)
                {
                    timer.Stop();
                    form.Close();
                }
            };
            timer.Start();

            Application.Run(form);
        }
    }
}
