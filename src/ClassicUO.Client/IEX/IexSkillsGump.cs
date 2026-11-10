// The shard's skills window (used while "Dark windows" and "Modern skills window" are on):
// resizable, a search box, Real/Base/Cap tabs, a groups sidebar (folds into a dropdown when the
// window is narrow), one row per skill with a use button, the value and a lock button.
// No progress bars or gain counts - when the next gain comes is left a surprise.
//
//   drag a skill onto a group      file it there
//   drag a usable skill outside    make a skill button, like the stock window
//   double-click a group           rename it (Enter to keep)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Network;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal sealed class IexSkillsGump : Gump
    {
        public static bool Use => IexDarkUi.Active && (ProfileManager.CurrentProfile?.IexModernSkills ?? false);

        const int MinWidth = 230, MinHeight = 220, WideFrom = 330;
        const int Frame = 13, RowHeight = 17;
        const float TotalCap = 700f; // T2A skill cap
        static readonly string[] ShowNames = { "Real", "Base", "Cap" };

        // Views: 0 = usable, 1 = all, 2.. = the player's groups.
        int _view;
        int _show; // index into ShowNames
        string _search = string.Empty;
        int _renaming = -1;

        StbTextBox _searchBox;
        ScrollArea _list;
        DataBox _rows;
        Label _total;

        bool _resizing;
        Point _resizeMouse, _resizeSize;
        int _dragSkill = -1;
        Point _dragFrom;
        readonly List<(Control Hit, int View)> _groupTargets = new List<(Control, int)>();

        public IexSkillsGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            WantUpdateSize = false;
            Width = 380;
            Height = 300;
            Build();
        }

        public override GumpType GumpType => GumpType.SkillMenu;

        bool Wide => Width >= WideFrom;

        // ------------------------------------------------------------------ layout

        void Build()
        {
            bool searchFocused = _searchBox != null && UIManager.KeyboardFocusControl == _searchBox;
            _groupTargets.Clear();
            RemoveAll(this);

            Add(new ResizePic(0x0E10) { Width = Width, Height = Height });

            // Title plate
            GumpPic title = IexDarkUi.TitlePic("Skills");
            int plateH = 0;

            if (title != null)
            {
                var plate = new ResizePic(0x0BB8) { Width = title.Width + 36, Height = title.Height + 10, AcceptMouseInput = false };
                plate.X = (Width - plate.Width) / 2;
                plate.Y = Frame - 2;
                title.X = plate.X + 18;
                title.Y = plate.Y + 5;
                Add(plate);
                Add(title);
                plateH = plate.Height;
            }

            _total = new Label(TotalText(), true, IexDarkUi.GoldHue, font: 1) { AcceptMouseInput = false };

            if (Wide)
            {
                _total.X = Width - Frame - 8 - _total.Width;
                _total.Y = Frame + 4;
            }

            Add(_total);

            // Toolbar: search, then Real/Base/Cap (tabs when wide, a dropdown when narrow).
            int y = Frame + plateH + 6;
            int searchW = Wide ? 120 : Width - 2 * Frame - 18 - _total.Width;
            AddSearch(Frame + 4, y, searchW);

            if (Wide)
            {
                int x = Frame + 4 + searchW + 14;
                var show = new Label("Show:", true, 0x03B2, font: 1) { X = x, Y = y + 3 };
                Add(show);
                x += show.Width + 6;

                for (int i = 0; i < ShowNames.Length; i++)
                {
                    x += AddTab(x, y, ShowNames[i], i == _show, i) + 4;
                }

                y += 26;
            }
            else
            {
                _total.X = Width - Frame - 6 - _total.Width;
                _total.Y = y + 3;
                y += 24;

                var views = new List<string> { "Usable", "All" };
                views.AddRange(World.SkillsGroupManager.Groups.Select(g => g.Name));
                var groups = new Combobox(Frame + 4, y, 110, views.ToArray(), _view, font: 1);
                groups.OnOptionSelected += (s, i) => { _view = i; RebuildList(); };
                Add(groups);

                var show = new Combobox(Frame + 122, y, 64, ShowNames, _show, font: 1);
                show.OnOptionSelected += (s, i) => { _show = i; RebuildList(); };
                Add(show);
                y += 28;
            }

            int top = y, bottom = Height - Frame - 8;
            int listX = Frame + 4;

            if (Wide)
            {
                const int sidebar = 96;
                Add(new ResizePic(0x0BB8) { X = listX, Y = top, Width = sidebar, Height = bottom - top, AcceptMouseInput = false });
                AddGroups(listX, top, sidebar, bottom);
                listX += sidebar + 6;
            }

            int listW = Width - Frame - 4 - listX;
            Add(new ResizePic(0x0BB8) { X = listX, Y = top, Width = listW, Height = bottom - top, AcceptMouseInput = false });
            _list = new ScrollArea(listX + 4, top + 4, listW - 6, bottom - top - 8, true) { AcceptMouseInput = true, CanMove = true };
            _rows = new DataBox(0, 0, listW - 22, 1) { AcceptMouseInput = true, CanMove = true, WantUpdateSize = false };
            _list.Add(_rows);
            Add(_list);
            RebuildList();

            // Resize grip, bottom-right.
            var grip = new GumpPic(Width - 12, Height - 12, IexDarkArt.ResizeGrip, 0) { AcceptMouseInput = true, CanMove = false };
            grip.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
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

        void AddSearch(int x, int y, int w)
        {
            Add(new ResizePic(0x0BB8) { X = x, Y = y, Width = w, Height = 20, AcceptMouseInput = false });

            var hint = new Label("Search...", true, 0x03B2, font: 1) { X = x + 6, Y = y + 3, AcceptMouseInput = false, IsVisible = _search.Length == 0 };
            Add(hint);

            _searchBox = new StbTextBox(1, 30, w - 10, true, hue: IexDarkUi.ParchmentHue)
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
                RebuildList();
            };
            Add(_searchBox);
        }

        int AddTab(int x, int y, string text, bool selected, int index)
        {
            var label = new Label(text, true, selected ? IexDarkUi.GoldHue : (ushort)0x03B2, font: 1) { AcceptMouseInput = false };
            int w = label.Width + 14;
            Add(new ResizePic(selected ? (ushort)0x0E10 : (ushort)0x0BB8) { X = x, Y = y, Width = w, Height = 20, AcceptMouseInput = false });
            label.X = x + 7;
            label.Y = y + 2;
            Add(label);
            var hit = new HitBox(x, y, w, 20, alpha: 0.1f);
            hit.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    _show = index;
                    Build();
                }
            };
            Add(hit);
            return w;
        }

        void AddGroups(int x, int top, int w, int bottom)
        {
            int y = top + 5;
            var names = new List<string> { "Usable", "All" };
            names.AddRange(World.SkillsGroupManager.Groups.Select(g => g.Name));

            for (int i = 0; i < names.Count && y + 16 < bottom - 22; i++, y += 18)
            {
                int view = i;

                if (view == _renaming)
                {
                    var box = new StbTextBox(1, 24, w - 12, true, hue: IexDarkUi.ParchmentHue) { X = x + 6, Y = y, Width = w - 12, Height = 16 };
                    box.SetText(names[i]);
                    Add(box);
                    box.SetKeyboardFocus();
                    continue;
                }

                Add(new Label(names[i], true, view == _view ? IexDarkUi.GoldHue : IexDarkUi.ParchmentHue, w - 12, font: 1, style: FontStyle.Cropped)
                {
                    X = x + 6,
                    Y = y,
                    AcceptMouseInput = false
                });

                var hit = new HitBox(x + 2, y - 1, w - 4, 17, alpha: 0.1f);
                hit.MouseUp += (s, e) =>
                {
                    if (e.Button != MouseButtonType.Left)
                    {
                        return;
                    }

                    _view = view;
                    Build();
                };
                hit.MouseDoubleClick += (s, e) =>
                {
                    if (view >= 2)
                    {
                        _renaming = view;
                        Build();
                    }
                };
                Add(hit);
                _groupTargets.Add((hit, view));
            }

            var add = new Label("+ New group", true, IexDarkUi.GoldHue, font: 1) { X = x + 6, Y = bottom - 20, AcceptMouseInput = false };
            Add(add);
            var addHit = new HitBox(x + 2, bottom - 21, w - 4, 17, alpha: 0.1f);
            addHit.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    World.SkillsGroupManager.Add(new SkillsGroup { Name = "New group" });
                    _renaming = _view = World.SkillsGroupManager.Groups.Count + 1;
                    Build();
                }
            };
            Add(addHit);
        }

        // ------------------------------------------------------------------ rows

        IEnumerable<Skill> Visible()
        {
            Skill[] skills = World.Player?.Skills;

            if (skills == null)
            {
                return Enumerable.Empty<Skill>();
            }

            IEnumerable<Skill> list;

            if (_search.Length > 0)
            {
                list = skills.Where(s => s != null && s.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            else if (_view == 0)
            {
                list = skills.Where(s => s != null && s.IsClickable);
            }
            else if (_view >= 2 && _view - 2 < World.SkillsGroupManager.Groups.Count)
            {
                SkillsGroup g = World.SkillsGroupManager.Groups[_view - 2];
                var inGroup = new List<Skill>();

                for (int i = 0; i < g.Count; i++)
                {
                    byte index = g.GetSkill(i);

                    if (index < skills.Length && skills[index] != null)
                    {
                        inGroup.Add(skills[index]);
                    }
                }

                list = inGroup;
            }
            else
            {
                list = skills.Where(s => s != null);
            }

            list = list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

            // Narrow: usable skills first, they are what gets clicked.
            return Wide ? list : list.OrderBy(s => s.IsClickable ? 0 : 1);
        }

        void RebuildList()
        {
            if (_rows == null)
            {
                return;
            }

            RemoveAll(_rows);
            int y = 0, w = _rows.Width;

            foreach (Skill skill in Visible())
            {
                _rows.Add(Row(skill, y, w));
                y += RowHeight;
            }

            _rows.Height = Math.Max(1, y);
            _total.Text = TotalText();
        }

        Control Row(Skill skill, int y, int w)
        {
            var row = new RowControl { X = 0, Y = y, Width = w, Height = RowHeight };
            int index = skill.Index;

            if (skill.IsClickable)
            {
                var use = new Button(0, IexDarkArt.UseButton, IexDarkArt.UseButtonPressed, IexDarkArt.UseButton) { X = 3, Y = 3, ButtonAction = ButtonAction.Activate };
                use.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtonType.Left)
                    {
                        GameActions.UseSkill(index);
                    }
                };
                row.Add(use);
            }

            row.Add(new Label(skill.Name, true, IexDarkUi.ParchmentHue, w - 80, font: 1, style: FontStyle.Cropped) { X = 18, Y = 0, AcceptMouseInput = false });

            var value = new Label(Value(skill).ToString("F1"), true, IexDarkUi.ParchmentHue, font: 1) { Y = 0, AcceptMouseInput = false };
            value.X = w - 18 - value.Width;
            row.Add(value);

            var lockButton = new Button(1, LockArt(skill.Lock), LockArt(skill.Lock), LockArt(skill.Lock)) { X = w - 11, Y = 4, ButtonAction = ButtonAction.Activate };
            lockButton.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtonType.Left || World.Player == null)
                {
                    return;
                }

                Skill sk = World.Player.Skills[index];
                byte next = (byte)(((byte)sk.Lock + 1) % 3);
                NetClient.Socket.Send_SkillStatusChangeRequest((ushort)index, next);
                sk.Lock = (Lock)next;
                RebuildList();
            };
            row.Add(lockButton);

            row.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    _dragSkill = index;
                    _dragFrom = Mouse.Position;
                }
            };

            return row;
        }

        // Take the old controls out now. Clear() only marks them disposed and leaves them in place
        // until later, and a disposed scroll area drawn in the same frame crashes the client.
        static void RemoveAll(Control parent)
        {
            foreach (Control c in parent.Children.ToArray())
            {
                parent.Remove(c);
                c.Dispose();
            }
        }

        float Value(Skill s) => _show == 1 ? s.Base : _show == 2 ? s.Cap : s.Value;

        static ushort LockArt(Lock l) => l == Lock.Down ? IexDarkArt.LockDown : l == Lock.Locked ? IexDarkArt.LockLocked : IexDarkArt.LockUp;

        string TotalText()
        {
            float sum = World.Player?.Skills?.Where(s => s != null).Sum(s => s.Value) ?? 0f;
            return $"{sum:F1} / {TotalCap:F0}";
        }

        /// <summary>Called when the server sends new skill values.</summary>
        public void RefreshValues() => RebuildList();

        // ------------------------------------------------------------------ dragging and renaming

        void DropSkill(int view)
        {
            int index = _dragSkill;
            _dragSkill = -1;

            if (view < 2 || index < 0 || index > byte.MaxValue)
            {
                return;
            }

            // A skill lives in one group: take it out of the others first.
            foreach (SkillsGroup g in World.SkillsGroupManager.Groups)
            {
                if (g.Contains((byte)index))
                {
                    g.Remove((byte)index);
                }
            }

            SkillsGroup target = World.SkillsGroupManager.Groups[view - 2];
            target.Add((byte)index);
            target.Sort();
            RebuildList();
        }

        public override void OnKeyboardReturn(int textID, string text)
        {
            if (_renaming >= 2 && _renaming - 2 < World.SkillsGroupManager.Groups.Count && !string.IsNullOrWhiteSpace(text))
            {
                World.SkillsGroupManager.Groups[_renaming - 2].Name = text.Trim();
            }

            _renaming = -1;
            Build();
        }

        public override void Update()
        {
            base.Update();

            if (_resizing)
            {
                if (!Mouse.LButtonPressed)
                {
                    _resizing = false;
                    Point d = Mouse.Position - _resizeMouse;
                    int w = Math.Max(MinWidth, _resizeSize.X + d.X), h = Math.Max(MinHeight, _resizeSize.Y + d.Y);

                    if (w != Width || h != Height)
                    {
                        Width = w;
                        Height = h;
                        Build();
                    }
                }
                else
                {
                    Point d = Mouse.Position - _resizeMouse;
                    int w = Math.Max(MinWidth, _resizeSize.X + d.X), h = Math.Max(MinHeight, _resizeSize.Y + d.Y);

                    // Rebuild in small steps, not every pixel of the drag.
                    if (Math.Abs(w - Width) >= 4 || Math.Abs(h - Height) >= 4)
                    {
                        Width = w;
                        Height = h;
                        Build();
                    }
                }
            }

            if (_dragSkill >= 0 && !Mouse.LButtonPressed)
            {
                int skill = _dragSkill;
                _dragSkill = -1;
                Point moved = Mouse.Position - _dragFrom;

                if (Math.Abs(moved.X) + Math.Abs(moved.Y) > 4) // a drag, not a click
                {
                    Control over = UIManager.MouseOverControl;
                    var target = _groupTargets.FirstOrDefault(t => t.Hit == over);

                    if (target.Hit != null)
                    {
                        _dragSkill = skill;
                        DropSkill(target.View);
                    }
                    else if ((over == null || over.RootParent != this) && World.Player != null && skill < World.Player.Skills.Length
                        && World.Player.Skills[skill].IsClickable)
                    {
                        // Dropped outside the window: a skill button, like the stock window.
                        UIManager.Add(new SkillButtonGump(World, World.Player.Skills[skill], Mouse.Position.X - 44, Mouse.Position.Y - 22));
                    }
                }
            }
        }

        /// <summary>One skill row: a plain container, so its use and lock buttons get their clicks.</summary>
        sealed class RowControl : Control
        {
            public RowControl()
            {
                AcceptMouseInput = true;
                CanMove = false;
                WantUpdateSize = false;
            }
        }

        // ------------------------------------------------------------------ saved with the layout

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);
            writer.WriteAttributeString("iexw", Width.ToString());
            writer.WriteAttributeString("iexh", Height.ToString());
            writer.WriteAttributeString("iexview", _view.ToString());
            writer.WriteAttributeString("iexshow", _show.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            if (int.TryParse(xml.GetAttribute("iexw"), out int w) && int.TryParse(xml.GetAttribute("iexh"), out int h))
            {
                Width = Math.Max(MinWidth, w);
                Height = Math.Max(MinHeight, h);
            }

            int.TryParse(xml.GetAttribute("iexview"), out _view);
            int.TryParse(xml.GetAttribute("iexshow"), out _show);
            _show = Math.Clamp(_show, 0, 2);
            Build();
        }
    }
}
