// Everything the launcher window reads and writes, kept apart from the drawing:
//  - launcher.cfg   (install folder) the launcher's own choices: skip the window, saved passwords per account
//  - shards.txt     (install folder) the player's other shards
//  - launcher.txt   links and patch notes, fetched from the client repo on GitHub each start and
//                   cached as launcher-cache.txt for when GitHub can't be reached
//  - settings.json and Data/Profiles, read only, for the account list and the shard address

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ClassicUO.IEX.Launcher
{
    sealed class NewsItem
    {
        public string Title, Date;
        public List<string> Lines = new List<string>();
    }

    sealed class LinkItem
    {
        public string Name, Url;
    }

    sealed class ShardEntry
    {
        public string Name = "", Host = "", UoPath = "", ClientVersion = "";
        public int Port = 2593;

        public string SettingsFile => Path.Combine("Shards", Regex.Replace(Name, @"[^A-Za-z0-9_\- ]", "_").Trim() + ".json");
    }

    sealed class Account
    {
        public string Name, LastCharacter;
        /// <summary>Null Name is "another account": the login screen starts empty.</summary>
        public override string ToString() => Name == null ? "Another account\u2026" : string.IsNullOrEmpty(LastCharacter) ? Name : Name + "  \u2014  " + LastCharacter;
    }

    static class LauncherData
    {
        public const string NewsUrl = "https://raw.githubusercontent.com/JilliansSoftware/InExXenclient/inexxen/launcher.txt";
        public const string ShardName = "In Ex Xen";

        static string Root => AppContext.BaseDirectory;
        static string P(string name) => Path.Combine(Root, name);

        // ---- launcher.cfg ---------------------------------------------------------------

        static Dictionary<string, string> _cfg;

        static Dictionary<string, string> Cfg
        {
            get
            {
                if (_cfg != null)
                    return _cfg;
                _cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(P("launcher.cfg")))
                        foreach (var line in File.ReadAllLines(P("launcher.cfg")))
                        {
                            int i = line.IndexOf('=');
                            if (i > 0)
                                _cfg[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                        }
                }
                catch { }
                return _cfg;
            }
        }

        static void SaveCfg()
        {
            try { File.WriteAllLines(P("launcher.cfg"), Cfg.Select(kv => kv.Key + "=" + kv.Value)); } catch { }
        }

        public static bool SkipWindow
        {
            get => Cfg.TryGetValue("skip", out var v) && v == "1";
            set { Cfg["skip"] = value ? "1" : "0"; SaveCfg(); }
        }

        public static string LastAccount
        {
            get => Cfg.TryGetValue("account", out var v) ? v : null;
            set { Cfg["account"] = value ?? ""; SaveCfg(); }
        }

        // ---- settings.json (read only; the client owns it) ------------------------------

        static string _settingsText;
        static string SettingsText => _settingsText ??= SafeRead(P("settings.json"));

        static string JsonString(string text, string key)
        {
            var m = Regex.Match(text ?? "", "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        static int? JsonInt(string text, string key)
        {
            var m = Regex.Match(text ?? "", "\"" + key + "\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : (int?)null;
        }

        public static string SavedUsername => JsonString(SettingsText, "username") ?? "";
        public static string ShardHost => JsonString(SettingsText, "ip") ?? "play.inexxen.com";
        public static int ShardPort => JsonInt(SettingsText, "port") ?? 2593;

        // ---- accounts --------------------------------------------------------------------

        public static string ProfilesRoot
        {
            get
            {
                var profiles = JsonString(SettingsText, "profilespath");
                return string.IsNullOrWhiteSpace(profiles) ? Path.Combine(Root, "Data", "Profiles") : profiles;
            }
        }

        /// <summary>Accounts that have played on this shard from this install, plus the saved one.</summary>
        public static List<Account> Accounts()
        {
            var names = new List<string>();
            var profiles = ProfilesRoot;

            try
            {
                foreach (var dir in Directory.GetDirectories(profiles))
                    if (Directory.Exists(Path.Combine(dir, ShardName)))
                        names.Add(Path.GetFileName(dir));
            }
            catch { }

            var saved = SavedUsername;
            if (saved.Length > 0 && !names.Contains(saved, StringComparer.OrdinalIgnoreCase))
                names.Insert(0, saved);

            var last = SafeRead(Path.Combine(profiles, "lastcharacter.json")) ?? "";
            var list = new List<Account>();
            foreach (var n in names)
            {
                var m = Regex.Match(last, "\\{[^}]*\"AccountName\"\\s*:\\s*\"" + Regex.Escape(n) + "\"[^}]*\"ServerName\"\\s*:\\s*\"" + Regex.Escape(ShardName) + "\"[^}]*\\}", RegexOptions.IgnoreCase);
                list.Add(new Account { Name = n, LastCharacter = m.Success ? JsonString(m.Value, "LastCharacterName") : null });
            }

            // the last one picked here first, else the saved one
            var first = LastAccount ?? saved;
            var pick = list.FindIndex(a => a.Name.Equals(first, StringComparison.OrdinalIgnoreCase));
            if (pick > 0)
            {
                var a = list[pick];
                list.RemoveAt(pick);
                list.Insert(0, a);
            }

            if (list.Count > 0)
                list.Add(new Account()); // log in with one not listed yet
            return list;
        }

        /// <summary>
        /// Command-line options that start the client on this account. The client keeps one saved
        /// password, so the launcher remembers each account's (still encrypted) password itself and
        /// hands the right one over when switching.
        /// </summary>
        public static List<string> AccountArgs(Account account)
        {
            var args = new List<string>();
            var saved = SavedUsername;
            var savedPassword = JsonString(SettingsText, "password");

            if (saved.Length > 0 && !string.IsNullOrEmpty(savedPassword))
            {
                Cfg["pw." + saved.ToLowerInvariant()] = savedPassword;
                SaveCfg();
            }

            if (account == null || account.Name != null && account.Name.Equals(saved, StringComparison.OrdinalIgnoreCase))
                return args;

            if (account.Name == null)
            {
                // an empty value clears the saved name and password for this start
                args.Add("-username");
                args.Add("-password_enc");
                return args;
            }

            args.Add("-username");
            args.Add(account.Name);
            if (Cfg.TryGetValue("pw." + account.Name.ToLowerInvariant(), out var pw) && pw.Length > 0)
            {
                args.Add("-password_enc");
                args.Add(pw);
            }
            return args;
        }

        // ---- other shards ----------------------------------------------------------------

        public static List<ShardEntry> LoadShards()
        {
            var list = new List<ShardEntry>();
            foreach (var line in (SafeRead(P("shards.txt")) ?? "").Split('\n'))
            {
                var p = line.TrimEnd('\r').Split('\t');
                if (p.Length >= 5 && p[0].Length > 0)
                    list.Add(new ShardEntry { Name = p[0], Host = p[1], Port = int.TryParse(p[2], out var port) ? port : 2593, UoPath = p[3], ClientVersion = p[4] });
            }
            return list;
        }

        public static void SaveShards(List<ShardEntry> shards)
        {
            try
            {
                File.WriteAllLines(P("shards.txt"), shards.Select(s => string.Join("\t", Clean(s.Name), Clean(s.Host), s.Port.ToString(), Clean(s.UoPath), Clean(s.ClientVersion))));
            }
            catch { }
        }

        static string Clean(string s) => (s ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", "");

        /// <summary>Command-line options that start the client on another shard with its own settings file.</summary>
        public static List<string> ShardArgs(ShardEntry s)
        {
            try { Directory.CreateDirectory(P("Shards")); } catch { }
            var args = new List<string> { "-settings", s.SettingsFile, "-ip", s.Host, "-port", s.Port.ToString() };
            if (!string.IsNullOrWhiteSpace(s.UoPath))
            {
                args.Add("-uopath");
                args.Add(s.UoPath);
            }
            if (!string.IsNullOrWhiteSpace(s.ClientVersion))
            {
                args.Add("-clientversion");
                args.Add(s.ClientVersion);
            }
            // a new settings file would point at a Razor path this install doesn't have (plugins are under Data/Plugins)
            if (!File.Exists(P(s.SettingsFile)) && File.Exists(P(Path.Combine("Data", "Plugins", "Razor", "Razor.exe"))))
            {
                args.Add("-plugins");
                args.Add("Razor/Razor.exe");
            }
            return args;
        }

        // ---- links and patch notes ---------------------------------------------------------

        /// <summary>Fetches launcher.txt; falls back to the last copy fetched, then to the one installed.</summary>
        public static string FetchNews()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(NewsUrl + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
                request.UserAgent = "InExXenClient-Launcher";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                using var response = request.GetResponse();
                using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                try { File.WriteAllText(P("launcher-cache.txt"), text, Encoding.UTF8); } catch { }
                return text;
            }
            catch
            {
                return SafeRead(P("launcher-cache.txt")) ?? SafeRead(P("launcher.txt")) ?? "";
            }
        }

        /// <summary>
        /// launcher.txt format:
        ///   # comment
        ///   link Discord https://discord.gg/...      (Website, Discord, Wiki, Account get their own icons)
        ///   == Title of an update | 18 Nov
        ///   - a line of the update
        /// </summary>
        public static void ParseNews(string text, List<NewsItem> news, List<LinkItem> links)
        {
            NewsItem cur = null;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("link ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = line.Substring(5).Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 2 && (p[1].StartsWith("https://") || p[1].StartsWith("http://")) && links.Count < 6)
                        links.Add(new LinkItem { Name = p[0].Replace('_', ' '), Url = p[1].Trim() });
                }
                else if (line.StartsWith("=="))
                {
                    var t = line.Substring(2).Trim();
                    int bar = t.LastIndexOf('|');
                    cur = new NewsItem { Title = bar >= 0 ? t.Substring(0, bar).Trim() : t, Date = bar >= 0 ? t.Substring(bar + 1).Trim() : "" };
                    news.Add(cur);
                }
                else if (cur != null)
                {
                    cur.Lines.Add(line.StartsWith("-") ? line.Substring(1).Trim() : line);
                }
            }
        }

        // ---- shard status ------------------------------------------------------------------

        /// <summary>
        /// True when the login port accepts a connection. Only connects and closes (nothing is sent),
        /// once per launch; the server ignores a socket that closes before saying anything.
        /// </summary>
        public static bool? IsOnline(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                var ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(4)))
                    return false;
                client.EndConnect(ar);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch
            {
                return null;
            }
        }

        static string SafeRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
            catch { return null; }
        }
    }
}
