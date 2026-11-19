// "Copy setup": pick a character to copy from, one to copy onto, and which parts.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClassicUO.IEX.Launcher
{
    sealed class CopySetupDialog : Form
    {
        static readonly Color Back = Color.FromArgb(22, 21, 24), Field = Color.FromArgb(8, 8, 10), Cream = Color.FromArgb(236, 226, 200), Dim = Color.FromArgb(160, 150, 128), Gold = Color.FromArgb(196, 148, 52);

        readonly ComboBox _from, _to;
        readonly CheckBox _settings, _layout, _macros;
        readonly List<CharacterProfile> _characters;

        public CopySetupDialog(List<CharacterProfile> characters, string preferredFrom, Fonts fonts)
        {
            _characters = characters;
            Text = "Copy a character's setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(470, 336);
            BackColor = Back;
            ForeColor = Cream;
            Font = fonts.Body(16);

            _from = Combo("Copy from", 18);
            _to = Combo("Onto", 58);
            foreach (var c in characters)
            {
                _from.Items.Add(c);
                _to.Items.Add(c);
            }
            int pick = Math.Max(0, characters.FindIndex(c => c.Name.Equals(preferredFrom ?? "", StringComparison.OrdinalIgnoreCase)));
            _from.SelectedIndex = pick;
            _to.SelectedIndex = characters.Count > 1 ? (pick == 0 ? 1 : 0) : 0;

            Controls.Add(new Label { Text = "What to copy", Left = 18, Top = 106, Width = 200, Height = 24, ForeColor = Dim });
            _settings = Check("Settings: everything in Options", 132, true);
            _layout = Check("Window layout: which windows are open, and where", 160, true);
            _macros = Check("Macros and hotkeys", 188, true);

            Controls.Add(new Label
            {
                Text = "The character you copy onto keeps a backup of its old setup in its profile folder. Close any game window playing that character first. Razor settings are separate.",
                Left = 18, Top = 222, Width = 434, Height = 60, ForeColor = Dim, Font = fonts.Body(14)
            });

            var ok = Button("Copy", new Rectangle(ClientSize.Width - 200, ClientSize.Height - 46, 86, 30));
            var cancel = Button("Cancel", new Rectangle(ClientSize.Width - 104, ClientSize.Height - 46, 86, 30));
            ok.Click += (s, e) => DoCopy();
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        ComboBox Combo(string label, int y)
        {
            Controls.Add(new Label { Text = label, Left = 18, Top = y + 4, Width = 110, Height = 24, ForeColor = Dim });
            var box = new ComboBox
            {
                Left = 130, Top = y, Width = 322, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Field, ForeColor = Cream, Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(box);
            return box;
        }

        CheckBox Check(string text, int y, bool on)
        {
            var c = new CheckBox { Text = text, Left = 24, Top = y, Width = 430, Height = 26, Checked = on, ForeColor = Cream, FlatStyle = FlatStyle.Flat };
            c.FlatAppearance.BorderColor = Gold;
            c.FlatAppearance.CheckedBackColor = Color.FromArgb(40, 30, 14);
            Controls.Add(c);
            return c;
        }

        Button Button(string text, Rectangle r)
        {
            var b = new Button { Text = text, Bounds = r, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(16, 15, 17), ForeColor = Cream, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 30, 14);
            Controls.Add(b);
            return b;
        }

        void DoCopy()
        {
            var from = _from.SelectedItem as CharacterProfile;
            var to = _to.SelectedItem as CharacterProfile;
            if (from == null || to == null || from == to)
            {
                MessageBox.Show(this, "Pick two different characters.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var parts = new[] { (_settings.Checked, "settings"), (_layout.Checked, "window layout"), (_macros.Checked, "macros") }.Where(p => p.Item1).Select(p => p.Item2).ToList();
            if (parts.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one thing to copy.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, $"Copy {string.Join(", ", parts)} from {from.Name} onto {to.Name}?\n\n{to.Name}'s current setup is backed up first.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                var backup = ProfileCopy.Copy(from, to, _settings.Checked, _layout.Checked, _macros.Checked);
                MessageBox.Show(this, $"Done. {to.Name} starts with {from.Name}'s {string.Join(", ", parts)} next time you play it.\n\nThe old setup is in:\n{backup}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "The copy didn't finish: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
