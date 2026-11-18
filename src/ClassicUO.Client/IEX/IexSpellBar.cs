// Spell bars: a row (or block) of slots you fill by dragging spells out of the spellbook.
// Click a slot to cast. Right-click a slot to give it a key (any key, with Ctrl/Alt/Shift),
// clear the key, or empty it; drag a spell out to move it or take it off. Drag the corner to
// change how many slots show. Right-click the bar's edge for a new bar or to close it.
// Bars are saved with the layout (Profile.ReadGumps); the first login after this opens one.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL3;

namespace ClassicUO.IEX
{
    internal sealed class IexSpellBar : Gump
    {
        const int SlotSize = 44, Gap = 2, Pad = 7, MaxSlots = 40;

        sealed class SlotData
        {
            public int Spell;
            public SDL.SDL_Keycode Key;
            public bool Ctrl, Alt, Shift;

            public bool HasKey => Key != SDL.SDL_Keycode.SDLK_UNKNOWN;
        }

        readonly SlotData[] _slots = Enumerable.Range(0, MaxSlots).Select(_ => new SlotData()).ToArray();
        int _cols = 8, _rows = 1;
        bool _resizing;
        Point _resizeMouse, _resizeSize;

        static SlotData _capturing; // the slot waiting for a key

        public IexSpellBar(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false; // right-click is the slot menu
        }

        public override GumpType GumpType => GumpType.IexSpellBar;

        int Shown => _cols * _rows;

        // ------------------------------------------------------------------ opening

        public static IexSpellBar OpenNew(World world, int x, int y)
        {
            var bar = new IexSpellBar(world) { X = x, Y = y };
            bar.Build();
            UIManager.Add(bar);
            return bar;
        }

        /// <summary>After the saved windows are restored at login: the first time, open a bar.</summary>
        public static void OnLogin(World world)
        {
            Profile p = ProfileManager.CurrentProfile;

            if (p == null || p.IexSpellBarOffered)
            {
                return;
            }

            p.IexSpellBarOffered = true;

            if (UIManager.Gumps.OfType<IexSpellBar>().Any())
            {
                return;
            }

            int w = Pad * 2 + 8 * SlotSize + 7 * Gap;
            Point pos = p.GameWindowPosition, size = p.GameWindowSize;
            OpenNew(world, pos.X + Math.Max(0, (size.X - w) / 2), pos.Y + Math.Max(0, size.Y - SlotSize - Pad * 2 - 30));
        }

        // ------------------------------------------------------------------ layout

        void Build()
        {
            foreach (Control c in Children.ToArray())
            {
                Remove(c);
                c.Dispose();
            }

            WantUpdateSize = false;
            Width = Pad * 2 + _cols * SlotSize + (_cols - 1) * Gap;
            Height = Pad * 2 + _rows * SlotSize + (_rows - 1) * Gap;

            Add(new ResizePic(0x0BB8) { Width = Width, Height = Height, AcceptMouseInput = false }); // the bar itself takes clicks on the edge

            for (int i = 0; i < Shown; i++)
            {
                Add(new SlotControl(this, i)
                {
                    X = Pad + i % _cols * (SlotSize + Gap),
                    Y = Pad + i / _cols * (SlotSize + Gap),
                    Width = SlotSize,
                    Height = SlotSize
                });
            }

            var grip = new IexResizeGrip(Width, Height, 10); // the bar's edge is narrow: clear of the last slot
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
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed || !_resizing)
            {
                return;
            }

            if (!Mouse.LButtonPressed)
            {
                _resizing = false;
            }

            Point d = Mouse.Position - _resizeMouse;
            int cols = Math.Clamp((int)Math.Round((_resizeSize.X + d.X - Pad * 2 + Gap) / (float)(SlotSize + Gap)), 1, MaxSlots);
            int rows = Math.Clamp((int)Math.Round((_resizeSize.Y + d.Y - Pad * 2 + Gap) / (float)(SlotSize + Gap)), 1, MaxSlots);

            while (cols * rows > MaxSlots)
            {
                if (cols >= rows) cols--; else rows--;
            }

            if (cols != _cols || rows != _rows)
            {
                _cols = cols;
                _rows = rows;
                Build();
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, button);

            if (button == MouseButtonType.Right)
            {
                BarMenu();
            }
        }

        void BarMenu()
        {
            var menu = new ContextMenuControl(this);
            menu.Add("New spell bar", () => OpenNew(World, X + 20, Y + Height + 6));
            menu.Add(IexLocked ? "Unlock (Ctrl+Alt+click)" : "Lock in place (Ctrl+Alt+click)", () => IexLocked = !IexLocked);
            menu.Add("Close this bar", Dispose);
            menu.Show();
        }

        // ------------------------------------------------------------------ casting and keys

        static SpellDefinition Spell(int id)
        {
            if (id == IexSpellbook.NightSightIndex)
            {
                return IexSpellbook.NightSight;
            }

            SpellDefinition def = id > 0 ? SpellDefinition.FullIndexGetSpell(id) : null;
            return def == null || def.ID <= 0 || string.IsNullOrEmpty(def.Name) ? null : def;
        }

        static void Cast(int id)
        {
            if (Spell(id) != null)
            {
                GameActions.CastSpell(id);
            }
        }

        static string KeyName(SlotData s)
        {
            if (!s.HasKey)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            if (s.Ctrl) sb.Append("C+");
            if (s.Alt) sb.Append("A+");
            if (s.Shift) sb.Append("S+");

            string name = KeysTranslator.TryGetKey(s.Key);
            sb.Append(name.Length == 1 ? name.ToUpperInvariant() : name);
            return sb.ToString();
        }

        static bool IsModifier(SDL.SDL_Keycode k) =>
            k == SDL.SDL_Keycode.SDLK_LCTRL || k == SDL.SDL_Keycode.SDLK_RCTRL
            || k == SDL.SDL_Keycode.SDLK_LSHIFT || k == SDL.SDL_Keycode.SDLK_RSHIFT
            || k == SDL.SDL_Keycode.SDLK_LALT || k == SDL.SDL_Keycode.SDLK_RALT
            || k == SDL.SDL_Keycode.SDLK_LGUI || k == SDL.SDL_Keycode.SDLK_RGUI;

        /// <summary>A key pressed in the game (where macros are looked up). True when a bar used it.</summary>
        public static bool OnKey(SDL.SDL_Keycode key)
        {
            if (key == SDL.SDL_Keycode.SDLK_UNKNOWN)
            {
                return false;
            }

            if (_capturing != null)
            {
                if (IsModifier(key))
                {
                    return true; // wait for the key itself
                }

                SlotData slot = _capturing;
                _capturing = null;

                if (key == SDL.SDL_Keycode.SDLK_ESCAPE)
                {
                    return true;
                }

                // One slot per key: take it off any other.
                foreach (IexSpellBar bar in UIManager.Gumps.OfType<IexSpellBar>())
                {
                    foreach (SlotData s in bar._slots)
                    {
                        if (s.Key == key && s.Ctrl == Keyboard.Ctrl && s.Alt == Keyboard.Alt && s.Shift == Keyboard.Shift)
                        {
                            s.Key = SDL.SDL_Keycode.SDLK_UNKNOWN;
                        }
                    }
                }

                slot.Key = key;
                slot.Ctrl = Keyboard.Ctrl;
                slot.Alt = Keyboard.Alt;
                slot.Shift = Keyboard.Shift;
                return true;
            }

            // Typing a chat line: a plain letter, number or sign is text, not a spell.
            if (!Keyboard.Ctrl && !Keyboard.Alt && (int)key >= 32 && (int)key < 127
                && !string.IsNullOrEmpty(UIManager.SystemChat?.TextBoxControl?.Text))
            {
                return false;
            }

            foreach (IexSpellBar bar in UIManager.Gumps.OfType<IexSpellBar>())
            {
                if (bar.IsDisposed)
                {
                    continue;
                }

                for (int i = 0; i < bar.Shown; i++)
                {
                    SlotData s = bar._slots[i];

                    if (s.Spell != 0 && s.Key == key && s.Ctrl == Keyboard.Ctrl && s.Alt == Keyboard.Alt && s.Shift == Keyboard.Shift)
                    {
                        Cast(s.Spell);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>A spell icon dropped (UseSpellButtonGump drag end). True when a bar took it.</summary>
        public static bool TryDrop(int spellId)
        {
            Point m = Mouse.Position;

            foreach (IexSpellBar bar in UIManager.Gumps.OfType<IexSpellBar>())
            {
                if (bar.IsDisposed || !bar.IsVisible || !bar.Bounds.Contains(m))
                {
                    continue;
                }

                int lx = m.X - bar.X - Pad, ly = m.Y - bar.Y - Pad;
                int col = Math.Clamp(lx / (SlotSize + Gap), 0, bar._cols - 1);
                int row = Math.Clamp(ly / (SlotSize + Gap), 0, bar._rows - 1);
                bar._slots[row * bar._cols + col].Spell = spellId;
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ saving

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);
            writer.WriteAttributeString("iexcols", _cols.ToString());
            writer.WriteAttributeString("iexrows", _rows.ToString());

            var sb = new StringBuilder();

            for (int i = 0; i < MaxSlots; i++)
            {
                SlotData s = _slots[i];
                int mods = (s.Ctrl ? 1 : 0) | (s.Alt ? 2 : 0) | (s.Shift ? 4 : 0);

                if (i > 0)
                {
                    sb.Append(';');
                }

                if (s.Spell != 0 || s.HasKey)
                {
                    sb.Append(s.Spell).Append(':').Append((uint)s.Key).Append(':').Append(mods);
                }
            }

            writer.WriteAttributeString("iexslots", sb.ToString().TrimEnd(';'));
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            if (int.TryParse(xml.GetAttribute("iexcols"), out int c) && int.TryParse(xml.GetAttribute("iexrows"), out int r))
            {
                _cols = Math.Clamp(c, 1, MaxSlots);
                _rows = Math.Clamp(r, 1, MaxSlots / _cols);
            }

            string[] parts = (xml.GetAttribute("iexslots") ?? string.Empty).Split(';');

            for (int i = 0; i < parts.Length && i < MaxSlots; i++)
            {
                string[] f = parts[i].Split(':');

                if (f.Length == 3 && int.TryParse(f[0], out int spell) && uint.TryParse(f[1], out uint key) && int.TryParse(f[2], out int mods))
                {
                    _slots[i].Spell = spell;
                    _slots[i].Key = (SDL.SDL_Keycode)key;
                    _slots[i].Ctrl = (mods & 1) != 0;
                    _slots[i].Alt = (mods & 2) != 0;
                    _slots[i].Shift = (mods & 4) != 0;
                }
            }

            Build();
        }

        public override void Dispose()
        {
            if (_capturing != null && _slots.Contains(_capturing))
            {
                _capturing = null;
            }

            base.Dispose();
        }

        // ------------------------------------------------------------------ one slot

        sealed class SlotControl : Control
        {
            static readonly Color SlotBack = new Color(0, 0, 0);
            static readonly Color SlotEdge = new Color(95, 82, 55);
            static readonly Color SlotEdgeHot = new Color(220, 185, 100);

            readonly IexSpellBar _bar;
            readonly int _index;
            bool _down;
            RenderedText _keyText;
            string _keyShown;
            int _tipSpell = -1;
            string _tipKey;

            public SlotControl(IexSpellBar bar, int index)
            {
                _bar = bar;
                _index = index;
                AcceptMouseInput = true;
                CanMove = false;
                CanCloseWithRightClick = false;
                WantUpdateSize = false;
            }

            SlotData Data => _bar._slots[_index];

            public override void Update()
            {
                base.Update();

                SlotData d = Data;

                // An empty slot moves the bar, a filled one is picked up.
                CanMove = d.Spell == 0;

                if (_down && d.Spell != 0)
                {
                    if (!Mouse.LButtonPressed)
                    {
                        _down = false;
                    }
                    else if (Math.Max(Math.Abs(Mouse.LDragOffset.X), Math.Abs(Mouse.LDragOffset.Y)) >= 6)
                    {
                        _down = false;
                        PickUp();
                    }
                }

                string key = _capturing == d ? "?" : KeyName(d);

                if (_tipSpell != d.Spell || _tipKey != key)
                {
                    _tipSpell = d.Spell;
                    _tipKey = key;
                    SpellDefinition def = Spell(d.Spell);

                    if (_capturing == d)
                    {
                        SetTooltip("Press a key for this slot (Esc: cancel)");
                    }
                    else if (def != null)
                    {
                        SetTooltip(key.Length > 0 ? $"{def.Name} ({key})" : def.Name);
                    }
                    else
                    {
                        SetTooltip("Drag a spell here from your spellbook");
                    }
                }
            }

            void PickUp()
            {
                SpellDefinition def = Spell(Data.Spell);
                Data.Spell = 0;

                if (def == null)
                {
                    return;
                }

                var icon = new UseSpellButtonGump(_bar.World, def) { X = Mouse.Position.X - 22, Y = Mouse.Position.Y - 22 };
                UIManager.Add(icon);
                UIManager.AttemptDragControl(icon, true);
            }

            protected override void OnMouseDown(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left)
                {
                    _down = true;
                }

                base.OnMouseDown(x, y, button);
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left)
                {
                    bool click = _down && Math.Max(Math.Abs(Mouse.LDragOffset.X), Math.Abs(Mouse.LDragOffset.Y)) < 6;
                    _down = false;

                    if (click && Data.Spell != 0)
                    {
                        if (_bar.World.TargetManager.IsTargeting)
                        {
                            _bar.World.TargetManager.CancelTarget();
                        }

                        Cast(Data.Spell);
                    }
                }
                else if (button == MouseButtonType.Right)
                {
                    Menu();
                }

                base.OnMouseUp(x, y, button);
            }

            void Menu()
            {
                SlotData d = Data;
                var menu = new ContextMenuControl(_bar);

                menu.Add(d.HasKey ? $"Change key ({KeyName(d)})" : "Set key", () =>
                {
                    _capturing = d;
                    UIManager.SystemChat?.SetFocus(); // keys reach the game through the chat line
                });

                if (d.HasKey)
                {
                    menu.Add("Clear key", () => d.Key = SDL.SDL_Keycode.SDLK_UNKNOWN);
                }

                if (d.Spell != 0)
                {
                    menu.Add("Empty slot", () => d.Spell = 0);
                }

                menu.Add("New spell bar", () => OpenNew(_bar.World, _bar.X + 20, _bar.Y + _bar.Height + 6));
                menu.Add(_bar.IexLocked ? "Unlock (Ctrl+Alt+click)" : "Lock in place (Ctrl+Alt+click)", () => _bar.IexLocked = !_bar.IexLocked);
                menu.Add("Close this bar", _bar.Dispose);
                menu.Show();
            }

            public override void Dispose()
            {
                _keyText?.Destroy();
                _keyText = null;
                base.Dispose();
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                SlotData d = Data;
                SpellDefinition def = Spell(d.Spell);
                bool hot = _capturing == d || UIManager.MouseOverControl == this;
                int w = Width, h = Height;

                Texture2D iconTex = null;
                Rectangle iconUV = default;

                if (def != null)
                {
                    int iconId = def.GumpIconSmallID > 0 ? def.GumpIconSmallID : def.GumpIconID;
                    ref readonly var info = ref Client.Game.UO.Gumps.GetGump((ushort)iconId);
                    iconTex = info.Texture;
                    iconUV = info.UV;
                }

                string key = _capturing == d ? "?" : KeyName(d);

                if (key != _keyShown)
                {
                    _keyShown = key;
                    _keyText?.Destroy();
                    _keyText = key.Length == 0 ? null : RenderedText.Create(key, 0x0481, 1, true, FontStyle.BlackBorder);
                }

                RenderedText keyText = _keyText;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Vector3 back = ShaderHueTranslator.GetHueVector(0, false, 0.55f);
                    Vector3 solid = ShaderHueTranslator.GetHueVector(0, false, 1f);

                    batcher.Draw(SolidColorTextureCache.GetTexture(SlotBack), new Rectangle(x, y, w, h), back, layerDepth);

                    if (iconTex != null)
                    {
                        batcher.Draw(iconTex, new Rectangle(x + 1, y + 1, w - 2, h - 2), iconUV, solid, layerDepth);
                    }

                    batcher.DrawRectangle(SolidColorTextureCache.GetTexture(hot ? SlotEdgeHot : SlotEdge), x, y, w - 1, h - 1, solid, layerDepth);

                    if (keyText != null && !keyText.IsDestroyed)
                    {
                        float scale = Math.Min(1f, (w - 4) / (float)Math.Max(1, keyText.Width));
                        keyText.Draw(batcher, x + w - 2 - (int)(keyText.Width * scale), y + h - 1 - (int)(keyText.Height * scale), layerDepth, 1f, 0, scale);
                    }

                    return true;
                });

                return true;
            }
        }
    }
}
