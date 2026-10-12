// Exanima launcher: Play, mod manager and game settings.
// Lives in the game folder next to Exanima.exe. To have Steam's Play button open it, set the
// game's launch options to:  "<game folder>\ExanimaLauncher.exe" %command%
using System;
using System.IO;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // From Steam, the game's path is among the arguments; otherwise use the launcher's folder.
            string gameDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            int i = Array.FindIndex(args, a => a.EndsWith("Exanima.exe", StringComparison.OrdinalIgnoreCase));
            if (i >= 0) gameDir = Path.GetDirectoryName(Path.GetFullPath(args[i]));

            if (!File.Exists(Path.Combine(gameDir, "Exanima.exe")))
            {
                MessageBox.Show("Exanima.exe was not found next to the launcher.\n\nPut ExanimaLauncher.exe in the Exanima game folder.",
                                "Exanima Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(new LauncherForm(gameDir, args));
        }
    }
}
