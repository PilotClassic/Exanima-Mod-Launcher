// Settings dialog for one mod, generated from the [Settings] section of its mod.ini.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    class ModSettingsDialog : Form
    {
        readonly ModInfo mod;
        readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();

        public ModSettingsDialog(ModInfo mod)
        {
            this.mod = mod;
            Text = mod.Name + " settings";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;

            var table = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, Padding = new Padding(16, 16, 16, 8), Dock = DockStyle.Top,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));

            var values = mod.LoadValues();
            foreach (var s in mod.Settings)
            {
                Control editor = CreateEditor(s, values[s.Key]);
                editors[s.Key] = editor;
                if (s.Type == SettingType.Bool)
                {
                    editor.Text = s.Label;
                    table.Controls.Add(editor);
                    table.SetColumnSpan(editor, 2);
                }
                else
                {
                    var label = Theme.Label(s.Label, Theme.Body, Theme.Text);
                    label.Margin = new Padding(0, 6, 12, 6);
                    table.Controls.Add(label);
                    table.Controls.Add(editor);
                }
            }

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 52,
                Padding = new Padding(12, 10, 12, 10), BackColor = Theme.Panel,
            };
            var save = Theme.FlatButton("Save", false);
            save.Size = new Size(96, 30);
            save.Click += delegate { Save(); DialogResult = DialogResult.OK; };
            var cancel = Theme.FlatButton("Cancel", false);
            cancel.Size = new Size(96, 30);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            var reset = Theme.FlatButton("Defaults", false);
            reset.Size = new Size(96, 30);
            reset.Click += delegate { ResetDefaults(); };
            buttons.Controls.AddRange(new Control[] { save, cancel, reset });

            Controls.Add(table);
            Controls.Add(buttons);
            ClientSize = new Size(Math.Max(420, table.PreferredSize.Width), table.PreferredSize.Height + buttons.Height);
        }

        Control CreateEditor(SettingDef s, string value)
        {
            switch (s.Type)
            {
                case SettingType.Bool:
                    return new CheckBox
                    {
                        Checked = IsTrue(value), AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 6),
                    };
                case SettingType.Int:
                    var num = new NumericUpDown
                    {
                        Minimum = s.Min, Maximum = s.Max, Width = 120, BackColor = Theme.Panel, ForeColor = Theme.Text,
                        BorderStyle = BorderStyle.FixedSingle,
                    };
                    int v;
                    num.Value = int.TryParse(value, out v) ? Math.Max(s.Min, Math.Min(s.Max, v)) : Math.Max(s.Min, 0);
                    return num;
                case SettingType.Choice:
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, FlatStyle = FlatStyle.Flat,
                        BackColor = Theme.Panel, ForeColor = Theme.Text,
                    };
                    combo.Items.AddRange(s.Choices);
                    combo.SelectedItem = s.Choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
                    if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
                    return combo;
                default:
                    return new TextBox { Text = value, Width = 200, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            }
        }

        void ResetDefaults()
        {
            foreach (var s in mod.Settings)
            {
                Control c = editors[s.Key];
                if (c is CheckBox) ((CheckBox)c).Checked = IsTrue(s.Default);
                else if (c is NumericUpDown)
                {
                    int v;
                    var n = (NumericUpDown)c;
                    n.Value = int.TryParse(s.Default, out v) ? Math.Max(n.Minimum, Math.Min(n.Maximum, v)) : n.Minimum;
                }
                else if (c is ComboBox) ((ComboBox)c).SelectedItem = s.Default;
                else c.Text = s.Default;
            }
        }

        void Save()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in mod.Settings)
            {
                Control c = editors[s.Key];
                if (c is CheckBox) values[s.Key] = ((CheckBox)c).Checked ? "1" : "0";
                else if (c is NumericUpDown) values[s.Key] = ((int)((NumericUpDown)c).Value).ToString();
                else if (c is ComboBox) values[s.Key] = Convert.ToString(((ComboBox)c).SelectedItem);
                else values[s.Key] = c.Text;
            }
            mod.SaveValues(values);
        }

        static bool IsTrue(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }
    }
}
