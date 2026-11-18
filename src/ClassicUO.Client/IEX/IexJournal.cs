// The shard's tabbed journal (Options "Use alternate journal"): stock's TazUO-style journal
// redone so it follows the In Ex Xen options.
//   - tabs from the profile (Profile.JournalTabs, shared with stock's ResizableJournal):
//     left-click to pick, right-click for the message types it shows or to delete it, + to add
//   - a search box that filters the current tab's lines as you type
//   - the "Text size" option (lines rewrap to the window), the window theme (Dark / Light
//     frame, colours kept readable), resizable from the bottom-right corner
//   - a "Journal" title plate and margins clear of the frame's trim, as the skills window

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility.Collections;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal sealed class IexJournal : Gump
    {
        const int MinWidth = 260, MinHeight = 180;
        // Inset clear of the frame's gold trim, as the skills window (IexSkillsGump: Frame 13 + 4).
        const int Frame = 17, Bottom = 21, TabHeight = 20, TabGap = 5, SearchWidth = 140, ScrollWidth = 16;
        const int LineGap = 2; // between journal entries

        static readonly MessageType[] MenuTypes =
        {
            MessageType.Regular, MessageType.System, MessageType.Emote, MessageType.Label,
            MessageType.Focus, MessageType.Whisper, MessageType.Yell, MessageType.Spell,
            MessageType.Guild, MessageType.Alliance, MessageType.Command, MessageType.Encoded,
            MessageType.Party, MessageType.Limit3Spell
        };

        // Kept while the window is closed, like stock.
        static int _lastWidth = 420, _lastHeight = 300;
        static string _lastTab;

        readonly Deque<Line> _lines = new Deque<Line>();
        string _tab;
        string _search = string.Empty;

        StbTextBox _searchBox;
        ScrollBar _scroll;
        Lines _text;

        bool _resizing;
        Point _resizeMouse, _resizeSize;

        public IexJournal(World world) : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            WantUpdateSize = false;
            X = 100;
            Y = 100;
            Width = _lastWidth;
            Height = _lastHeight;
            _tab = _lastTab;

            foreach (JournalEntry e in JournalManager.Entries)
            {
                AddEntry(e);
            }

            world.Journal.EntryAdded += OnEntryAdded;
            Build();
        }

        public override GumpType GumpType => GumpType.Journal;

        static Dictionary<string, MessageType[]> Tabs => ProfileManager.CurrentProfile.JournalTabs;

        MessageType[] Filter => _tab != null && Tabs.TryGetValue(_tab, out MessageType[] f) ? f : null;

        // ------------------------------------------------------------------ layout

        void Build()
        {
            bool searchFocused = _searchBox != null && UIManager.KeyboardFocusControl == _searchBox;
            int scrollValue = _scroll?.Value ?? 0;
            bool atBottom = _scroll == null || _scroll.Value >= _scroll.MaxValue;

            foreach (Control c in Children.ToArray())
            {
                Remove(c);
                c.Dispose();
            }

            if (_tab == null || !Tabs.ContainsKey(_tab))
            {
                _tab = Tabs.Keys.FirstOrDefault();
            }

            // Wide enough for every tab and the + button.
            int tabsWidth = Frame + 23 + Frame;

            foreach (string name in Tabs.Keys)
            {
                tabsWidth += Client.Game.UO.FileManager.Fonts.GetWidthUnicode(1, name) + 16 + TabGap;
            }

            Width = _lastWidth = Math.Max(Width, tabsWidth);

            Add(new ResizePic(0x0E10) { Width = Width, Height = Height });

            // Title plate, as the skills window's.
            int y = Frame - 6;
            GumpPic title = IexDarkUi.TitlePic("Journal");

            if (title != null)
            {
                var plate = new ResizePic(0x0BB8) { Width = title.Width + 36, Height = title.Height + 10, AcceptMouseInput = false };
                plate.X = (Width - plate.Width) / 2;
                plate.Y = y;
                title.X = plate.X + 18;
                title.Y = plate.Y + 5;
                Add(plate);
                Add(title);
                y += plate.Height + 8;
            }
            else
            {
                y = Frame;
            }

            // Tabs, then the + button; the search box goes at the end of the row when it fits,
            // otherwise on a row of its own.
            int x = Frame;

            foreach (string name in Tabs.Keys.ToArray())
            {
                x += AddTab(x, y, name) + TabGap;
            }

            AddPlus(x, y);
            x += 23;

            if (Width - Frame - SearchWidth - 6 >= x)
            {
                AddSearch(Width - Frame - SearchWidth, y, SearchWidth);
                y += TabHeight + 8;
            }
            else
            {
                y += TabHeight + 6;
                AddSearch(Frame, y, Width - 2 * Frame);
                y += TabHeight + 8;
            }

            // Text panel and scroll bar, with room inside the panel's edge.
            int panelW = Width - 2 * Frame, panelH = Height - y - Bottom;

            if (IexDarkUi.Active)
            {
                Add(new ResizePic(0x0BB8) { X = Frame, Y = y, Width = panelW, Height = panelH, AcceptMouseInput = false });
            }
            else
            {
                Add(new AlphaBlendControl(0.7f) { X = Frame, Y = y, Width = panelW, Height = panelH, Hue = 0, AcceptMouseInput = false });
            }

            _scroll = new ScrollBar(Frame + panelW - ScrollWidth - 4, y + 4, panelH - 8) { MinValue = 0 };
            _text = new Lines(this, Frame + 8, y + 6, panelW - ScrollWidth - 8 - 4 - 8, panelH - 12);
            Add(_text);
            Add(_scroll);
            _text.Measure();
            _scroll.Value = atBottom ? _scroll.MaxValue : Math.Min(scrollValue, _scroll.MaxValue);

            // Resize grip, bottom-right (gold art with a theme on, a plain corner without).
            Control grip = IexDarkUi.Active
                ? new GumpPic(Width - 12, Height - 12, IexDarkArt.ResizeGrip, 0) { AcceptMouseInput = true, CanMove = false }
                : new HitBox(Width - 10, Height - 10, 10, 10, alpha: 0.35f);
            grip.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left && !IexLocked) // locked: no resizing (IexWindowLock)
                {
                    _resizing = true;
                    _resizeMouse = Mouse.Position;
                    _resizeSize = new Point(Width, Height);
                }
            };
            Add(grip);

            if (searchFocused)
            {
                _searchBox.SetKeyboardFocus();
            }
        }

        int AddTab(int x, int y, string name)
        {
            bool selected = name == _tab;
            ushort hue = IexDarkUi.Active ? (selected ? IexDarkUi.AccentHue : IexDarkUi.DimHue) : (selected ? (ushort)0x0035 : (ushort)0x0481);
            var label = new Label(name, true, hue, font: 1) { AcceptMouseInput = false };
            int w = label.Width + 16;
            Add(new ResizePic(selected ? (ushort)0x0E10 : (ushort)0x0BB8) { X = x, Y = y, Width = w, Height = TabHeight, AcceptMouseInput = false });
            label.X = x + 8;
            label.Y = y + 2;
            Add(label);

            var hit = new HitBox(x, y, w, TabHeight, alpha: 0.1f) { CanCloseWithRightClick = false }; // right-click opens the tab menu
            hit.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    _tab = name;
                    Build();
                    _scroll.Value = _scroll.MaxValue;
                }
                else if (e.Button == MouseButtonType.Right)
                {
                    TabMenu(name).Show();
                }
            };
            Add(hit);
            return w;
        }

        void AddPlus(int x, int y)
        {
            Add(new ResizePic(0x0BB8) { X = x, Y = y, Width = 20, Height = TabHeight, AcceptMouseInput = false });
            Add(new Label("+", true, IexDarkUi.Active ? IexDarkUi.AccentHue : (ushort)0x0481, font: 1) { X = x + 7, Y = y + 2, AcceptMouseInput = false });
            var hit = new HitBox(x, y, 20, TabHeight, "Add a tab", 0.1f);
            hit.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtonType.Left)
                {
                    return;
                }

                UIManager.Add(new EntryDialog(World, 250, 150, "Enter a tab name", name =>
                {
                    name = name?.Trim();

                    if (!string.IsNullOrEmpty(name) && !Tabs.ContainsKey(name))
                    {
                        Tabs.Add(name, new[] { MessageType.Regular });
                        _tab = name;
                        Build();
                    }
                }));
            };
            Add(hit);
        }

        void AddSearch(int x, int y, int w)
        {
            Add(new ResizePic(0x0BB8) { X = x, Y = y, Width = w, Height = TabHeight, AcceptMouseInput = false });

            var hint = new Label("Search...", true, IexDarkUi.Active ? IexDarkUi.DimHue : (ushort)0x0481, font: 1)
            {
                X = x + 6, Y = y + 3, AcceptMouseInput = false, IsVisible = _search.Length == 0
            };
            Add(hint);

            _searchBox = new StbTextBox(1, 60, w - 10, true, hue: IexDarkUi.Active ? IexDarkUi.TextHue : (ushort)0xFFFF)
            {
                X = x + 5,
                Y = y + 2,
                Width = w - 10,
                Height = 16
            };
            _searchBox.SetText(_search);
            _searchBox.TextChanged += (s, e) =>
            {
                _search = _searchBox.Text ?? string.Empty;
                hint.IsVisible = _search.Length == 0;
                _text.Measure();
                _scroll.Value = _scroll.MaxValue;
            };
            Add(_searchBox);
        }

        ContextMenuControl TabMenu(string name)
        {
            var menu = new ContextMenuControl(this);

            if (!Tabs.TryGetValue(name, out MessageType[] types))
            {
                return menu;
            }

            foreach (MessageType type in MenuTypes)
            {
                MessageType t = type;
                menu.Add(TypeName(t), () =>
                {
                    if (Tabs.TryGetValue(name, out MessageType[] now))
                    {
                        Tabs[name] = now.Contains(t) ? now.Where(m => m != t).ToArray() : now.Append(t).ToArray();
                        _text.Measure();
                    }
                }, true, types.Contains(t));
            }

            menu.Add("X Delete Tab", () =>
            {
                UIManager.Add(new QuestionGump(World, $"Delete [{name}] tab?", yes =>
                {
                    if (yes && Tabs.Remove(name))
                    {
                        Build();
                    }
                }));
            });

            return menu;
        }

        static string TypeName(MessageType t) => t == MessageType.Limit3Spell ? "Limit3Spell(Sphere)" : t.ToString();

        // ------------------------------------------------------------------ entries

        void OnEntryAdded(object sender, JournalEntry e) => AddEntry(e);

        void AddEntry(JournalEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            if (!string.IsNullOrEmpty(e.Name) && World.IgnoreManager.IgnoredCharsList.Contains(e.Name))
            {
                return;
            }

            while (_lines.Count >= Constants.MAX_JOURNAL_HISTORY_COUNT)
            {
                _lines.RemoveFromFront().Destroy();
            }

            bool atBottom = _scroll == null || _scroll.Value >= _scroll.MaxValue;
            _lines.AddToBack(new Line(e));

            if (_text != null)
            {
                _text.Measure();

                if (atBottom)
                {
                    _scroll.Value = _scroll.MaxValue;
                }
            }
        }

        bool Shows(Line line)
        {
            MessageType[] filter = Filter;

            if (filter != null)
            {
                bool system = line.Entry.TextType == TextType.SYSTEM;
                bool ok = system ? filter.Contains(MessageType.System) : filter.Contains(line.Entry.MessageType);

                if (!ok)
                {
                    return false;
                }
            }

            return _search.Length == 0 || line.Text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Called by the Options "Text size" change: rewrap at the new size.
        public void IexReload()
        {
            foreach (Line l in _lines)
            {
                l.Destroy();
            }

            _text?.Measure();
            _scroll.Value = _scroll.MaxValue;
        }

        // ------------------------------------------------------------------ input, resize, save

        protected override void OnMouseWheel(MouseEventType delta)
        {
            base.OnMouseWheel(delta);
            _scroll?.InvokeMouseWheel(delta);
        }

        public override void Update()
        {
            base.Update();

            if (!_resizing || IsDisposed)
            {
                return;
            }

            Point d = Mouse.Position - _resizeMouse;
            int w = Math.Max(MinWidth, _resizeSize.X + d.X), h = Math.Max(MinHeight, _resizeSize.Y + d.Y);

            if (!Mouse.LButtonPressed)
            {
                _resizing = false;
            }

            // Rebuild in small steps while dragging, and once more on release.
            if (!_resizing ? w != Width || h != Height : Math.Abs(w - Width) >= 4 || Math.Abs(h - Height) >= 4)
            {
                Width = _lastWidth = w;
                Height = _lastHeight = h;
                Build();
            }
        }

        public override void Dispose()
        {
            World.Journal.EntryAdded -= OnEntryAdded;
            _lastTab = _tab;

            foreach (Line l in _lines)
            {
                l.Destroy();
            }

            _lines.Clear();
            base.Dispose();
        }

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);
            writer.WriteAttributeString("iexw", Width.ToString());
            writer.WriteAttributeString("iexh", Height.ToString());

            if (_tab != null)
            {
                writer.WriteAttributeString("iextab", _tab);
            }
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            if (int.TryParse(xml.GetAttribute("iexw"), out int w) && int.TryParse(xml.GetAttribute("iexh"), out int h))
            {
                Width = _lastWidth = Math.Max(MinWidth, w);
                Height = _lastHeight = Math.Max(MinHeight, h);
            }

            string tab = xml.GetAttribute("iextab");

            if (!string.IsNullOrEmpty(tab))
            {
                _tab = tab;
            }

            Build();
        }

        // ------------------------------------------------------------------ one journal line

        sealed class Line
        {
            public readonly JournalEntry Entry;
            public readonly string Text;
            RenderedText _body, _hour;
            int _wrap;

            public Line(JournalEntry e)
            {
                Entry = e;
                Text = string.IsNullOrEmpty(e.Name) ? e.Text : $"{e.Name}: {e.Text}";
            }

            // Text drawn at the current theme and wrapped to `width` on-screen pixels.
            public void Prepare(int width, float scale, out RenderedText hour, out RenderedText body)
            {
                if (_body == null || _wrap != width)
                {
                    Destroy();
                    FontStyle edge = IexDarkUi.Light ? FontStyle.None : FontStyle.BlackBorder;
                    _hour = RenderedText.Create($"{Entry.Time:t} ", IexDarkUi.Active ? IexDarkUi.DimHue : (ushort)1150, 1, true, edge);
                    int textWidth = Math.Max(40, (int)(width / scale) - _hour.Width);
                    _body = RenderedText.Create(Text, IexDarkUi.ReadableHue(Entry.Hue), Entry.Font, Entry.IsUnicode,
                        FontStyle.Indention | edge, maxWidth: textWidth);
                    _wrap = width;
                }

                hour = _hour;
                body = _body;
            }

            public void Destroy()
            {
                _body?.Destroy();
                _hour?.Destroy();
                _body = _hour = null;
                _wrap = 0;
            }
        }

        // The text area: draws the lines that pass the tab and search, clipped and scrolled.
        sealed class Lines : Control
        {
            readonly IexJournal _owner;
            readonly List<(RenderedText Hour, RenderedText Body)> _shown = new List<(RenderedText, RenderedText)>();
            float _scale;

            public Lines(IexJournal owner, int x, int y, int w, int h)
            {
                _owner = owner;
                X = x;
                Y = y;
                Width = w;
                Height = h;
                AcceptMouseInput = true;
                CanMove = true;
                CanCloseWithRightClick = true;
                WantUpdateSize = false;
            }

            // Recollect the lines to show and set the scroll range. Keeps the bottom pinned
            // when the view was at the bottom.
            public void Measure()
            {
                ScrollBar scroll = _owner._scroll;
                bool atBottom = scroll.Value >= scroll.MaxValue;

                _scale = IexText.Scale;
                _shown.Clear();
                int total = 0;

                foreach (Line line in _owner._lines)
                {
                    if (!_owner.Shows(line))
                    {
                        continue;
                    }

                    line.Prepare(Width, _scale, out RenderedText hour, out RenderedText body);
                    _shown.Add((hour, body));
                    total += (int)(body.Height * _scale) + LineGap;
                }

                scroll.MaxValue = Math.Max(0, total - LineGap - Height);
                scroll.IsVisible = scroll.MaxValue > 0;

                if (atBottom || scroll.Value > scroll.MaxValue)
                {
                    scroll.Value = scroll.MaxValue;
                }
            }

            protected override void OnMouseWheel(MouseEventType delta) => _owner._scroll?.InvokeMouseWheel(delta);

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
                float layerDepth = layerDepthRef;
                int offset = _owner._scroll?.Value ?? 0;
                float scale = _scale;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    if (!batcher.ClipBegin(x, y, Width, Height))
                    {
                        return true;
                    }

                    int top = -offset;

                    foreach ((RenderedText hour, RenderedText body) in _shown)
                    {
                        int h = (int)(body.Height * scale);

                        if (top + h > 0 && top < Height && !body.IsDestroyed)
                        {
                            hour.Draw(batcher, x, y + top, layerDepth, 1f, 0, scale);
                            body.Draw(batcher, x + (int)(hour.Width * scale), y + top, layerDepth, 1f, 0, scale);
                        }

                        top += h + LineGap;

                        if (top >= Height)
                        {
                            break;
                        }
                    }

                    batcher.ClipEnd();
                    return true;
                });

                return true;
            }
        }
    }
}
