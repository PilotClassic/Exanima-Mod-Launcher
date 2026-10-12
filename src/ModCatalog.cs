// Reads and writes the mods folder: mod.ini of each mod, the load order (mods.cfg) and
// per-mod settings (settings.ini). The loader (winmm.dll) reads the same files; see MODDING.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ExanimaLauncher
{
    enum SettingType { Bool, Int, Choice, Text }

    class SettingDef
    {
        public string Key;
        public SettingType Type;
        public string Default;
        public string Label;
        public int Min = int.MinValue, Max = int.MaxValue;   // Int
        public string[] Choices = new string[0];              // Choice

        // "type | default | label | extra" where extra is "min | max" for int, "a,b,c" for choice.
        public static SettingDef Parse(string key, string spec)
        {
            string[] parts = spec.Split('|').Select(p => p.Trim()).ToArray();
            var def = new SettingDef { Key = key, Label = key, Default = "" };
            string type = parts.Length > 0 ? parts[0].ToLowerInvariant() : "text";
            def.Type = type == "bool" ? SettingType.Bool : type == "int" ? SettingType.Int
                     : type == "choice" ? SettingType.Choice : SettingType.Text;
            if (parts.Length > 1) def.Default = parts[1];
            if (parts.Length > 2 && parts[2].Length > 0) def.Label = parts[2];
            if (def.Type == SettingType.Int)
            {
                int v;
                if (parts.Length > 3 && int.TryParse(parts[3], out v)) def.Min = v;
                if (parts.Length > 4 && int.TryParse(parts[4], out v)) def.Max = v;
            }
            if (def.Type == SettingType.Choice && parts.Length > 3)
                def.Choices = parts[3].Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
            return def;
        }
    }

    class ModInfo
    {
        public string Key;          // folder name, or file name for a loose DLL
        public string Dir;          // mod folder (the mods folder for a loose DLL)
        public bool IsFolder;
        public string Name, Version = "", Author = "", Description = "", Dll;
        public int LoaderApi = 1;
        public bool Enabled = true;
        public List<SettingDef> Settings = new List<SettingDef>();

        public bool DllExists { get { return File.Exists(Dll); } }

        public string Status
        {
            get
            {
                if (!DllExists) return "DLL missing";
                if (LoaderApi > ModCatalog.LoaderApi) return "Needs newer loader";
                return Enabled ? "Enabled" : "Disabled";
            }
        }

        public string SettingsPath { get { return Path.Combine(Dir, "settings.ini"); } }

        public Dictionary<string, string> LoadValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> saved;
            if (IsFolder && Ini.Read(SettingsPath).TryGetValue("Settings", out saved))
                foreach (var kv in saved) values[kv.Key] = kv.Value;
            foreach (var s in Settings)
                if (!values.ContainsKey(s.Key)) values[s.Key] = s.Default;
            return values;
        }

        // Unicode with BOM so the loader's GetPrivateProfileStringW reads any characters.
        public void SaveValues(Dictionary<string, string> values)
        {
            var sb = new StringBuilder("[Settings]\r\n");
            foreach (var s in Settings)
            {
                string v;
                if (values.TryGetValue(s.Key, out v)) sb.AppendFormat("{0}={1}\r\n", s.Key, v);
            }
            File.WriteAllText(SettingsPath, sb.ToString(), Encoding.Unicode);
        }
    }

    static class ModCatalog
    {
        public const int LoaderApi = 3;   // loader API shipped with this launcher

        public static List<ModInfo> Load(string modsDir)
        {
            var mods = new List<ModInfo>();
            if (!Directory.Exists(modsDir)) return mods;

            foreach (string dir in Directory.GetDirectories(modsDir))
            {
                string ini = Path.Combine(dir, "mod.ini");
                if (!File.Exists(ini)) continue;
                var sections = Ini.Read(ini);
                Dictionary<string, string> m;
                if (!sections.TryGetValue("Mod", out m)) m = new Dictionary<string, string>();
                string folder = Path.GetFileName(dir);
                var mod = new ModInfo
                {
                    Key = folder, Dir = dir, IsFolder = true,
                    Name = Get(m, "Name", folder), Version = Get(m, "Version", ""),
                    Author = Get(m, "Author", ""), Description = Get(m, "Description", ""),
                    Dll = Path.Combine(dir, Get(m, "Dll", folder + ".dll")),
                };
                int api;
                if (int.TryParse(Get(m, "LoaderApi", "1"), out api)) mod.LoaderApi = api;
                Dictionary<string, string> settings;
                if (sections.TryGetValue("Settings", out settings))
                    foreach (var kv in settings) mod.Settings.Add(SettingDef.Parse(kv.Key, kv.Value));
                mods.Add(mod);
            }

            var folderDlls = new HashSet<string>(mods.Select(x => Path.GetFileName(x.Dll)), StringComparer.OrdinalIgnoreCase);
            foreach (string dll in Directory.GetFiles(modsDir, "*.dll"))
            {
                string file = Path.GetFileName(dll);
                if (folderDlls.Contains(file)) continue;   // the loader ignores these too
                mods.Add(new ModInfo
                {
                    Key = file, Dir = modsDir, IsFolder = false, Name = Path.GetFileNameWithoutExtension(file),
                    Description = "Single DLL without a mod.ini (older mod format).", Dll = dll,
                });
            }

            // mods.cfg: "+Key" enabled, "-Key" disabled, in load order; unlisted mods are enabled
            // and come after the listed ones, alphabetically (same rule as the loader).
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string cfg = Path.Combine(modsDir, "mods.cfg");
            if (File.Exists(cfg))
                foreach (string raw in File.ReadAllLines(cfg))
                {
                    string line = raw.Trim();
                    if (line.Length < 2 || (line[0] != '+' && line[0] != '-')) continue;
                    var mod = mods.FirstOrDefault(x => string.Equals(x.Key, line.Substring(1), StringComparison.OrdinalIgnoreCase));
                    if (mod == null || order.ContainsKey(mod.Key)) continue;
                    mod.Enabled = line[0] == '+';
                    order[mod.Key] = order.Count;
                }
            return mods.OrderBy(x => order.ContainsKey(x.Key) ? order[x.Key] : int.MaxValue)
                       .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void SaveOrder(string modsDir, IEnumerable<ModInfo> mods)
        {
            var lines = mods.Select(m => (m.Enabled ? "+" : "-") + m.Key);
            File.WriteAllLines(Path.Combine(modsDir, "mods.cfg"), lines, new UTF8Encoding(false));
        }

        static string Get(Dictionary<string, string> d, string key, string fallback)
        {
            string v;
            return d.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }
    }

    // Minimal INI reader: [Section] / key = value, ';' and '#' comments.
    static class Ini
    {
        public static Dictionary<string, Dictionary<string, string>> Read(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            var section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            result[""] = section;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (!result.TryGetValue(name, out section))
                        result[name] = section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0) section[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }
    }
}
