// Copies one character's setup onto another: settings (profile.json, skill groups, info bar),
// window layout (gumps.xml) and macros (macros.xml). The target's files are backed up first.
//
// Some of a character's files name its own things by serial: the paperdoll and your own health bar
// (the character), a backpack or spellbook window (that character's items), the grab bag. Those are
// moved onto the target's own serials where the target's files know them, and dropped otherwise, so
// the copy never opens the other character's paperdoll or tries to open its backpack.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace ClassicUO.IEX.Launcher
{
    sealed class CharacterProfile
    {
        public string Account, Name, Folder;
        public override string ToString() => Name + "  (" + Account + ")";
    }

    static class ProfileCopy
    {
        const int Container = 2, HealthBar = 4, PaperDoll = 9, SpellBook = 11; // ClassicUO's GumpType

        static readonly string[] SettingsFiles = { "profile.json", "skillsgroups.xml", "infobar.xml" };
        static readonly string[] LayoutFiles = { "gumps.xml" };
        static readonly string[] MacroFiles = { "macros.xml" };

        /// <summary>Every character on this install that has played on the shard, most recent first.</summary>
        public static List<CharacterProfile> Characters(string profilesRoot, string shardName)
        {
            var list = new List<(CharacterProfile C, DateTime When)>();
            try
            {
                foreach (var accountDir in Directory.GetDirectories(profilesRoot))
                {
                    var shardDir = Path.Combine(accountDir, shardName);
                    if (!Directory.Exists(shardDir))
                        continue;
                    foreach (var charDir in Directory.GetDirectories(shardDir))
                    {
                        var profile = Path.Combine(charDir, "profile.json");
                        if (!File.Exists(profile))
                            continue;
                        list.Add((new CharacterProfile { Account = Path.GetFileName(accountDir), Name = Path.GetFileName(charDir), Folder = charDir }, File.GetLastWriteTimeUtc(profile)));
                    }
                }
            }
            catch { }
            return list.OrderByDescending(t => t.When).Select(t => t.C).ToList();
        }

        /// <summary>Copies the chosen parts and returns the folder holding the target's old files.</summary>
        public static string Copy(CharacterProfile from, CharacterProfile to, bool settings, bool layout, bool macros)
        {
            if (from.Folder.Equals(to.Folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pick two different characters.");

            var files = new List<string>();
            if (settings) files.AddRange(SettingsFiles);
            if (layout) files.AddRange(LayoutFiles);
            if (macros) files.AddRange(MacroFiles);
            if (files.Count == 0)
                throw new InvalidOperationException("Tick at least one thing to copy.");

            // back up everything the copy may touch, before touching anything
            var backup = Path.Combine(to.Folder, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            foreach (var f in files)
            {
                var existing = Path.Combine(to.Folder, f);
                if (File.Exists(existing))
                    File.Copy(existing, Path.Combine(backup, f), true);
            }

            foreach (var f in files)
            {
                var src = Path.Combine(from.Folder, f);
                var dst = Path.Combine(to.Folder, f);
                if (!File.Exists(src))
                    continue;

                if (f == "profile.json")
                    WriteProfile(src, dst);
                else if (f == "gumps.xml")
                    WriteLayout(src, Path.Combine(backup, f), dst);
                else
                    File.Copy(src, dst, true);
            }
            return backup;
        }

        // The grab bag is one of the character's own containers: the target keeps its own.
        static void WriteProfile(string src, string dst)
        {
            var text = File.ReadAllText(src);
            var mine = File.Exists(dst) ? Regex.Match(File.ReadAllText(dst), "\"grab_bag_serial\"\\s*:\\s*(\\d+)") : Match.Empty;
            text = Regex.Replace(text, "(\"grab_bag_serial\"\\s*:\\s*)\\d+", m => m.Groups[1].Value + (mine.Success ? mine.Groups[1].Value : "0"));
            File.WriteAllText(dst, text, new System.Text.UTF8Encoding(false));
        }

        static void WriteLayout(string src, string targetOld, string dst)
        {
            var doc = new XmlDocument();
            doc.Load(src);

            // what the source and the target files tell about each character's own serials
            uint fromPlayer = PlayerSerial(doc);
            uint toPlayer = 0;
            var toItems = new Dictionary<string, string>(); // "type/graphic" -> serial of the target's own window
            if (File.Exists(targetOld))
            {
                try
                {
                    var old = new XmlDocument();
                    old.Load(targetOld);
                    toPlayer = PlayerSerial(old);
                    foreach (XmlElement g in old.GetElementsByTagName("gump"))
                    {
                        int type = Int(g.GetAttribute("type"));
                        if (type == Container || type == SpellBook)
                            toItems[type + "/" + g.GetAttribute("graphic")] = g.GetAttribute("serial");
                    }
                }
                catch { }
            }

            foreach (XmlElement g in doc.GetElementsByTagName("gump").Cast<XmlElement>().ToList())
            {
                if (!uint.TryParse(g.GetAttribute("serial"), out var serial) || serial == 0)
                    continue;

                int type = Int(g.GetAttribute("type"));
                string replacement = null;

                if ((type == PaperDoll || type == HealthBar) && serial == fromPlayer && toPlayer != 0)
                    replacement = toPlayer.ToString();
                else if ((type == Container || type == SpellBook) && toItems.TryGetValue(type + "/" + g.GetAttribute("graphic"), out var item))
                    replacement = item;

                if (replacement != null)
                    g.SetAttribute("serial", replacement);
                else
                    g.ParentNode.RemoveChild(g); // the source character's own thing, or someone it was watching
            }

            doc.Save(dst);
        }

        static uint PlayerSerial(XmlDocument doc)
        {
            foreach (XmlElement g in doc.GetElementsByTagName("gump"))
                if (Int(g.GetAttribute("type")) == PaperDoll && uint.TryParse(g.GetAttribute("serial"), out var s) && s != 0)
                    return s;
            return 0;
        }

        static int Int(string s) => int.TryParse(s, out var v) ? v : -1;
    }
}
