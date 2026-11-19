// Add or edit one of the player's other shards: a name, the login address and port, the UO folder
// whose files that shard expects, and (optionally) the client version it wants.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClassicUO.IEX.Launcher
{
    sealed class ShardDialog : Form
    {
        static readonly Color Back = Color.FromArgb(22, 21, 24), Field = Color.FromArgb(8, 8, 10), Cream = Color.FromArgb(236, 226, 200), Dim = Color.FromArgb(160, 150, 128);

        readonly TextBox _name, _host, _port, _path, _version;
        public ShardEntry Entry { get; private set; }

        public ShardDialog(ShardEntry existing, Fonts fonts)
        {
            Text = existing == null ? "Add a shard" : "Edit " + existing.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(470, 330);
            BackColor = Back;
            ForeColor = Cream;
            Font = fonts.Body(16);

            int y = 18;
            _name = Row("Name", existing?.Name ?? "", ref y, 300);
            _host = Row("Address", existing?.Host ?? "", ref y, 300);
            _port = Row("Port", (existing?.Port ?? 2593).ToString(), ref y, 90);
            _path = Row("UO folder", existing?.UoPath ?? "", ref y, 230);
            var browse = Button("Browse...", new Rectangle(_path.Right + 8, _path.Top - 1, 72, 28));
            browse.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog { Description = "The Ultima Online folder this shard's files are in (it holds tiledata.mul)." };
                if (Directory.Exists(_path.Text))
                    dlg.SelectedPath = _path.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _path.Text = dlg.SelectedPath;
            };
            _version = Row("Client version", existing?.ClientVersion ?? "", ref y, 120);

            var hint = new Label
            {
                Text = "Leave the client version empty to read it from the folder's client.exe. Your shard's website lists the address and the files it needs.",
                Left = 18, Top = y + 2, Width = 434, Height = 44, ForeColor = Dim, Font = fonts.Body(14)
            };
            Controls.Add(hint);

            var ok = Button("Save", new Rectangle(ClientSize.Width - 200, ClientSize.Height - 46, 86, 30));
            var cancel = Button("Cancel", new Rectangle(ClientSize.Width - 104, ClientSize.Height - 46, 86, 30));
            ok.Click += (s, e) => Save();
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        TextBox Row(string label, string value, ref int y, int width)
        {
            Controls.Add(new Label { Text = label, Left = 18, Top = y + 4, Width = 120, Height = 24, ForeColor = Dim });
            var box = new TextBox { Text = value, Left = 140, Top = y, Width = width, BackColor = Field, ForeColor = Cream, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(box);
            y += 40;
            return box;
        }

        Button Button(string text, Rectangle r)
        {
            var b = new Button
            {
                Text = text, Bounds = r, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(16, 15, 17), ForeColor = Cream, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(196, 148, 52);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 30, 14);
            Controls.Add(b);
            return b;
        }

        void Save()
        {
            string name = _name.Text.Trim(), host = _host.Text.Trim(), path = _path.Text.Trim(), version = _version.Text.Trim();
            string problem = null;
            if (name.Length == 0)
                problem = "Give the shard a name.";
            else if (name.Equals(LauncherData.ShardName, StringComparison.OrdinalIgnoreCase))
                problem = "In Ex Xen has its own tab. Pick another name.";
            else if (host.Length == 0 || host.Contains(" "))
                problem = "Enter the shard's address, like login.example.com.";
            else if (!int.TryParse(_port.Text.Trim(), out var p) || p < 1 || p > 65535)
                problem = "The port is a number, usually 2593.";
            else if (path.Length == 0 || !File.Exists(Path.Combine(path, "tiledata.mul")))
                problem = "Pick the Ultima Online folder this shard uses (it holds tiledata.mul).";
            else if (version.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(\.\d+){1,3}[a-z]?$"))
                problem = "The client version looks like 7.0.20 or 5.0.6.5. Leave it empty if unsure.";

            if (problem != null)
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Entry = new ShardEntry { Name = name, Host = host, Port = int.Parse(_port.Text.Trim()), UoPath = path, ClientVersion = version };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
