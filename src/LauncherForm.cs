// The launcher window: Mods tab (enable, order, per-mod settings), Settings tab (game
// graphics and audio) and the Play button, drawn with the game's own logo, fonts and
// main-menu background (see GameAssets).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    class LauncherForm : Form
    {
        static readonly Rectangle ContentFrame = new Rectangle(20, 158, 760, 318);

        readonly string gameDir, modsDir;
        readonly string[] args;
        List<ModInfo> mods = new List<ModInfo>();
        readonly GameSettings settings = new GameSettings();

        Bitmap backdrop;
        Panel modsPage, settingsPage;
        GameButton modsTab, settingsTab, settingsButton, upButton, downButton, toggleButton;
        ListView list;
        GameText description, status;

        public LauncherForm(string gameDir, string[] args)
        {
            this.gameDir = gameDir;
            this.args = args;
            modsDir = Path.Combine(gameDir, "mods");
            GameAssets.Load(gameDir);

            Text = "Exanima Launcher " + Updater.CurrentText;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            ClientSize = new Size(800, 560);
            try { Icon = Icon.ExtractAssociatedIcon(Path.Combine(gameDir, "Exanima.exe")); } catch { }

            BuildBackdrop();
            BuildHeader();
            BuildFooter();
            modsPage = Page();
            settingsPage = Page();
            BuildModsPage();
            BuildSettingsPage();
            ShowPage(true);
            ReloadMods();
            FormClosing += delegate { SaveAll(); };
            Shown += delegate { CheckForUpdate(); };
        }

        // ------------------------------------------------------------------ launcher update

        // Asks GitHub in the background (see Updater); offers a newer launcher when there is one.
        void CheckForUpdate()
        {
            Updater.CleanUp();
            if (Updater.Repository == null || Updater.GameRunning) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                ReleaseInfo release = Updater.Check(out error);
                Updater.Log(error != null ? "check failed: " + error
                            : release != null ? "offering " + release.Tag : "up to date (" + Updater.CurrentText + ")");
                if (release == null || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        using (var dialog = new UpdateDialog(release, args))
                            if (dialog.ShowDialog(this) == DialogResult.OK) Close();
                    });
                }
                catch (InvalidOperationException) { }   // the window closed meanwhile
            });
        }

        // ------------------------------------------------------------------ background

        // Pre-renders the background: the game's main-menu scene, darkened toward the edges, the
        // logo, and the translucent content panel. Child controls paint over it transparently.
        void BuildBackdrop()
        {
            backdrop = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var g = Graphics.FromImage(backdrop))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var all = new Rectangle(Point.Empty, ClientSize);
                if (GameAssets.Background != null)
                {
                    // cover: scale to fill, crop the overflow evenly
                    var img = GameAssets.Background;
                    float s = Math.Max(all.Width / (float)img.Width, all.Height / (float)img.Height);
                    float w = img.Width * s, h = img.Height * s;
                    g.DrawImage(img, (all.Width - w) / 2, (all.Height - h) / 2, w, h);
                    using (var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(dim, all);
                }
                else
                {
                    using (var grad = new LinearGradientBrush(all, Color.FromArgb(36, 30, 24), Theme.Back, LinearGradientMode.Vertical))
                        g.FillRectangle(grad, all);
                }
                // darker at the top (logo) and bottom (buttons)
                using (var top = new LinearGradientBrush(new Rectangle(0, 0, all.Width, 170), Color.FromArgb(150, 0, 0, 0),
                                                         Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(top, 0, 0, all.Width, 170);
                using (var bottom = new LinearGradientBrush(new Rectangle(0, all.Height - 100, all.Width, 100), Color.FromArgb(0, 0, 0, 0),
                                                            Color.FromArgb(170, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(bottom, 0, all.Height - 100, all.Width, 100);

                if (GameAssets.Logo != null) g.DrawImage(GameAssets.Logo, 14, 2, 290, 100);
                else Ui.Draw(g, "EXANIMA", TextStyle.Title, 90f, Theme.Accent, new RectangleF(30, 20, 300, 70), StringAlignment.Near);

                using (var panel = new SolidBrush(Color.FromArgb(185, 10, 9, 8))) g.FillRectangle(panel, ContentFrame);
                Ui.Frame(g, ContentFrame, Theme.Text);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
        }

        Panel Page()
        {
            var p = new Panel
            {
                BackColor = Color.Transparent, Location = new Point(ContentFrame.X + 8, ContentFrame.Y + 8),
                Size = new Size(ContentFrame.Width - 16, ContentFrame.Height - 16),
            };
            Controls.Add(p);
            return p;
        }

        // ------------------------------------------------------------------ header and footer

        void BuildHeader()
        {
            bool loader = File.Exists(Path.Combine(gameDir, "winmm.dll"));
            string exe, gameArgs;
            bool fromSteam;
            GameLauncher.ResolveCommand(gameDir, args, out exe, out gameArgs, out fromSteam);
            string info = loader ? "Mod loader installed" : "Mod loader (winmm.dll) missing: mods will not load";
            if (fromSteam) info += " - started from Steam";
            Controls.Add(new GameText
            {
                Text = info, TextSize = Ui.SmallSize, Align = StringAlignment.Far, ForeColor = loader ? Theme.Muted : Theme.Warning,
                Location = new Point(330, 16), Size = new Size(450, 24),
            });
            if (GameAssets.Error != null)
                Controls.Add(new GameText
                {
                    Text = "Game fonts unavailable: " + GameAssets.Error, TextSize = Ui.SmallSize, Align = StringAlignment.Far,
                    ForeColor = Theme.Warning, Location = new Point(330, 40), Size = new Size(450, 24),
                });

            modsTab = new GameButton { Text = "MODS", Tab = true, Location = new Point(28, 112), Size = new Size(120, 42) };
            modsTab.Click += delegate { ShowPage(true); };
            settingsTab = new GameButton { Text = "SETTINGS", Tab = true, Location = new Point(156, 112), Size = new Size(190, 42) };
            settingsTab.Click += delegate { ShowPage(false); };
            Controls.Add(modsTab);
            Controls.Add(settingsTab);
        }

        void BuildFooter()
        {
            var play = new GameButton { Text = "PLAY", Primary = true, Location = new Point(586, 488), Size = new Size(194, 56) };
            play.Click += delegate { Play(); };

            var folder = new GameButton { Text = "Open mods folder", Location = new Point(20, 496), Size = new Size(176, 40) };
            folder.Click += delegate
            {
                Directory.CreateDirectory(modsDir);
                Process.Start("explorer.exe", "\"" + modsDir + "\"");
            };

            status = new GameText { ForeColor = Theme.Muted, Location = new Point(212, 496), Size = new Size(360, 40) };
            Controls.AddRange(new Control[] { play, folder, status });
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !(ActiveControl is ComboBox)) Play(); };
        }

        // ------------------------------------------------------------------ mods page

        void BuildModsPage()
        {
            list = new ListView
            {
                View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true, BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 16, 14), ForeColor = Theme.Text, Location = new Point(4, 4), Size = new Size(570, 196),
            };
            list.SmallImageList = new ImageList { ImageSize = new Size(1, 26) };   // sets the row height
            list.Columns.Add("Mod", 200);
            list.Columns.Add("Version", 74);
            list.Columns.Add("Author", 170);
            list.Columns.Add("Status", 122);
            list.DrawColumnHeader += DrawHeader;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += DrawSubItem;
            // Enabling: the Enable/Disable button, a click on the diamond, a double-click or Space.
            list.MouseClick += (s, e) =>
            {
                var hit = list.HitTest(e.Location);
                if (hit.Item != null && e.X < 26) ToggleMod(hit.Item);
            };
            list.MouseDoubleClick += (s, e) =>
            {
                var hit = list.HitTest(e.Location);
                if (hit.Item != null && e.X >= 26) ToggleMod(hit.Item);
            };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && list.SelectedItems.Count > 0) { ToggleMod(list.SelectedItems[0]); e.Handled = true; }
            };
            list.SelectedIndexChanged += delegate { UpdateDetails(); };

            toggleButton = SideButton("Disable", 4, delegate { if (list.SelectedItems.Count > 0) ToggleMod(list.SelectedItems[0]); });
            upButton = SideButton("Move up", 46, delegate { MoveSelected(-1); });
            downButton = SideButton("Move down", 84, delegate { MoveSelected(1); });
            settingsButton = SideButton("Settings", 126, delegate { OpenModSettings(); });
            SideButton("Refresh", 164, delegate { SaveAll(); ReloadMods(); });

            description = new GameText
            {
                Multiline = true, ForeColor = Theme.Muted, TextSize = Ui.SmallSize, Location = new Point(6, 210), Size = new Size(726, 88),
            };
            modsPage.Controls.Add(list);
            modsPage.Controls.Add(description);
        }

        GameButton SideButton(string text, int top, EventHandler click)
        {
            var b = new GameButton { Text = text, Location = new Point(588, top), Size = new Size(148, 34) };
            b.Click += click;
            modsPage.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ settings page

        void BuildSettingsPage()
        {
            if (!settings.Exists)
            {
                settingsPage.Controls.Add(new GameText
                {
                    Text = "Start the game once so it creates its settings file; the settings then appear here.",
                    ForeColor = Theme.Muted, Location = new Point(8, 8), Size = new Size(720, 30),
                });
                return;
            }
            int col = 0, row = 0;
            foreach (var opt in GameSettings.Options)
            {
                AddOption(opt, 8 + col * 372, 6 + row * 38);
                if (++col == 2) { col = 0; row++; }
            }
            settingsPage.Controls.Add(new GameText
            {
                Text = "Changes are saved when you press Play or close the launcher.", ForeColor = Theme.Muted,
                TextSize = Ui.SmallSize, Location = new Point(8, 6 + row * 38 + 14), Size = new Size(720, 28),
            });
        }

        void AddOption(GameOption opt, int x, int y)
        {
            int? current = settings.Get(opt.Key);
            if (opt.IsBool)
            {
                var cb = new GameCheck
                {
                    Text = opt.Label, Checked = current.GetValueOrDefault() != 0, Location = new Point(x, y + 4), Size = new Size(320, 28),
                };
                cb.CheckedChanged += delegate { settings.Set(opt.Key, cb.Checked ? 1 : 0); };
                settingsPage.Controls.Add(cb);
                return;
            }

            settingsPage.Controls.Add(new GameText { Text = opt.Label, Location = new Point(x, y + 4), Size = new Size(176, 28) });

            if (opt.IsSlider)
            {
                var value = new GameText { ForeColor = Theme.Muted, Location = new Point(x + 334, y + 4), Size = new Size(30, 28) };
                var slider = new GameSlider
                {
                    Minimum = opt.Min, Maximum = opt.Max, Location = new Point(x + 178, y + 4), Size = new Size(152, 28),
                };
                slider.Value = Math.Max(opt.Min, Math.Min(opt.Max, current.GetValueOrDefault(opt.Max)));
                value.Text = slider.Value.ToString();
                slider.ValueChanged += delegate { value.Text = slider.Value.ToString(); settings.Set(opt.Key, slider.Value); };
                settingsPage.Controls.Add(slider);
                settingsPage.Controls.Add(value);
                return;
            }

            var combo = new GameCombo { Location = new Point(x + 178, y + 2), Width = 168 };
            var values = opt.Values.ToList();
            combo.Items.AddRange(opt.Names);
            int index = current.HasValue ? values.IndexOf(current.Value) : -1;
            if (current.HasValue && index < 0)
            {
                // keep a value the launcher does not offer, so opening the launcher never changes it
                values.Add(current.Value);
                combo.Items.Add(string.Format("Custom ({0})", current.Value));
                index = values.Count - 1;
            }
            combo.SelectedIndex = index;
            combo.SelectedIndexChanged += delegate
            {
                if (combo.SelectedIndex >= 0) settings.Set(opt.Key, values[combo.SelectedIndex]);
            };
            settingsPage.Controls.Add(combo);
        }

        void ShowPage(bool showMods)
        {
            modsPage.Visible = showMods;
            settingsPage.Visible = !showMods;
            modsTab.Selected = showMods;
            settingsTab.Selected = !showMods;
            modsTab.Invalidate();
            settingsTab.Invalidate();
        }

        // ------------------------------------------------------------------ mods list

        void ReloadMods()
        {
            mods = ModCatalog.Load(modsDir);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var m in mods)
                list.Items.Add(new ListViewItem(new[] { m.Name, m.Version, m.Author, m.Status }) { Tag = m });
            list.EndUpdate();
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            UpdateDetails();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            status.Text = string.Format("{0} mod(s), {1} enabled", mods.Count, mods.Count(m => m.Enabled));
        }

        ModInfo Selected
        {
            get { return list.SelectedItems.Count > 0 ? (ModInfo)list.SelectedItems[0].Tag : null; }
        }

        void UpdateDetails()
        {
            var m = Selected;
            toggleButton.Enabled = m != null;
            toggleButton.Text = m != null && !m.Enabled ? "Enable" : "Disable";
            settingsButton.Enabled = m != null && m.Settings.Count > 0;
            upButton.Enabled = m != null && list.SelectedIndices[0] > 0;
            downButton.Enabled = m != null && list.SelectedIndices[0] < list.Items.Count - 1;
            if (m == null)
            {
                description.Text = mods.Count == 0
                    ? "No mods installed. Put a mod's folder (with its mod.ini) into the mods folder, then press Refresh."
                    : "";
                return;
            }
            string text = m.Description.Length > 0 ? m.Description : "No description.";
            if (!m.DllExists) text += "\nThe mod's DLL is missing: " + Path.GetFileName(m.Dll);
            else if (m.LoaderApi > ModCatalog.LoaderApi)
                text += string.Format("\nThis mod needs a newer mod loader (API {0}; installed API {1}). Update winmm.dll.",
                                      m.LoaderApi, ModCatalog.LoaderApi);
            text += "\nFolder: " + m.Dir;
            description.Text = text;
        }

        void ToggleMod(ListViewItem item)
        {
            var mod = (ModInfo)item.Tag;
            mod.Enabled = !mod.Enabled;
            item.SubItems[3].Text = mod.Status;
            list.Invalidate();
            UpdateDetails();
            UpdateStatus();
        }

        void MoveSelected(int delta)
        {
            if (list.SelectedIndices.Count == 0) return;
            int i = list.SelectedIndices[0], j = i + delta;
            if (j < 0 || j >= list.Items.Count) return;
            var mod = mods[i];
            mods.RemoveAt(i);
            mods.Insert(j, mod);
            var item = list.Items[i];
            list.Items.RemoveAt(i);
            list.Items.Insert(j, item);
            item.Selected = true;
            item.Focused = true;
            list.Focus();
            UpdateDetails();
        }

        void OpenModSettings()
        {
            var m = Selected;
            if (m == null || m.Settings.Count == 0) return;
            using (var dialog = new ModSettingsDialog(m)) dialog.ShowDialog(this);
        }

        void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var back = new SolidBrush(Color.FromArgb(14, 12, 11))) e.Graphics.FillRectangle(back, e.Bounds);
            var r = new RectangleF(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            Ui.Draw(e.Graphics, e.Header.Text, TextStyle.Body, Ui.SmallSize, Theme.Muted, r, StringAlignment.Near);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        void DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var mod = (ModInfo)e.Item.Tag;
            using (var b = new SolidBrush(e.Item.Selected ? Theme.Selection : list.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);

            RectangleF r = e.Bounds;
            Color color = mod.Enabled ? Theme.Text : Theme.Muted;
            if (e.ColumnIndex == 0)
            {
                Ui.Diamond(e.Graphics, new PointF(r.X + 12, r.Y + r.Height / 2), 6, mod.Enabled, Theme.Text);
                r = new RectangleF(r.X + 26, r.Y, r.Width - 26, r.Height);
            }
            else r = new RectangleF(r.X + 8, r.Y, r.Width - 8, r.Height);
            if (e.ColumnIndex == 3)
                color = (!mod.DllExists || mod.LoaderApi > ModCatalog.LoaderApi) ? Theme.Warning : mod.Enabled ? Theme.Accent : Theme.Muted;
            Ui.Draw(e.Graphics, e.SubItem.Text, TextStyle.Body, Ui.BodySize, color, r, StringAlignment.Near);
        }

        // ------------------------------------------------------------------ play

        void SaveAll()
        {
            try
            {
                if (Directory.Exists(modsDir)) ModCatalog.SaveOrder(modsDir, mods);
                settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Play()
        {
            if (GameLauncher.GameRunning())
            {
                MessageBox.Show(this, "Exanima is already running.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveAll();
            Enabled = false;
            string error = GameLauncher.Launch(gameDir, args, s => { status.Text = s; status.Refresh(); });
            if (error != null)
            {
                Enabled = true;
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Close();
        }
    }
}
