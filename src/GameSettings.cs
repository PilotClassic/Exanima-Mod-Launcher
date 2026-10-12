// Edits the game's own settings file, %APPDATA%\Exanima\Exanima.ini ("Key = Value" lines,
// no sections). Only the values shown in the launcher are changed; every other line is kept.
// Value ranges match what the game's settings menu writes (see Options below).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExanimaLauncher
{
    class GameOption
    {
        public string Key, Label;
        public bool IsBool, IsSlider;
        public int[] Values = new int[0];      // allowed values for a list
        public string[] Names = new string[0]; // display names for Values
        public int Min, Max;                   // slider range
    }

    class GameSettings
    {
        public static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Exanima", "Exanima.ini");

        public static readonly GameOption[] Options =
        {
            Bool("Fullscreen", "Fullscreen"),
            Bool("VerticalSync", "Vertical sync"),
            Bool("ShowFPS", "Show FPS"),
            List("TextureQuality", "Texture quality", new[] { 2, 3, 4, 5 }, "Low", "Medium", "High", "Ultra"),
            List("TextureDetail", "Texture detail", new[] { 2, 3 }, "Normal", "High"),
            List("ShadingQuality", "Shading quality", new[] { 3, 4, 5, 6 }, "Low", "Medium", "High", "Ultra"),
            List("ShadowQuality", "Shadow quality", new[] { 1, 2, 3, 4 }, "Low", "Medium", "High", "Ultra"),
            List("ShadowDetail", "Shadow detail", new[] { 4, 5 }, "Normal", "High"),
            List("HWMultisample", "Anti-aliasing (MSAA)", new[] { 0, 2, 4, 8 }, "Off", "2x", "4x", "8x"),
            List("Supersample", "Supersampling", new[] { 0, 1, 3, 4 }, "Off", "Low", "High", "Max"),
            new GameOption { Key = "Volume", Label = "Volume", IsSlider = true, Min = 0, Max = 100 },
            Bool("Music", "Music"),
        };

        readonly List<string> lines = new List<string>();
        public bool Exists { get; private set; }
        public bool Dirty { get; private set; }

        public GameSettings()
        {
            Exists = File.Exists(Path);
            if (Exists) lines.AddRange(File.ReadAllLines(Path));
        }

        public int? Get(string key)
        {
            int i = Find(key);
            int v;
            if (i >= 0 && int.TryParse(lines[i].Substring(lines[i].IndexOf('=') + 1).Trim(), out v)) return v;
            return null;
        }

        public void Set(string key, int value)
        {
            if (Get(key) == value) return;
            string line = string.Format("{0} = {1}", key, value);
            int i = Find(key);
            if (i >= 0) lines[i] = line; else lines.Add(line);
            Dirty = true;
        }

        public void Save()
        {
            if (!Dirty || !Exists) return;
            File.WriteAllLines(Path, lines);
            Dirty = false;
        }

        int Find(string key)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq > 0 && string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        static GameOption Bool(string key, string label)
        {
            return new GameOption { Key = key, Label = label, IsBool = true };
        }

        static GameOption List(string key, string label, int[] values, params string[] names)
        {
            return new GameOption { Key = key, Label = label, Values = values, Names = names };
        }
    }
}
