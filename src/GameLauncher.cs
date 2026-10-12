// Starts Exanima.exe.
//  - Started by Steam with the launch option  "<path>\ExanimaLauncher.exe" %command%,
//    the launcher's arguments are Steam's game command line; it is run as given, so Steam
//    arguments such as +connect_lobby <id> reach the game.
//  - Started on its own, the launcher runs Exanima.exe from its own folder with SteamAppId
//    set (no steam_appid.txt needed), starting Steam first if it is not running.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ExanimaLauncher
{
    static class GameLauncher
    {
        public const string AppId = "362490";

        public static bool GameRunning()
        {
            return Process.GetProcessesByName("Exanima").Length > 0;
        }

        // Splits the launcher's own arguments into the game exe and the arguments for it.
        public static void ResolveCommand(string gameDir, string[] args, out string exe, out string gameArgs, out bool fromSteam)
        {
            int i = Array.FindIndex(args, a => a.EndsWith("Exanima.exe", StringComparison.OrdinalIgnoreCase));
            fromSteam = i >= 0;
            exe = fromSteam ? args[i] : Path.Combine(gameDir, "Exanima.exe");
            gameArgs = JoinArgs(fromSteam ? args.Skip(i + 1) : args);
        }

        // Returns null on success, or a message describing why the game could not start.
        public static string Launch(string gameDir, string[] args, Action<string> status)
        {
            string exe, gameArgs;
            bool fromSteam;
            ResolveCommand(gameDir, args, out exe, out gameArgs, out fromSteam);
            if (!File.Exists(exe)) return "Exanima.exe was not found:\n" + exe;

            if (!fromSteam && !EnsureSteam(status))
                return "Steam is not running and could not be started. Start Steam and try again.";

            var psi = new ProcessStartInfo(exe, gameArgs)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            if (!psi.EnvironmentVariables.ContainsKey("SteamAppId")) psi.EnvironmentVariables["SteamAppId"] = AppId;
            if (!psi.EnvironmentVariables.ContainsKey("SteamGameId")) psi.EnvironmentVariables["SteamGameId"] = AppId;
            status("Starting Exanima...");
            Process.Start(psi);
            return null;
        }

        static bool EnsureSteam(Action<string> status)
        {
            if (Process.GetProcessesByName("steam").Length > 0) return true;
            string steam = null;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key != null) steam = key.GetValue("SteamExe") as string;
            if (steam == null || !File.Exists(steam)) return false;

            status("Starting Steam...");
            Process.Start(steam, "-silent");
            DateTime until = DateTime.Now.AddSeconds(60);
            while (DateTime.Now < until && Process.GetProcessesByName("steamwebhelper").Length == 0)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(250);
            }
            // give Steam a moment to sign in before the game calls SteamAPI_Init
            DateTime settle = DateTime.Now.AddSeconds(5);
            while (DateTime.Now < settle) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(100); }
            return Process.GetProcessesByName("steam").Length > 0;
        }

        static string JoinArgs(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                bool quote = a.Length == 0 || a.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
                sb.Append(quote ? "\"" + a.Replace("\"", "\\\"") + "\"" : a);
            }
            return sb.ToString();
        }
    }
}
