// "A new launcher is available" dialog: version, release notes, Update / Skip this version /
// Later. Update downloads in the background, then replaces the launcher and restarts it.
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    class UpdateDialog : Form
    {
        readonly ReleaseInfo release;
        readonly string[] args;
        readonly Label message;
        readonly Button update, skip, later;

        public UpdateDialog(ReleaseInfo release, string[] args)
        {
            this.release = release;
            this.args = args;
            Text = "Launcher update";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 340);

            var title = Theme.Label("Exanima Launcher " + release.Version.ToString(3) + " is available", Theme.Tab, Theme.TextBright);
            title.Location = new Point(16, 14);
            message = Theme.Label("You have version " + Updater.CurrentText + ". Your mods and settings are kept.", Theme.Body, Theme.Muted);
            message.AutoSize = false;
            message.Location = new Point(16, 44);
            message.Size = new Size(448, 22);
            message.AutoEllipsis = true;

            var notes = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Panel, ForeColor = Theme.Text, Location = new Point(16, 72), Size = new Size(448, 200),
                Text = string.IsNullOrEmpty(release.Notes) ? "(no release notes)" : release.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            };

            update = Theme.FlatButton("Update", true);
            update.Size = new Size(110, 32);
            update.Location = new Point(354, 292);
            update.Click += delegate { StartUpdate(); };
            later = Theme.FlatButton("Later", false);
            later.Size = new Size(96, 32);
            later.Location = new Point(250, 292);
            later.Click += delegate { Close(); };
            skip = Theme.FlatButton("Skip this version", false);
            skip.Size = new Size(150, 32);
            skip.Location = new Point(16, 292);
            skip.Click += delegate { Updater.Skip(release); Close(); };

            Controls.AddRange(new Control[] { title, message, notes, update, later, skip });
            AcceptButton = update;
            CancelButton = later;
        }

        void StartUpdate()
        {
            if (Updater.GameRunning)
            {
                message.Text = "Close the game first, then press Update again.";
                message.ForeColor = Theme.Warning;
                return;
            }
            update.Enabled = skip.Enabled = later.Enabled = false;
            message.ForeColor = Theme.Muted;
            message.Text = "Downloading...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null, error = null;
                try
                {
                    file = Updater.Download(release, pct => BeginInvoke((Action)delegate { message.Text = "Downloading... " + pct + "%"; }));
                }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke((Action)delegate { Finish(file, error); });
            });
        }

        void Finish(string file, string error)
        {
            if (error == null)
            {
                try
                {
                    Updater.Install(file, args);
                    DialogResult = DialogResult.OK;   // the new launcher is starting; the caller closes this one
                    return;
                }
                catch (Exception ex) { error = ex.Message; }
            }
            message.Text = "The update failed: " + error;
            message.ForeColor = Theme.Warning;
            later.Enabled = skip.Enabled = true;
            later.Text = "Close";
        }
    }
}
