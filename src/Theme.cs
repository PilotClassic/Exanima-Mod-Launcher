// Colors, fonts and small drawing helpers for the launcher's dark look.
using System.Drawing;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(20, 18, 16);
        public static readonly Color Panel = Color.FromArgb(31, 28, 25);
        public static readonly Color PanelHover = Color.FromArgb(44, 39, 34);
        public static readonly Color Border = Color.FromArgb(62, 54, 45);
        public static readonly Color Text = Color.FromArgb(230, 220, 203);
        public static readonly Color TextBright = Color.FromArgb(255, 246, 228);
        public static readonly Color Disabled = Color.FromArgb(110, 102, 92);
        public static readonly Color AccentDark = Color.FromArgb(92, 70, 38);
        public static readonly Color Muted = Color.FromArgb(150, 140, 126);
        public static readonly Color Accent = Color.FromArgb(176, 141, 87);
        public static readonly Color AccentHover = Color.FromArgb(200, 162, 104);
        public static readonly Color Selection = Color.FromArgb(70, 58, 40);
        public static readonly Color Warning = Color.FromArgb(214, 120, 90);

        public static readonly Font Title = new Font("Georgia", 26f, FontStyle.Bold);
        public static readonly Font Subtitle = new Font("Georgia", 10f, FontStyle.Italic);
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font BodyBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font Tab = new Font("Georgia", 11f, FontStyle.Bold);
        public static readonly Font Play = new Font("Georgia", 15f, FontStyle.Bold);

        public static Button FlatButton(string text, bool accent)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, Font = accent ? Play : Body, Cursor = Cursors.Hand,
                BackColor = accent ? Accent : Panel, ForeColor = accent ? Back : Text, UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = accent ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = accent ? AccentHover : PanelHover;
            b.FlatAppearance.MouseDownBackColor = accent ? Accent : Selection;
            return b;
        }

        public static Label Label(string text, Font font, Color color)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent };
        }
    }
}
