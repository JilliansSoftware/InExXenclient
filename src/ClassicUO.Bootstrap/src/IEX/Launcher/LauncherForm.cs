// The launcher window: a borderless, hand-painted form over background.png (made by
// branding/art/make_launcher_art.py). Layout B from the mock: banner with tabs on its bottom
// edge, patch notes (or the list of other shards) on the left, and a column on the right with
// the shard's status, the account to play as, the update progress and the Play button.
// Box positions must match the art script.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClassicUO.IEX.Launcher
{
    sealed class LauncherForm : Form
    {
        // ---- palette and layout ----------------------------------------------------------------
        static readonly Color Gold = Color.FromArgb(196, 148, 52), GoldLo = Color.FromArgb(112, 76, 22), GoldHi = Color.FromArgb(240, 204, 110);
        static readonly Color Cream = Color.FromArgb(236, 226, 200), Dim = Color.FromArgb(160, 150, 128), Ink = Color.FromArgb(6, 6, 8);
        static readonly Color GoldA = Color.FromArgb(250, 222, 128), GoldB = Color.FromArgb(226, 170, 52), GoldC = Color.FromArgb(130, 84, 18);

        const int W = 960, H = 640;
        static readonly Rectangle Banner = new Rectangle(18, 18, 924, 220);
        static readonly Rectangle NotesBox = new Rectangle(18, 288, 639, 334);   // notes / shard list box
        static readonly Rectangle SideBox = new Rectangle(670, 252, 272, 370); // status / account / update / play
        const int Pad = 18;

        static readonly Rectangle NotesView = new Rectangle(NotesBox.X + 18, NotesBox.Y + 14, NotesBox.Width - 18 - 30, NotesBox.Height - 22);
        static readonly Rectangle ScrollTrack = new Rectangle(NotesBox.Right - 20, NotesBox.Y + 8, 8, NotesBox.Height - 16);

        static readonly Rectangle AccountBox = new Rectangle(SideBox.X + Pad, SideBox.Y + 120, SideBox.Width - Pad * 2, 32);
        static readonly Rectangle ProgressBox = new Rectangle(SideBox.X + Pad, SideBox.Y + 196, SideBox.Width - Pad * 2, 16);
        static readonly Rectangle PlayButton = new Rectangle(SideBox.X + Pad, SideBox.Y + 260, SideBox.Width - Pad * 2, 74);
        static readonly Rectangle SkipBox = new Rectangle(SideBox.X + Pad, SideBox.Y + 344, 14, 14);

        // ---- state -----------------------------------------------------------------------------
        readonly string[] _args;
        public string[] Result { get; private set; }

        int _tab; // 0 In Ex Xen, 1 Other shards
        string _hover, _pressed;
        readonly List<(string Id, Rectangle Rect)> _hits = new List<(string, Rectangle)>();

        readonly List<NewsItem> _news = new List<NewsItem>();
        readonly List<LinkItem> _links = new List<LinkItem>();
        bool _newsLoaded;
        int _scroll, _contentHeight;
        bool _dragThumb;
        int _dragStartY, _dragStartScroll;

        readonly List<Account> _accounts;
        int _account;
        bool _dropOpen;

        readonly List<ShardEntry> _shards;
        int _shard = -1;

        volatile int _online = -1; // -1 checking, 0 offline, 1 online
        string _updateStatus = "Checking for updates...";
        int _percent = -1;
        bool _updateDone;

        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 50 };
        int _tick;

        readonly Image _background;
        readonly Fonts _f = new Fonts();

        public LauncherForm(string[] args)
        {
            _args = args;
            Text = "In Ex Xen";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(22, 21, 24);
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); } catch { }

            _background = Resources.Image("background.png");
            _accounts = LauncherData.Accounts();
            _shards = LauncherData.LoadShards();
            if (_shards.Count > 0)
                _shard = 0;

            _timer.Tick += OnTick;
            _timer.Start();

            var host = LauncherData.ShardHost;
            var port = LauncherData.ShardPort;
            new Thread(() =>
            {
                var online = LauncherData.IsOnline(host, port);
                _online = online == true ? 1 : 0;
            }) { IsBackground = true, Name = "IEX launcher status" }.Start();

            new Thread(() =>
            {
                var text = LauncherData.FetchNews();
                var news = new List<NewsItem>();
                var links = new List<LinkItem>();
                LauncherData.ParseNews(text, news, links);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _news.AddRange(news);
                        _links.AddRange(links);
                        _newsLoaded = true;
                        Invalidate();
                    }));
                }
                catch { }
            }) { IsBackground = true, Name = "IEX launcher news" }.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _background?.Dispose();
                _f.Dispose();
            }
            base.Dispose(disposing);
        }

        void OnTick(object sender, EventArgs e)
        {
            _tick++;
            bool changed = false;
            var status = Updater.Status;
            var percent = Updater.Percent;
            var done = Updater.Done;
            if (status != _updateStatus || percent != _percent || done != _updateDone)
            {
                _updateStatus = status;
                _percent = percent;
                _updateDone = done;
                changed = true;
            }

            if (done && Updater.Relaunched)
            {
                // the new exe is already starting and shows its own launcher
                Result = null;
                Close();
                return;
            }

            if (changed || !done || _online < 0 || !_newsLoaded)
                Invalidate(); // marquee and "checking" dots
        }

        // ---- painting ----------------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            _hits.Clear();

            if (_background != null)
                g.DrawImageUnscaled(_background, 0, 0);

            DrawWindowButtons(g);
            DrawLinks(g);
            DrawTabs(g);

            if (_tab == 0)
            {
                GoldText(g, "What's new", _f.Gothic(26), new PointF(30, 248));
                DrawNotes(g);
            }
            else
            {
                GoldText(g, "Your other shards", _f.Gothic(26), new PointF(30, 248));
                DrawShardList(g);
            }

            DrawRightColumn(g);

            if (_dropOpen)
                DrawDropList(g);
        }

        void Hit(string id, Rectangle r) => _hits.Add((id, r));
        bool Hot(string id) => _hover == id;

        void DrawWindowButtons(Graphics g)
        {
            var close = new Rectangle(Banner.Right - 30, Banner.Y + 8, 22, 22);
            var min = new Rectangle(close.X - 26, close.Y, 22, 22);
            foreach (var (id, r) in new[] { ("min", min), ("close", close) })
            {
                Hit(id, r);
                using (var b = new SolidBrush(Color.FromArgb(Hot(id) ? 200 : 120, 8, 8, 10)))
                    g.FillRectangle(b, r);
                using (var p = new Pen(Hot(id) ? GoldHi : GoldLo))
                    g.DrawRectangle(p, r);
                using var pen = new Pen(Hot(id) ? GoldHi : Color.FromArgb(214, 190, 140), 2f);
                if (id == "close")
                {
                    g.DrawLine(pen, r.X + 7, r.Y + 7, r.Right - 7, r.Bottom - 7);
                    g.DrawLine(pen, r.Right - 7, r.Y + 7, r.X + 7, r.Bottom - 7);
                }
                else
                {
                    g.DrawLine(pen, r.X + 6, r.Bottom - 7, r.Right - 6, r.Bottom - 7);
                }
            }
        }

        void DrawLinks(Graphics g)
        {
            // a row under the window buttons, right-aligned
            int x = Banner.Right - 12 - _links.Count * 44 + 10;
            int cy = Banner.Y + 64;
            for (int i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                string id = "link" + i;
                var r = new Rectangle(x + i * 44, cy - 17, 34, 34);
                Hit(id, r);
                bool hot = Hot(id);
                using (var b = new SolidBrush(hot ? Color.FromArgb(40, 30, 14) : Color.FromArgb(220, 14, 13, 15)))
                    g.FillEllipse(b, r);
                using (var p = new Pen(hot ? Gold : GoldLo, 1.2f))
                    g.DrawEllipse(p, r);
                Glyph(g, link.Name, r.X + r.Width / 2f, r.Y + r.Height / 2f, hot ? GoldHi : Color.FromArgb(214, 190, 140));

                if (hot)
                {
                    var f = _f.Semi(14);
                    var size = g.MeasureString(link.Name, f);
                    var tip = new RectangleF(Math.Min(r.X + r.Width / 2f - size.Width / 2 - 8, Banner.Right - size.Width - 22), r.Bottom + 6, size.Width + 16, 22);
                    using (var b = new SolidBrush(Color.FromArgb(8, 8, 10)))
                        g.FillRectangle(b, tip);
                    using (var p = new Pen(GoldLo))
                        g.DrawRectangle(p, tip.X, tip.Y, tip.Width, tip.Height);
                    DrawText(g, link.Name, f, Cream, tip, StringAlignment.Center);
                }
            }
        }

        static void Glyph(Graphics g, string name, float cx, float cy, Color c)
        {
            using var pen = new Pen(c, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(c);
            switch (name.ToLowerInvariant())
            {
                case "website":
                case "web":
                    g.DrawEllipse(pen, cx - 9, cy - 9, 18, 18);
                    g.DrawEllipse(pen, cx - 4, cy - 9, 8, 18);
                    g.DrawLine(pen, cx - 9, cy, cx + 9, cy);
                    break;
                case "discord":
                case "chat":
                    using (var path = Rounded(new RectangleF(cx - 10, cy - 8, 20, 13), 4))
                        g.DrawPath(pen, path);
                    g.FillPolygon(brush, new[] { new PointF(cx - 4, cy + 4), new PointF(cx - 8, cy + 10), new PointF(cx + 1, cy + 4) });
                    break;
                case "wiki":
                case "guide":
                case "guides":
                    g.DrawPolygon(pen, new[] { new PointF(cx, cy - 6), new PointF(cx - 10, cy - 9), new PointF(cx - 10, cy + 7), new PointF(cx, cy + 10) });
                    g.DrawPolygon(pen, new[] { new PointF(cx, cy - 6), new PointF(cx + 10, cy - 9), new PointF(cx + 10, cy + 7), new PointF(cx, cy + 10) });
                    break;
                case "account":
                    g.DrawEllipse(pen, cx - 10, cy - 5, 8, 8);
                    g.DrawLine(pen, cx - 2, cy - 1, cx + 10, cy - 1);
                    g.DrawLine(pen, cx + 6, cy - 1, cx + 6, cy + 4);
                    g.DrawLine(pen, cx + 10, cy - 1, cx + 10, cy + 4);
                    break;
                default:
                    // first letter for anything else
                    using (var f = new Font(FontFamily.GenericSerif, 12f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(name.Substring(0, 1).ToUpperInvariant(), f, brush, cx, cy + 1, sf);
                    break;
            }
        }

        void DrawTabs(Graphics g)
        {
            string[] names = { LauncherData.ShardName, "Other shards" };
            int x = 36, y = Banner.Bottom - 34 + 1, h = 34;
            var font = _f.Semi(18);
            for (int i = 0; i < names.Length; i++)
            {
                int tw = (int)g.MeasureString(names[i], font).Width + 36;
                var r = new Rectangle(x, y, tw, h);
                Hit("tab" + i, r);
                if (i == _tab)
                {
                    using (var b = new SolidBrush(Color.FromArgb(22, 21, 24)))
                        g.FillRectangle(b, r.X, r.Y, r.Width, r.Height + 1);
                    using (var p = new Pen(GoldHi, 2f))
                        g.DrawLine(p, r.X, r.Y + 1, r.Right, r.Y + 1);
                    using (var p = new Pen(Gold))
                    {
                        g.DrawLine(p, r.X, r.Y, r.X, r.Bottom);
                        g.DrawLine(p, r.Right, r.Y, r.Right, r.Bottom);
                    }
                    GoldText(g, names[i], font, new PointF(r.X + 18, r.Y + 5));
                }
                else
                {
                    var rr = new Rectangle(r.X, r.Y + 4, r.Width, r.Height - 4);
                    using (var b = new SolidBrush(Hot("tab" + i) ? Color.FromArgb(34, 26, 14) : Color.FromArgb(230, 10, 9, 12)))
                        g.FillRectangle(b, rr);
                    using (var p = new Pen(GoldLo))
                        g.DrawRectangle(p, rr);
                    DrawText(g, names[i], font, Hot("tab" + i) ? GoldHi : Dim, new RectangleF(rr.X, rr.Y + 2, rr.Width, rr.Height), StringAlignment.Center);
                }
                x += tw + 4;
            }
        }

        void DrawNotes(Graphics g)
        {
            var clip = g.Clip;
            g.SetClip(new Rectangle(NotesBox.X + 2, NotesBox.Y + 2, NotesBox.Width - 4, NotesBox.Height - 4));

            float y = NotesView.Y - _scroll;
            var h2 = _f.Bold(20);
            var body = _f.Body(16);
            var date = _f.Body(15);

            if (!_newsLoaded)
            {
                DrawText(g, "Loading the latest news" + new string('.', _tick / 6 % 4), body, Dim, new RectangleF(NotesView.X, NotesView.Y, NotesView.Width, 24), StringAlignment.Near);
            }
            else if (_news.Count == 0)
            {
                DrawText(g, "No news yet. Check back after the next update.", body, Dim, new RectangleF(NotesView.X, NotesView.Y, NotesView.Width, 24), StringAlignment.Near);
            }

            foreach (var item in _news)
            {
                var dateW = string.IsNullOrEmpty(item.Date) ? 0 : g.MeasureString(item.Date, date).Width + 12;
                foreach (var titleLine in Wrap(g, item.Title, h2, NotesView.Width - dateW))
                {
                    GoldText(g, titleLine, h2, new PointF(NotesView.X - 2, y));
                    if (dateW > 0)
                    {
                        DrawText(g, item.Date, date, Dim, new RectangleF(NotesView.Right - dateW, y + 5, dateW, 20), StringAlignment.Far);
                        dateW = 0;
                    }
                    y += 28;
                }
                y += 2;
                foreach (var line in item.Lines)
                {
                    bool first = true;
                    foreach (var part in Wrap(g, line, body, NotesView.Width - 22))
                    {
                        if (first)
                            Diamond(g, NotesView.X + 8, y + 10, 4, Gold);
                        DrawText(g, part, body, Cream, new RectangleF(NotesView.X + 22, y, NotesView.Width - 22, 24), StringAlignment.Near);
                        y += 23;
                        first = false;
                    }
                }
                y += 14;
            }

            _contentHeight = (int)(y + _scroll - NotesView.Y);
            g.Clip = clip;

            // fade the last lines into the box
            using (var fade = new LinearGradientBrush(new Rectangle(NotesBox.X + 2, NotesBox.Bottom - 26, NotesBox.Width - 26, 25), Color.FromArgb(0, 9, 9, 11), Color.FromArgb(255, 9, 9, 11), LinearGradientMode.Vertical))
                g.FillRectangle(fade, NotesBox.X + 2, NotesBox.Bottom - 25, NotesBox.Width - 26, 24);

            DrawScrollBar(g);
        }

        int MaxScroll => Math.Max(0, _contentHeight - NotesView.Height + 10);

        Rectangle ThumbRect()
        {
            if (MaxScroll <= 0)
                return Rectangle.Empty;
            int th = Math.Max(30, ScrollTrack.Height * NotesView.Height / Math.Max(1, _contentHeight));
            int ty = ScrollTrack.Y + (ScrollTrack.Height - th) * _scroll / MaxScroll;
            return new Rectangle(ScrollTrack.X + 1, ty, ScrollTrack.Width - 1, th);
        }

        void DrawScrollBar(Graphics g)
        {
            var thumb = ThumbRect();
            if (thumb.IsEmpty)
                return;
            using (var b = new SolidBrush(Color.FromArgb(20, 16, 10)))
                g.FillRectangle(b, ScrollTrack);
            using (var p = new Pen(GoldLo))
                g.DrawRectangle(p, ScrollTrack);
            Hit("thumb", new Rectangle(ScrollTrack.X - 3, ScrollTrack.Y, ScrollTrack.Width + 6, ScrollTrack.Height));
            using (var b = new SolidBrush(Hot("thumb") || _dragThumb ? GoldHi : Gold))
                g.FillRectangle(b, thumb);
        }

        void DrawShardList(Graphics g)
        {
            var head = _f.Semi(14);
            var row = _f.Semi(17);
            var cell = _f.Body(15);
            int x0 = NotesBox.X + 18, colAddr = NotesBox.X + 230, colVer = NotesBox.X + 470;
            DrawText(g, "SHARD", head, Dim, new RectangleF(x0, NotesBox.Y + 12, 200, 20), StringAlignment.Near);
            DrawText(g, "ADDRESS", head, Dim, new RectangleF(colAddr, NotesBox.Y + 12, 230, 20), StringAlignment.Near);
            DrawText(g, "CLIENT", head, Dim, new RectangleF(colVer, NotesBox.Y + 12, 120, 20), StringAlignment.Near);
            using (var p = new Pen(GoldLo))
                g.DrawLine(p, NotesBox.X + 12, NotesBox.Y + 36, NotesBox.Right - 12, NotesBox.Y + 36);

            int y = NotesBox.Y + 44;
            if (_shards.Count == 0)
            {
                DrawText(g, "No other shards yet. Add one to connect to it with this client.", _f.Body(16), Dim, new RectangleF(x0, y + 6, NotesBox.Width - 36, 24), StringAlignment.Near);
                y += 40;
            }

            for (int i = 0; i < _shards.Count && y < NotesBox.Bottom - 150; i++)
            {
                var s = _shards[i];
                var r = new Rectangle(NotesBox.X + 8, y - 2, NotesBox.Width - 16, 36);
                Hit("shard" + i, r);
                bool sel = i == _shard;
                if (sel || Hot("shard" + i))
                {
                    using (var b = new SolidBrush(sel ? Color.FromArgb(40, 30, 14) : Color.FromArgb(20, 17, 12)))
                        g.FillRectangle(b, r);
                    if (sel)
                        using (var p = new Pen(Gold))
                            g.DrawRectangle(p, r);
                }
                if (sel)
                    GoldText(g, Ellipsize(g, s.Name, row, colAddr - x0 - 10), row, new PointF(x0, y + 5));
                else
                    DrawText(g, Ellipsize(g, s.Name, row, colAddr - x0 - 10), row, Cream, new RectangleF(x0, y, colAddr - x0 - 10, 32), StringAlignment.Near, true);
                DrawText(g, Ellipsize(g, s.Host + " : " + s.Port, cell, colVer - colAddr - 10), cell, sel ? Cream : Dim, new RectangleF(colAddr, y, colVer - colAddr - 10, 32), StringAlignment.Near, true);
                DrawText(g, string.IsNullOrEmpty(s.ClientVersion) ? "from its files" : s.ClientVersion, cell, sel ? Cream : Dim, new RectangleF(colVer, y, NotesBox.Right - colVer - 12, 32), StringAlignment.Near, true);
                y += 40;
            }

            var note = _f.Body(15);
            DrawText(g, "Other shards start with their own settings and their own UO folder.", note, Dim, new RectangleF(x0, NotesBox.Bottom - 100, NotesBox.Width - 36, 20), StringAlignment.Near);
            DrawText(g, "In Ex Xen updates and news only apply to In Ex Xen.", note, Dim, new RectangleF(x0, NotesBox.Bottom - 80, NotesBox.Width - 36, 20), StringAlignment.Near);

            int by = NotesBox.Bottom - 48;
            SmallButton(g, "add", "+ Add shard", new Rectangle(x0, by, 130, 30), true);
            SmallButton(g, "edit", "Edit", new Rectangle(x0 + 140, by, 100, 30), _shard >= 0);
            SmallButton(g, "remove", "Remove", new Rectangle(x0 + 250, by, 100, 30), _shard >= 0);
        }

        void SmallButton(Graphics g, string id, string label, Rectangle r, bool enabled)
        {
            if (enabled)
                Hit(id, r);
            bool hot = enabled && Hot(id);
            using (var b = new SolidBrush(hot ? Color.FromArgb(40, 30, 14) : Color.FromArgb(16, 15, 17)))
                g.FillRectangle(b, r);
            using (var p = new Pen(hot ? Gold : GoldLo))
                g.DrawRectangle(p, r);
            DrawText(g, label, _f.Semi(15), !enabled ? Color.FromArgb(90, 84, 72) : hot ? GoldHi : Cream, r, StringAlignment.Center, true);
        }

        void DrawRightColumn(Graphics g)
        {
            var label = _f.Semi(14);
            int x = SideBox.X + Pad, w = SideBox.Width - Pad * 2;

            // shard status
            DrawText(g, "SHARD", label, Dim, new RectangleF(x, SideBox.Y + 16, w, 18), StringAlignment.Near);
            string statusText;
            Color dot;
            if (_tab == 1)
            {
                statusText = _shard >= 0 ? _shards[_shard].Name : "No shard picked";
                dot = Color.FromArgb(150, 140, 120);
            }
            else if (_online < 0)
            {
                statusText = "Checking" + new string('.', _tick / 6 % 4);
                dot = Color.FromArgb(200, 170, 80);
            }
            else if (_online == 1)
            {
                statusText = "Online";
                dot = Color.FromArgb(90, 200, 90);
            }
            else
            {
                statusText = "Offline";
                dot = Color.FromArgb(210, 70, 60);
            }
            var sf = _f.Semi(16);
            statusText = Ellipsize(g, statusText, sf, w - 44);
            var pill = new Rectangle(x, SideBox.Y + 40, (int)g.MeasureString(statusText, sf).Width + 44, 30);
            using (var path = Rounded(pill, 6))
            {
                using (var b = new SolidBrush(Color.FromArgb(12, 14, 12)))
                    g.FillPath(b, path);
                using (var p = new Pen(GoldLo))
                    g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(dot))
                g.FillEllipse(b, pill.X + 13, pill.Y + 10, 10, 10);
            DrawText(g, statusText, sf, Cream, new RectangleF(pill.X + 30, pill.Y, pill.Width - 30, pill.Height), StringAlignment.Near, true);

            // account
            DrawText(g, "PLAY AS", label, Dim, new RectangleF(x, AccountBox.Y - 24, w, 18), StringAlignment.Near);
            if (_tab == 0 && _accounts.Count > 0)
            {
                Hit("account", AccountBox);
                using (var b = new SolidBrush(Ink))
                    g.FillRectangle(b, AccountBox);
                using (var p = new Pen(Hot("account") || _dropOpen ? GoldHi : Gold))
                    g.DrawRectangle(p, AccountBox);
                var af = _f.Semi(17);
                DrawText(g, Ellipsize(g, _accounts[_account].ToString(), af, AccountBox.Width - 40), af, Cream, new RectangleF(AccountBox.X + 10, AccountBox.Y, AccountBox.Width - 40, AccountBox.Height), StringAlignment.Near, true);
                if (_accounts.Count > 1)
                    using (var b = new SolidBrush(GoldHi))
                        g.FillPolygon(b, new[] { new Point(AccountBox.Right - 22, AccountBox.Y + 12), new Point(AccountBox.Right - 10, AccountBox.Y + 12), new Point(AccountBox.Right - 16, AccountBox.Y + 20) });
            }
            else
            {
                DrawText(g, "You log in at the next screen.", _f.Body(16), Dim, new RectangleF(x, AccountBox.Y, w, AccountBox.Height), StringAlignment.Near, true);
            }

            // update
            DrawText(g, "UPDATE", label, Dim, new RectangleF(x, ProgressBox.Y - 26, w, 18), StringAlignment.Near);
            using (var b = new SolidBrush(Ink))
                g.FillRectangle(b, ProgressBox);
            using (var p = new Pen(GoldLo))
                g.DrawRectangle(p, ProgressBox);
            var inner = new Rectangle(ProgressBox.X + 2, ProgressBox.Y + 2, ProgressBox.Width - 3, ProgressBox.Height - 3);
            if (_percent < 0 && !_updateDone)
            {
                int seg = inner.Width / 4;
                int off = (_tick * 6) % (inner.Width + seg) - seg;
                var r = Rectangle.Intersect(inner, new Rectangle(inner.X + off, inner.Y, seg, inner.Height));
                if (r.Width > 0)
                    FillGold(g, r);
            }
            else
            {
                int pw = (int)(inner.Width * Math.Max(0, Math.Min(100, _updateDone ? 100 : _percent)) / 100f);
                if (pw > 0)
                    FillGold(g, new Rectangle(inner.X, inner.Y, pw, inner.Height));
            }
            DrawText(g, Ellipsize(g, _updateStatus ?? "", _f.Body(15), w), _f.Body(15), Dim, new RectangleF(x, ProgressBox.Bottom + 6, w, 40), StringAlignment.Near);

            // play
            bool canPlay = _updateDone && (_tab == 0 || _shard >= 0);
            if (canPlay)
                Hit("play", PlayButton);
            bool hot = canPlay && Hot("play"), down = hot && _pressed == "play";
            var top = !canPlay ? Color.FromArgb(60, 46, 42) : hot ? Color.FromArgb(150, 44, 28) : Color.FromArgb(120, 34, 22);
            var bottom = !canPlay ? Color.FromArgb(34, 26, 24) : hot ? Color.FromArgb(74, 18, 12) : Color.FromArgb(58, 14, 10);
            var pr = down ? new Rectangle(PlayButton.X, PlayButton.Y + 1, PlayButton.Width, PlayButton.Height) : PlayButton;
            using (var b = new LinearGradientBrush(pr, top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, pr);
            using (var p = new Pen(canPlay ? GoldHi : GoldLo))
                g.DrawRectangle(p, pr);
            using (var p = new Pen(canPlay ? Gold : Color.FromArgb(80, 60, 30)))
                g.DrawRectangle(p, pr.X + 1, pr.Y + 1, pr.Width - 2, pr.Height - 2);
            using (var p = new Pen(Color.FromArgb(150, 100, 40)))
                g.DrawRectangle(p, pr.X + 3, pr.Y + 3, pr.Width - 6, pr.Height - 6);
            var playLabel = _tab == 0 ? "Play" : "Connect";
            var pf = _f.Gothic(_tab == 0 ? 36 : 30);
            var ps = g.MeasureString(playLabel, pf);
            if (canPlay)
                GoldText(g, playLabel, pf, new PointF(pr.X + (pr.Width - ps.Width) / 2, pr.Y + (pr.Height - ps.Height) / 2 + 2));
            else
                DrawText(g, playLabel, pf, Color.FromArgb(120, 104, 80), pr, StringAlignment.Center, true);

            // skip next time
            var skipRow = new Rectangle(SkipBox.X, SkipBox.Y - 3, w, 20);
            Hit("skip", skipRow);
            using (var b = new SolidBrush(Ink))
                g.FillRectangle(b, SkipBox);
            using (var p = new Pen(Hot("skip") ? Gold : GoldLo))
                g.DrawRectangle(p, SkipBox);
            if (LauncherData.SkipWindow)
                using (var p = new Pen(GoldHi, 2f))
                    g.DrawLines(p, new[] { new Point(SkipBox.X + 3, SkipBox.Y + 7), new Point(SkipBox.X + 6, SkipBox.Y + 11), new Point(SkipBox.X + 12, SkipBox.Y + 3) });
            DrawText(g, "Skip this window next time", _f.Body(15), Hot("skip") ? Cream : Dim, new RectangleF(SkipBox.Right + 8, SkipBox.Y - 4, w - 22, 22), StringAlignment.Near);
        }

        void DrawDropList(Graphics g)
        {
            int rowH = 30;
            var box = new Rectangle(AccountBox.X, AccountBox.Bottom + 2, AccountBox.Width, rowH * _accounts.Count + 4);
            using (var b = new SolidBrush(Color.FromArgb(10, 9, 12)))
                g.FillRectangle(b, box);
            using (var p = new Pen(Gold))
                g.DrawRectangle(p, box);
            var af = _f.Semi(16);
            for (int i = 0; i < _accounts.Count; i++)
            {
                var r = new Rectangle(box.X + 2, box.Y + 2 + i * rowH, box.Width - 3, rowH);
                Hit("acct" + i, r);
                if (Hot("acct" + i) || i == _account)
                    using (var b = new SolidBrush(Hot("acct" + i) ? Color.FromArgb(48, 36, 16) : Color.FromArgb(28, 22, 12)))
                        g.FillRectangle(b, r);
                DrawText(g, Ellipsize(g, _accounts[i].ToString(), af, r.Width - 16), af, Hot("acct" + i) ? GoldHi : Cream, new RectangleF(r.X + 8, r.Y, r.Width - 16, r.Height), StringAlignment.Near, true);
            }
        }

        // ---- drawing helpers ---------------------------------------------------------------------

        static void FillGold(Graphics g, Rectangle r)
        {
            using var b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(236, 196, 92), Color.FromArgb(124, 80, 18), LinearGradientMode.Vertical);
            g.FillRectangle(b, r);
        }

        static void Diamond(Graphics g, float cx, float cy, float r, Color c)
        {
            using var b = new SolidBrush(c);
            g.FillPolygon(b, new[] { new PointF(cx - r, cy), new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r) });
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment align, bool middle = false)
        {
            using var b = new SolidBrush(c);
            using var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            using var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = align, LineAlignment = middle ? StringAlignment.Center : StringAlignment.Near, Trimming = StringTrimming.None };
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            g.DrawString(s, f, shadow, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
            g.DrawString(s, f, b, r, sf);
        }

        /// <summary>Gilded text: a vertical gold gradient with a dark outline, like the in-game titles.</summary>
        static void GoldText(Graphics g, string s, Font f, PointF at)
        {
            using var path = new GraphicsPath();
            using var sf = new StringFormat(StringFormat.GenericTypographic);
            path.AddString(s, f.FontFamily, (int)f.Style, f.Size, at, sf);
            var bounds = path.GetBounds();
            if (bounds.Height <= 0)
                return;
            using (var outline = new Pen(Color.FromArgb(230, 8, 5, 4), 3f) { LineJoin = LineJoin.Round })
                g.DrawPath(outline, path);
            using var brush = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width + 1, bounds.Height + 2), GoldA, GoldC, LinearGradientMode.Vertical)
            {
                InterpolationColors = new ColorBlend { Colors = new[] { GoldA, GoldB, GoldC }, Positions = new[] { 0f, 0.5f, 1f } }
            };
            g.FillPath(brush, path);
        }

        static IEnumerable<string> Wrap(Graphics g, string text, Font f, float width)
        {
            var words = (text ?? "").Split(' ');
            var line = "";
            using var sf = new StringFormat(StringFormat.GenericTypographic);
            foreach (var word in words)
            {
                var test = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && g.MeasureString(test, f, int.MaxValue, sf).Width > width)
                {
                    yield return line;
                    line = word;
                }
                else
                {
                    line = test;
                }
            }
            if (line.Length > 0)
                yield return line;
        }

        static string Ellipsize(Graphics g, string s, Font f, float width)
        {
            using var sf = new StringFormat(StringFormat.GenericTypographic);
            if (g.MeasureString(s, f, int.MaxValue, sf).Width <= width)
                return s;
            while (s.Length > 1 && g.MeasureString(s + "…", f, int.MaxValue, sf).Width > width)
                s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "…";
        }

        // ---- input -------------------------------------------------------------------------------

        string HitTest(Point p)
        {
            if (_dropOpen)
            {
                for (int i = _hits.Count - 1; i >= 0; i--)
                    if (_hits[i].Id.StartsWith("acct") && _hits[i].Rect.Contains(p))
                        return _hits[i].Id;
            }
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].Rect.Contains(p))
                    return _hits[i].Id;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragThumb)
            {
                var thumb = ThumbRect();
                int travel = ScrollTrack.Height - thumb.Height;
                if (travel > 0)
                    SetScroll(_dragStartScroll + (e.Y - _dragStartY) * MaxScroll / travel);
                return;
            }
            var hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null)
            {
                _hover = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            var hit = HitTest(e.Location);
            _pressed = hit;

            if (hit == "thumb")
            {
                var thumb = ThumbRect();
                if (!thumb.Contains(e.Location))
                    SetScroll(_scroll + (e.Y < thumb.Y ? -1 : 1) * NotesView.Height * 3 / 4);
                _dragThumb = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
                Capture = true;
                return;
            }

            if (hit == null && !_dropOpen && Banner.Contains(e.Location))
            {
                // drag the window by its banner
                ReleaseCapture();
                SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
                return;
            }

            if (e.Clicks == 2 && hit != null && hit.StartsWith("shard"))
            {
                Connect();
                return;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragThumb)
            {
                _dragThumb = false;
                Capture = false;
                Invalidate();
                return;
            }
            var hit = HitTest(e.Location);
            var pressed = _pressed;
            _pressed = null;
            if (e.Button != MouseButtons.Left || hit != pressed)
            {
                Invalidate();
                return;
            }

            if (_dropOpen)
            {
                _dropOpen = false;
                if (hit != null && hit.StartsWith("acct"))
                    _account = int.Parse(hit.Substring(4));
                Invalidate();
                return;
            }

            switch (hit)
            {
                case "close":
                    Close();
                    break;
                case "min":
                    WindowState = FormWindowState.Minimized;
                    break;
                case "tab0":
                case "tab1":
                    _tab = hit == "tab0" ? 0 : 1;
                    break;
                case "account":
                    _dropOpen = _accounts.Count > 1;
                    break;
                case "skip":
                    LauncherData.SkipWindow = !LauncherData.SkipWindow;
                    break;
                case "play":
                    if (_tab == 0)
                        Play();
                    else
                        Connect();
                    break;
                case "add":
                    EditShard(null);
                    break;
                case "edit":
                    if (_shard >= 0)
                        EditShard(_shards[_shard]);
                    break;
                case "remove":
                    RemoveShard();
                    break;
                default:
                    if (hit != null && hit.StartsWith("link"))
                        OpenLink(_links[int.Parse(hit.Substring(4))].Url);
                    else if (hit != null && hit.StartsWith("shard"))
                        _shard = int.Parse(hit.Substring(5));
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_tab == 0)
                SetScroll(_scroll - e.Delta / 120 * 46);
        }

        void SetScroll(int value)
        {
            value = Math.Max(0, Math.Min(MaxScroll, value));
            if (value != _scroll)
            {
                _scroll = value;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                if (_dropOpen)
                {
                    _dropOpen = false;
                    Invalidate();
                }
                else
                {
                    Close();
                }
            }
            else if (e.KeyCode == Keys.Enter && _updateDone)
            {
                if (_tab == 0)
                    Play();
                else
                    Connect();
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown)
            {
                SetScroll(_scroll + (e.KeyCode == Keys.Down ? 46 : NotesView.Height * 3 / 4));
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp)
            {
                SetScroll(_scroll - (e.KeyCode == Keys.Up ? 46 : NotesView.Height * 3 / 4));
            }
        }

        // ---- actions -----------------------------------------------------------------------------

        void Play()
        {
            if (!_updateDone)
                return;
            var extra = new List<string>();
            if (_accounts.Count > 0)
            {
                var a = _accounts[_account];
                extra.AddRange(LauncherData.AccountArgs(a));
                if (a.Name != null)
                    LauncherData.LastAccount = a.Name;
            }
            Result = _args.Concat(extra).ToArray();
            Close();
        }

        void Connect()
        {
            if (!_updateDone || _shard < 0 || _shard >= _shards.Count)
                return;
            Result = _args.Concat(LauncherData.ShardArgs(_shards[_shard])).ToArray();
            Close();
        }

        void EditShard(ShardEntry existing)
        {
            using var dialog = new ShardDialog(existing, _f);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            if (existing == null)
            {
                _shards.Add(dialog.Entry);
                _shard = _shards.Count - 1;
            }
            else
            {
                _shards[_shard] = dialog.Entry;
            }
            LauncherData.SaveShards(_shards);
        }

        void RemoveShard()
        {
            if (_shard < 0)
                return;
            if (MessageBox.Show(this, "Remove " + _shards[_shard].Name + " from the list?\n\nIts settings file stays in the Shards folder.", "In Ex Xen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _shards.RemoveAt(_shard);
            _shard = Math.Min(_shard, _shards.Count - 1);
            LauncherData.SaveShards(_shards);
        }

        static void OpenLink(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>The launcher's fonts, embedded in the exe (EB Garamond and Grenze Gotisch, both OFL).</summary>
    sealed class Fonts : IDisposable
    {
        readonly PrivateFontCollection _collection = new PrivateFontCollection();
        readonly List<IntPtr> _memory = new List<IntPtr>();
        readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();
        FontFamily _body, _semi, _gothic;

        public Fonts()
        {
            _body = Load("Garamond.ttf", "IEX Garamond");
            _semi = Load("GaramondSemiBold.ttf", "IEX Garamond SemiBold");
            _gothic = Load("Grenze.ttf", "IEX Grenze");
        }

        FontFamily Load(string resource, string family)
        {
            try
            {
                var bytes = Resources.Bytes(resource);
                if (bytes != null)
                {
                    var mem = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, mem, bytes.Length);
                    _memory.Add(mem);
                    _collection.AddMemoryFont(mem, bytes.Length);
                    // GDI+ may append the weight to the family name ("IEX Grenze SemiBold")
                    var found = _collection.Families.FirstOrDefault(f => f.Name == family)
                        ?? _collection.Families.FirstOrDefault(f => f.Name.StartsWith(family + " "));
                    if (found != null)
                        return found;
                }
            }
            catch { }
            return FontFamily.GenericSerif;
        }

        Font Get(FontFamily family, float px, FontStyle style = FontStyle.Regular)
        {
            var key = family.Name + "|" + px + "|" + style;
            if (!_cache.TryGetValue(key, out var f))
                _cache[key] = f = new Font(family, px, style, GraphicsUnit.Pixel);
            return f;
        }

        public Font Body(float px) => Get(_body, px);
        public Font Semi(float px) => Get(_semi, px);
        public Font Bold(float px) => Get(_semi, px);
        public Font Gothic(float px) => Get(_gothic, px);

        public void Dispose()
        {
            foreach (var f in _cache.Values)
                f.Dispose();
            _cache.Clear();
            _collection.Dispose();
            foreach (var m in _memory)
                Marshal.FreeCoTaskMem(m);
            _memory.Clear();
        }
    }

    static class Resources
    {
        public static byte[] Bytes(string name)
        {
            using var s = typeof(Resources).Assembly.GetManifestResourceStream("IEX." + name);
            if (s == null)
                return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public static Image Image(string name)
        {
            var bytes = Bytes(name);
            if (bytes == null)
                return null;
            using var ms = new MemoryStream(bytes);
            using var img = System.Drawing.Image.FromStream(ms);
            return new Bitmap(img); // detached from the stream
        }
    }
}
