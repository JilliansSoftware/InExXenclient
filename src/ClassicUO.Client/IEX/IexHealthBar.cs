// Compact health bars in the old bars' style: a black stone frame with gold trim and studs
// (others' stone coloured by notoriety, yours red in war mode, pulsing red when low on hits), the
// name in gold title lettering on a leather ribbon, gold H/M/S letters, and wells carved into the
// stone holding painted liquid that slowly drifts (IexHealthBarArt.cs). The numbers show while
// the mouse is over a bar, or always (Options, Profile.IexBarNumbers).
// Hits for everyone; mana and stamina for you and your party. Drag the corner to resize it:
// the frame is stretched from its art so it stays crisp, and the new size becomes the default
// for the next bar of that kind (yours, or anyone else's).
// It is the style of "Use custom health bars" when Profile.IexCompactBars is on (new characters).

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    // In the stock gumps' namespace, so their files need no extra using.
    internal static class IexHealthBarPlace
    {
        // Place a new bar (stock code sets X and Y in an initializer).
        public static BaseHealthBarGump At(this BaseHealthBarGump bar, int x, int y)
        {
            bar.X = x;
            bar.Y = y;
            return bar;
        }
    }
}

namespace ClassicUO.IEX
{
    internal sealed partial class IexHealthBar : BaseHealthBarGump
    {
        // The frame art's band, outside its inner gold line (make_compactbar.py).
        const int FrameL = 9, FrameR = 9, FrameT = 7, FrameB = 8, MinWidth = 80;
        const ushort EditHue = 0x0481;

        static bool Compact => ProfileManager.CurrentProfile?.IexCompactBars ?? false;

        // ------------------------------------------------------------------ choosing the bar

        // A "custom" health bar: the compact one when that style is chosen.
        public static BaseHealthBarGump Custom(World world, Entity entity) =>
            Compact ? new IexHealthBar(world, entity) : new HealthBarGumpCustom(world, entity);

        public static BaseHealthBarGump Custom(World world, uint serial) =>
            Compact ? new IexHealthBar(world, serial) : new HealthBarGumpCustom(world, serial);

        public static BaseHealthBarGump Custom(World world) =>
            Compact ? new IexHealthBar(world) : new HealthBarGumpCustom(world);

        // Whichever bar the options ask for.
        public static BaseHealthBarGump Create(World world, uint serial) =>
            ProfileManager.CurrentProfile.CustomBarsToggled ? Custom(world, serial) : new HealthBarGump(world, serial);

        // ------------------------------------------------------------------

        bool _full; // hits, mana and stamina (else hits only)
        int _w, _h; // the size this bar keeps (0: the default for its kind)
        bool _resizing;
        Point _resizeMouse, _resizeSize;

        public IexHealthBar(World world, Entity entity) : base(world, entity) { }
        public IexHealthBar(World world, uint serial) : base(world, serial) { }
        public IexHealthBar(World world) : base(world, 0, 0) { }

        bool WantsFull => LocalSerial == World.Player || World.Party.Contains(LocalSerial);

        // Earlier builds' default sizes (the first test's, the plain style's, then the framed builds'): treated as
        // "not resized" and given this style's defaults.
        static bool FirstSize(Point s) =>
            s.X <= 0 || s.Y <= 0 || s == new Point(160, 56) || s == new Point(160, 32) || s == new Point(180, 68) || s == new Point(180, 36)
            || s == new Point(180, 80) || s == new Point(180, 48)
            || s == new Point(250, 110) || s == new Point(250, 66) || s == new Point(150, 68);

        // Room for the frame, a name row and wells of at least 6 px (and the frame's studs).
        static int MinHeight(bool full) => Math.Max(42, FrameT + FrameB + 9 + 1 + (full ? 3 * 6 + 2 * 2 : 6) + 1);

        static Point DefaultSize(bool self, bool full)
        {
            Profile p = ProfileManager.CurrentProfile;
            Point s = self ? p.IexBarSizeSelf : p.IexBarSizeOther;

            if (FirstSize(s))
            {
                s = self ? new Point(150, 90) : new Point(150, 53);
            }
            return new Point(Math.Max(MinWidth, s.X), Math.Max(MinHeight(full), s.Y));
        }

        protected override void UpdateContents()
        {
            BuildGump();
        }

        protected override void BuildGump()
        {
            if (_textBox != null)
            {
                _textBox.MouseUp -= TextBoxOnMouseUp;
            }

            foreach (Control c in Children.ToArray())
            {
                Remove(c);
                c.Dispose();
            }

            _textBox = null;
            WantUpdateSize = false;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true; // gumps don't take clicks by default: this drags, targets, closes on right-click
            CanMove = true;

            _full = WantsFull;

            if (_w <= 0 || _h <= 0)
            {
                Point d = DefaultSize(LocalSerial == World.Player, _full);
                _w = d.X;
                _h = d.Y;
            }

            Width = _w = Math.Max(MinWidth, _w);
            Height = _h = Math.Max(MinHeight(_full), _h);

            Entity entity = World.Get(LocalSerial);
            _canChangeName = entity is Mobile m && m.IsRenamable;

            Add(new BarsView(this) { Width = Width, Height = Height });

            // The name is drawn in gold lettering (BarsView); this box only shows while renaming a pet.
            Rectangle nameRow = NameRow(Width, Height, _full);

            Add
            (
                _textBox = new StbTextBox(1, 32, nameRow.Width, true, FontStyle.Cropped | FontStyle.BlackBorder, EditHue, TEXT_ALIGN_TYPE.TS_CENTER)
                {
                    X = nameRow.X,
                    Y = nameRow.Y,
                    Width = nameRow.Width,
                    Height = nameRow.Height,
                    IsEditable = false,
                    IsVisible = false,
                    AcceptMouseInput = _canChangeName,
                    AcceptKeyboardInput = _canChangeName,
                    WantUpdateSize = false,
                    CanMove = true
                }
            );

            _textBox.MouseUp += TextBoxOnMouseUp;
            _textBox.SetText(_name ?? string.Empty);

            Add(new Grip(this) { X = Width - 9, Y = Height - 9, Width = 9, Height = 9 });
        }

        // A click on a pet's name starts renaming it (the box is hidden otherwise, so the click
        // reaches the bar).
        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, button);

            Point drag = Mouse.LDragOffset;

            if (button == MouseButtonType.Left && _canChangeName && _textBox != null && !World.TargetManager.IsTargeting
                && Math.Max(Math.Abs(drag.X), Math.Abs(drag.Y)) < 1 && NameRow(Width, Height, _full).Contains(x, y))
            {
                _textBox.IsVisible = true;
                _textBox.IsEditable = true;
                _textBox.SetKeyboardFocus();
            }
        }

        public override bool Contains(int x, int y) => true;

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            bool inParty = World.Party.Contains(LocalSerial);
            Entity entity = World.Get(LocalSerial);

            if (entity is Item it && it.Layer == 0 && it.Container == World.Player)
            {
                entity = null;
            }

            Profile profile = ProfileManager.CurrentProfile;

            if (entity == null || entity.IsDestroyed)
            {
                // Gone from view: close it (as the options say) or grey it out.
                if (LocalSerial != World.Player && (profile.CloseHealthBarType == 1 || profile.CloseHealthBarType == 2 && World.CorpseManager.Exists(0, LocalSerial | 0x8000_0000)))
                {
                    if (!inParty && CheckIfAnchoredElseDispose())
                    {
                        return;
                    }
                }

                _isDead = false;

                if (!_outOfRange)
                {
                    _outOfRange = true;

                    if (World.TargetManager.LastAttack != LocalSerial)
                    {
                        GameActions.SendCloseStatus(World, LocalSerial);
                    }

                    if (_textBox != null)
                    {
                        _textBox.IsEditable = false;
                    }
                }
            }
            else
            {
                Mobile mobile = entity as Mobile;

                if (!_isDead && entity != World.Player && mobile != null && mobile.IsDead && profile.CloseHealthBarType == 2)
                {
                    if (!inParty && CheckIfAnchoredElseDispose())
                    {
                        return;
                    }
                }

                _isDead = mobile != null && mobile.IsDead;

                bool renamable = mobile != null && mobile.IsRenamable;

                if (_canChangeName != renamable)
                {
                    _canChangeName = renamable;

                    if (_textBox != null)
                    {
                        _textBox.AcceptMouseInput = _textBox.AcceptKeyboardInput = renamable;

                        if (!renamable)
                        {
                            _textBox.IsEditable = false;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(entity.Name) && _name != entity.Name)
                {
                    _name = entity.Name;

                    if (_textBox != null && !_textBox.IsEditable)
                    {
                        _textBox.SetText(_name);
                    }
                }

                if (_outOfRange)
                {
                    if (entity.HitsMax == 0)
                    {
                        GameActions.RequestMobileStatus(World, entity);
                    }

                    _outOfRange = false;
                }

                if (UIManager.MouseOverControl != null && UIManager.MouseOverControl.RootParent == this)
                {
                    SelectedObject.HealthbarObject = entity;
                    SelectedObject.Object = entity;
                }
            }

            if (_textBox != null && _textBox.IsVisible != _textBox.IsEditable)
            {
                _textBox.IsVisible = _textBox.IsEditable; // renaming finished or cancelled
            }

            // Joined or left the party: mana and stamina come or go, at that kind's size.
            if (WantsFull != _full && !_resizing)
            {
                _w = _h = 0;
                BuildGump();
                return;
            }

            if (_resizing)
            {
                Point d = Mouse.Position - _resizeMouse;
                int w = Math.Max(MinWidth, _resizeSize.X + d.X), h = Math.Max(MinHeight(_full), _resizeSize.Y + d.Y);

                if (!Mouse.LButtonPressed)
                {
                    _resizing = false;

                    if (LocalSerial == World.Player)
                    {
                        profile.IexBarSizeSelf = new Point(w, h);
                    }
                    else
                    {
                        profile.IexBarSizeOther = new Point(w, h);
                    }
                }

                if (w != Width || h != Height)
                {
                    _w = w;
                    _h = h;
                    BuildGump();
                }
            }
        }

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);
            writer.WriteAttributeString("iexw", Width.ToString());
            writer.WriteAttributeString("iexh", Height.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            // Read the size first: the base builds the bar.
            if (int.TryParse(xml.GetAttribute("iexw"), out int w) && int.TryParse(xml.GetAttribute("iexh"), out int h) && !FirstSize(new Point(w, h)))
            {
                _w = w;
                _h = h;
            }

            base.Restore(xml);
        }

        // ------------------------------------------------------------------ layout

        // Inside the frame's inner gold line (the frame art's middle, IexHealthBar.FrameArt).
        static Rectangle Middle(int w, int h) => new Rectangle(FrameL, FrameT, w - FrameL - FrameR, h - FrameT - FrameB);

        // The name row along the top of the middle: the title font's full height (about 21 px) when
        // the bar has room, so the lettering is drawn at its own size; shrunk only on short bars.
        static Rectangle NameRow(int w, int h, bool full)
        {
            Rectangle m = Middle(w, h);
            int nh = Math.Clamp((int)(m.Height * (full ? 0.3f : 0.5f)), 12, 21);
            return new Rectangle(m.X + 3, m.Y + 1, m.Width - 6, nh);
        }

        // ------------------------------------------------------------------ the bars

        sealed class BarsView : Control
        {
            // The liquids' colours (painted by PaintArt, IexHealthBarArt.cs).
            static readonly Color Hits = new Color(172, 30, 26);
            static readonly Color Poison = new Color(46, 150, 44);
            static readonly Color Mortal = new Color(200, 168, 34); // yellow hits: can't be hurt
            static readonly Color Mana = new Color(34, 84, 196);
            static readonly Color Stam = new Color(192, 112, 22);
            static readonly Color Grey = new Color(92, 92, 92);

            static readonly Color TargetRed = new Color(210, 36, 30);

            static readonly string[] Letters = { "H", "M", "S" };

            readonly IexHealthBar _owner;
            readonly BarText[] _text = { new BarText(), new BarText(), new BarText() };

            Texture2D _frame, _name, _nameSmall, _socketBack, _socketFront, _ribbon;
            string _frameKey, _nameText;
            Point _socketSize, _ribbonSize;

            public BarsView(IexHealthBar owner)
            {
                _owner = owner;
                AcceptMouseInput = false;
                WantUpdateSize = false;
            }

            public override void Dispose()
            {
                foreach (BarText t in _text)
                {
                    t.Destroy();
                }

                _frame?.Dispose();
                _name?.Dispose();
                _nameSmall?.Dispose();
                _socketBack?.Dispose();
                _socketFront?.Dispose();
                _ribbon?.Dispose();
                _frame = _name = _nameSmall = _socketBack = _socketFront = _ribbon = null;
                base.Dispose();
            }

            // Which frame: yours (red in war mode), others' by notoriety, grey when out of reach.
            static string FrameName(bool self, bool grey, Mobile mobile, World world)
            {
                if (self)
                {
                    return world.Player.InWarMode ? "compactbar-war" : "compactbar";
                }

                if (grey || mobile == null)
                {
                    return "compactbar-other";
                }

                switch (mobile.NotorietyFlag)
                {
                    case NotorietyFlag.Innocent: return "compactbar-innocent";
                    case NotorietyFlag.Ally: return "compactbar-ally";
                    case NotorietyFlag.Enemy: return "compactbar-enemy";
                    case NotorietyFlag.Murderer: return "compactbar-murderer";
                    case NotorietyFlag.Invulnerable: return "compactbar-invulnerable";
                    default: return "compactbar-other";
                }
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                IexHealthBar o = _owner;
                World world = o.World;
                Entity entity = world.Get(o.LocalSerial);
                Mobile mobile = entity as Mobile;
                bool grey = entity == null || o._outOfRange || o._isDead;
                bool self = o.LocalSerial == world.Player;
                int count = o._full ? 3 : 1;
                int width = Width, height = Height;

                // the frame, rebuilt when its size or colour changes
                string frameName = FrameName(self, grey, mobile, world);
                string key = $"{frameName}:{width}x{height}";

                if (_frameKey != key || _frame == null || _frame.IsDisposed)
                {
                    _frame?.Dispose();
                    _frame = FrameArt.Build(frameName, width, height);
                    _frameKey = key;
                }

                // the name in gold title lettering (hidden while it is being edited)
                string name = o._name ?? string.Empty;

                if (_nameText != name)
                {
                    _name?.Dispose();
                    _nameSmall?.Dispose();
                    _name = Lettering.Build(4, name);
                    _nameSmall = null; // the smaller cut (font 6), made when a name doesn't fit
                    _nameText = name;
                }

                bool editing = o._textBox != null && o._textBox.IsVisible;

                Rectangle middle = Middle(width, height);
                Rectangle nameRow = NameRow(width, height, o._full);

                int lettersW = 11;
                int wellX = middle.X + lettersW + 1, wellW = middle.Right - 2 - wellX;
                int top = nameRow.Bottom + 1, gap = 2;
                int wellH = Math.Max(4, (middle.Bottom - 2 - top - gap * (count - 1)) / count);

                bool target = !self && !grey && world.TargetManager.LastTargetInfo.Serial == o.LocalSerial;

                Color hitsColor = grey ? Grey
                    : mobile != null && mobile.IsPoisoned ? Poison
                    : mobile != null && mobile.IsYellowHits ? Mortal
                    : Hits;

                // Your own numbers and your party's are real; anyone else's hits are a share of 100.
                bool numbers = self || mobile != null && o._full;

                int hits = entity?.Hits ?? 0, hitsMax = entity?.HitsMax ?? 0;
                _text[0].Set(grey ? string.Empty : numbers && hitsMax > 0 ? $"{hits}/{hitsMax}" : hitsMax > 0 ? $"{CalculatePercents(hitsMax, hits, 100)}%" : string.Empty);

                if (count == 3)
                {
                    _text[1].Set(grey || mobile == null || mobile.ManaMax == 0 ? string.Empty : $"{mobile.Mana}/{mobile.ManaMax}");
                    _text[2].Set(grey || mobile == null || mobile.StaminaMax == 0 ? string.Empty : $"{mobile.Stamina}/{mobile.StaminaMax}");
                }

                // the carved sockets (one size for all of this bar's wells)
                if (_socketSize != new Point(wellW, wellH) || _socketBack == null || _socketBack.IsDisposed)
                {
                    _socketBack?.Dispose();
                    _socketFront?.Dispose();
                    _socketBack = SocketArt.Back(wellW, wellH);
                    _socketFront = SocketArt.Front(wellW, wellH);
                    _socketSize = new Point(wellW, wellH);
                }

                // the name's size, and the ribbon behind it: the title font 1:1 if it fits, else its
                // smaller cut 1:1, and only shrinking (which blurs) when even that is too big
                Texture2D nameTex0 = _name;
                int roomW = nameRow.Width - 12, roomH = nameRow.Height;

                if (nameTex0 != null && !nameTex0.IsDisposed && (nameTex0.Width > roomW || nameTex0.Height > roomH))
                {
                    if (_nameSmall == null || _nameSmall.IsDisposed)
                    {
                        _nameSmall = Lettering.Build(6, name);
                    }

                    nameTex0 = _nameSmall ?? nameTex0;
                }

                int nameW = 0, nameH = 0;

                if (nameTex0 != null && !nameTex0.IsDisposed)
                {
                    float nameScale = Math.Min(1f, Math.Min(roomH / (float)nameTex0.Height, roomW / (float)nameTex0.Width));
                    nameW = (int)(nameTex0.Width * nameScale);
                    nameH = (int)(nameTex0.Height * nameScale);
                }

                int ribbonH = Math.Max(8, nameRow.Height - 3);
                int ribbonW = Math.Min(nameRow.Width + 4, nameW + 2 * RibbonArt.Tail(ribbonH) + 10);

                if (_ribbonSize != new Point(ribbonW, ribbonH) || _ribbon == null || _ribbon.IsDisposed)
                {
                    _ribbon?.Dispose();
                    _ribbon = RibbonArt.Build(ribbonW, ribbonH);
                    _ribbonSize = new Point(ribbonW, ribbonH);
                }

                // low on hits: the frame pulses red
                bool low = !grey && hitsMax > 0 && hits * 4 < hitsMax;

                // the numbers: always, if the options say so; otherwise while the mouse is over the bar
                bool showNumbers = (ProfileManager.CurrentProfile?.IexBarNumbers ?? false) || o._resizing
                    || UIManager.MouseOverControl != null && UIManager.MouseOverControl.RootParent == o;

                Texture2D frame = _frame, nameTex = nameTex0, socketBack = _socketBack, socketFront = _socketFront, ribbon = _ribbon;
                float t = Time.Ticks / 1000f;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Vector3 plain = ShaderHueTranslator.GetHueVector(0, false, 1f);

                    if (frame != null && !frame.IsDisposed)
                    {
                        batcher.Draw(frame, new Rectangle(x, y, width, height), plain, layerDepth);

                        if (low)
                        {
                            // the band (not the middle) washed red, slow heartbeat
                            float a = 0.2f + 0.3f * (0.5f + 0.5f * MathF.Sin(t * 4f));
                            var red = new Vector3(0x25, ShaderHueTranslator.SHADER_TEXT_HUE, a);
                            batcher.Draw(frame, new Rectangle(x, y, width, FrameT), new Rectangle(0, 0, width, FrameT), red, layerDepth);
                            batcher.Draw(frame, new Rectangle(x, y + height - FrameB, width, FrameB), new Rectangle(0, height - FrameB, width, FrameB), red, layerDepth);
                            batcher.Draw(frame, new Rectangle(x, y + FrameT, FrameL, height - FrameT - FrameB), new Rectangle(0, FrameT, FrameL, height - FrameT - FrameB), red, layerDepth);
                            batcher.Draw(frame, new Rectangle(x + width - FrameR, y + FrameT, FrameR, height - FrameT - FrameB), new Rectangle(width - FrameR, FrameT, FrameR, height - FrameT - FrameB), red, layerDepth);
                        }
                    }

                    if (target)
                    {
                        Outline(batcher, x + middle.X, y + middle.Y, middle.Width, middle.Height, TargetRed, layerDepth);
                    }

                    if (nameTex != null && !nameTex.IsDisposed && !editing)
                    {
                        var hv = ShaderHueTranslator.GetHueVector(0, false, grey ? 0.55f : 1f);
                        int cx = x + nameRow.X + nameRow.Width / 2, cy = y + nameRow.Y + nameRow.Height / 2;

                        if (ribbon != null && !ribbon.IsDisposed)
                        {
                            batcher.Draw(ribbon, new Rectangle(cx - ribbon.Width / 2, cy - ribbon.Height / 2 + 1, ribbon.Width, ribbon.Height), hv, layerDepth);
                        }

                        batcher.Draw(nameTex, new Rectangle(cx - nameW / 2, cy - nameH / 2 + 1, nameW, nameH), hv, layerDepth);
                    }

                    for (int i = 0; i < count; i++)
                    {
                        int by = y + top + i * (wellH + gap);
                        int now, max;
                        Color c;

                        if (i == 0)
                        {
                            now = hits;
                            max = hitsMax;
                            c = hitsColor;
                        }
                        else if (i == 1)
                        {
                            now = mobile?.Mana ?? 0;
                            max = mobile?.ManaMax ?? 0;
                            c = grey ? Grey : Mana;
                        }
                        else
                        {
                            now = mobile?.Stamina ?? 0;
                            max = mobile?.StaminaMax ?? 0;
                            c = grey ? Grey : Stam;
                        }

                        // the letter, gold, in the strip at the left
                        Texture2D letter = Lettering.Letter(Letters[i]);

                        if (letter != null && wellH >= 7)
                        {
                            float ls = letter.Height <= wellH + 2 ? 1f : (wellH + 2) / (float)letter.Height; // 1:1 when it fits
                            int lw = (int)(letter.Width * ls), lh = (int)(letter.Height * ls);
                            batcher.Draw(letter, new Rectangle(x + middle.X + (lettersW - lw) / 2 + 1, by + (wellH - lh) / 2, lw, lh), ShaderHueTranslator.GetHueVector(0, false, grey ? 0.55f : 1f), layerDepth);
                        }

                        // the well carved into the stone, the painted liquid in it, the lip's shadow over it
                        int wx = x + wellX;
                        int innerW = wellW - 2, innerH = wellH - 2;
                        int fill = grey ? innerW : CalculatePercents(max, now, innerW);

                        if (socketBack != null && !socketBack.IsDisposed)
                        {
                            batcher.Draw(socketBack, new Rectangle(wx, by, wellW, wellH), plain, layerDepth);
                        }

                        if (fill > 0 && innerH > 0)
                        {
                            Paint(batcher, PaintArt.For(c), c, wx, by, wellW, wellH, Math.Min(innerW, fill), !grey, t, i, layerDepth);
                        }

                        if (socketFront != null && !socketFront.IsDisposed)
                        {
                            batcher.Draw(socketFront, new Rectangle(wx, by, wellW, wellH), plain, layerDepth);
                        }

                        if (showNumbers)
                        {
                            _text[i].Draw(batcher, wx + 1, by + 1, innerW, innerH, layerDepth);
                        }
                    }

                    return true;
                });

                return true;
            }

            // The painted liquid in a well, a row at a time so it follows the socket's rounded ends.
            // The paint drifts slowly sideways, and where the bar isn't full its edge is ragged and
            // stirs, with a lighter meniscus.
            static void Paint(UltimaBatcher2D batcher, Texture2D paint, Color c, int wx, int by, int wellW, int wellH, int fillW, bool alive, float t, int seed, float depth)
            {
                int innerW = wellW - 2, innerH = wellH - 2;
                bool ragged = alive && fillW < innerW;
                int drift = alive ? (int)(t * 3f) + seed * 83 : 0;
                Vector3 plain = ShaderHueTranslator.GetHueVector(0, false, 1f);
                var lip = new Color(Math.Min(255, c.R * 3 / 2 + 30), Math.Min(255, c.G * 3 / 2 + 30), Math.Min(255, c.B * 3 / 2 + 30));

                for (int r = 1; r <= innerH; r++)
                {
                    int inset = SocketArt.Inset(r, wellH);
                    int start = inset, rowMax = wellW - inset;
                    int end = 1 + fillW;

                    if (ragged)
                    {
                        float j = MathF.Sin(r * 0.9f + t * 1.1f + seed) * 0.8f + MathF.Sin(r * 0.45f - t * 0.7f + seed * 2) * 0.6f;
                        end += Math.Clamp((int)MathF.Round(j), -1, 1);
                    }

                    end = Math.Min(end, rowMax);

                    if (end <= start)
                    {
                        continue;
                    }

                    int sy = Math.Clamp((r - 1) * PaintArt.H / Math.Max(1, innerH), 0, PaintArt.H - 1);
                    int dx = start;

                    while (dx < end) // the paint tiles sideways: at most one wrap per row
                    {
                        int sx = ((drift + dx) % PaintArt.W + PaintArt.W) % PaintArt.W;
                        int len = Math.Min(end - dx, PaintArt.W - sx);
                        batcher.Draw(paint, new Rectangle(wx + dx, by + r, len, 1), new Rectangle(sx, sy, len, 1), plain, depth);
                        dx += len;
                    }

                    if (ragged && end < rowMax)
                    {
                        Fill(batcher, wx + end - 1, by + r, 1, 1, lip, 0.3f, depth);
                    }
                }
            }

            static void Fill(UltimaBatcher2D batcher, int x, int y, int w, int h, Color c, float alpha, float depth)
            {
                batcher.Draw(SolidColorTextureCache.GetTexture(c), new Rectangle(x, y, w, h), ShaderHueTranslator.GetHueVector(0, false, alpha), depth);
            }

            static void Outline(UltimaBatcher2D batcher, int x, int y, int w, int h, Color c, float depth)
            {
                Fill(batcher, x, y, w, 1, c, 0.9f, depth);
                Fill(batcher, x, y + h - 1, w, 1, c, 0.9f, depth);
                Fill(batcher, x, y, 1, h, c, 0.9f, depth);
                Fill(batcher, x + w - 1, y, 1, h, c, 0.9f, depth);
            }
        }

        // ------------------------------------------------------------------ art

        // The stone and gold frames (branding/art/make_compactbar.py): 125x61, stretched to any
        // size. Corners, the corner studs and the four centre studs keep their shape; only the
        // runs between them stretch.
        static class FrameArt
        {
            const int KitW = 125, KitH = 61, Corner = 14, StudX = 62, StudY = 30, StudR = 4;

            static readonly Dictionary<string, uint[]> _kits = new Dictionary<string, uint[]>();

            static uint[] Kit(string name)
            {
                if (_kits.TryGetValue(name, out uint[] px))
                {
                    return px;
                }

                px = null;
                using var stream = typeof(IexHealthBar).Assembly.GetManifestResourceStream($"IEX.Art.{name}.rgba.gz");

                if (stream != null)
                {
                    using var gz = new GZipStream(stream, CompressionMode.Decompress);
                    var bytes = new byte[KitW * KitH * 4];
                    int read = 0, n;

                    while (read < bytes.Length && (n = gz.Read(bytes, read, bytes.Length - read)) > 0)
                    {
                        read += n;
                    }

                    if (read == bytes.Length)
                    {
                        px = new uint[KitW * KitH];
                        Buffer.BlockCopy(bytes, 0, px, 0, bytes.Length); // R | G<<8 | B<<16 | A<<24, as Color
                    }
                }

                _kits[name] = px;
                return px;
            }

            // Where target pixel t (of n) reads from in the kit (of size, with its stud at stud).
            static int Map(int t, int n, int size, int stud)
            {
                if (t < Corner)
                {
                    return t;
                }

                if (t >= n - Corner)
                {
                    return t - (n - size);
                }

                int c = n / 2;

                if (Math.Abs(t - c) <= StudR)
                {
                    return stud + (t - c);
                }

                if (t < c)
                {
                    return Corner + (t - Corner) * (stud - StudR - Corner) / Math.Max(1, c - StudR - Corner);
                }

                return stud + StudR + 1 + (t - c - StudR - 1) * (size - Corner - stud - StudR - 1) / Math.Max(1, n - Corner - c - StudR - 1);
            }

            public static Texture2D Build(string name, int w, int h)
            {
                uint[] kit = Kit(name);

                if (kit == null || w <= 0 || h <= 0)
                {
                    return null;
                }

                var sx = new int[w];

                for (int x = 0; x < w; x++)
                {
                    sx[x] = Math.Clamp(Map(x, w, KitW, StudX), 0, KitW - 1);
                }

                var px = new uint[w * h];

                for (int y = 0; y < h; y++)
                {
                    int row = Math.Clamp(Map(y, h, KitH, StudY), 0, KitH - 1) * KitW;

                    for (int x = 0; x < w; x++)
                    {
                        px[y * w + x] = kit[row + sx[x]];
                    }
                }

                var tex = new Texture2D(Client.Game.GraphicsDevice, w, h);
                tex.SetData(px);
                return tex;
            }
        }

        // Text in the shard's gold title lettering (as window titles), as a texture.
        static class Lettering
        {
            // the sample sheet's gold (bright at the top, deep at the foot)
            static readonly Color TitleHi = new Color(248, 216, 112), TitleMid = new Color(224, 168, 48), TitleLo = new Color(136, 88, 16);

            static readonly Dictionary<string, Texture2D> _letters = new Dictionary<string, Texture2D>();

            public static Texture2D Build(byte font, string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                var fi = Client.Game.UO.FileManager.Fonts.GenerateASCII(font, text, 0, 0, TEXT_ALIGN_TYPE.TS_LEFT, 0, false, 0);

                if (fi.Data == null || fi.Width <= 0 || fi.Height <= 0)
                {
                    return null;
                }

                // Cropped to the ink: the font's cell has empty rows above and below, and a texture
                // taller than its row would be shrunk (and blurred) to fit. Drawn 1:1 it stays crisp.
                int x0 = fi.Width, y0 = fi.Height, x1 = -1, y1 = -1;

                for (int y = 0; y < fi.Height; y++)
                {
                    for (int x = 0; x < fi.Width; x++)
                    {
                        if ((fi.Data[y * fi.Width + x] >> 24) != 0)
                        {
                            x0 = Math.Min(x0, x);
                            x1 = Math.Max(x1, x);
                            y0 = Math.Min(y0, y);
                            y1 = Math.Max(y1, y);
                        }
                    }
                }

                if (x1 < x0)
                {
                    return null;
                }

                // Every pixel of a letter is solid ink, coloured by a smooth gold fall from top to
                // foot (not by the font's own pixel shading, which reads as speckle), with a clean
                // dark outline a pixel wide all round.
                int iw = x1 - x0 + 1, ih = y1 - y0 + 1;
                int w = iw + 2, h = ih + 2;
                var ink = new bool[w * h];

                for (int y = 0; y < ih; y++)
                {
                    for (int x = 0; x < iw; x++)
                    {
                        ink[(y + 1) * w + x + 1] = (fi.Data[(y + y0) * fi.Width + x + x0] >> 24) != 0;
                    }
                }

                var px = new uint[w * h];
                uint edge = new Color(9, 5, 4, 230).PackedValue; // premultiplied

                for (int y = 0; y < h; y++)
                {
                    float t = y / (float)Math.Max(1, h - 1);
                    Color g = t < 0.5f ? Color.Lerp(TitleHi, TitleMid, t * 2) : Color.Lerp(TitleMid, TitleLo, (t - 0.5f) * 2);

                    for (int x = 0; x < w; x++)
                    {
                        if (ink[y * w + x])
                        {
                            px[y * w + x] = g.PackedValue;
                            continue;
                        }

                        for (int dy = -1; dy <= 1 && px[y * w + x] == 0; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;

                                if (nx >= 0 && ny >= 0 && nx < w && ny < h && ink[ny * w + nx])
                                {
                                    px[y * w + x] = edge;
                                    break;
                                }
                            }
                        }
                    }
                }

                var tex = new Texture2D(Client.Game.GraphicsDevice, w, h);
                tex.SetData(px);
                return tex;
            }

            /// <summary>H, M or S (ASCII font 1, as on the old bars), kept for every bar.</summary>
            public static Texture2D Letter(string letter)
            {
                if (!_letters.TryGetValue(letter, out Texture2D tex) || tex == null || tex.IsDisposed)
                {
                    tex = Build(1, letter);
                    _letters[letter] = tex;
                }

                return tex;
            }
        }

        // The numbers in a bar, shrunk to fit; hidden when the bar is too thin to read.
        // The numbers inside a well, in the old serifed lettering (ASCII font 6, a small cut of the
        // title font): cream with a dark outline, so they read on red, blue and amber alike.
        sealed class BarText
        {
            static readonly Color Ink = new Color(244, 234, 208);

            Texture2D _tex;
            string _value = string.Empty;

            public void Set(string value)
            {
                if (value == _value)
                {
                    return;
                }

                _value = value;
                _tex?.Dispose();
                _tex = value.Length == 0 ? null : Build(value);
            }

            static Texture2D Build(string text)
            {
                var fi = Client.Game.UO.FileManager.Fonts.GenerateASCII(6, text, 0, 0, TEXT_ALIGN_TYPE.TS_LEFT, 0, false, 0);

                if (fi.Data == null || fi.Width <= 0 || fi.Height <= 0)
                {
                    return null;
                }

                // only the rows with ink (the font's cell has empty space above and below)
                int top = fi.Height, bottom = -1;

                for (int y = 0; y < fi.Height; y++)
                {
                    for (int x = 0; x < fi.Width; x++)
                    {
                        if ((fi.Data[y * fi.Width + x] >> 24) != 0)
                        {
                            top = Math.Min(top, y);
                            bottom = y;
                            break;
                        }
                    }
                }

                if (bottom < top)
                {
                    return null;
                }

                int w = fi.Width + 2, h = bottom - top + 1 + 2; // a pixel of outline all round
                var ink = new bool[w * h];

                for (int y = top; y <= bottom; y++)
                {
                    for (int x = 0; x < fi.Width; x++)
                    {
                        ink[(y - top + 1) * w + x + 1] = (fi.Data[y * fi.Width + x] >> 24) != 0;
                    }
                }

                var px = new uint[w * h];
                uint inkColor = Ink.PackedValue, edge = new Color(0, 0, 0, 220).PackedValue; // premultiplied black

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (ink[y * w + x])
                        {
                            px[y * w + x] = inkColor;
                            continue;
                        }

                        for (int dy = -1; dy <= 1 && px[y * w + x] == 0; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;

                                if (nx >= 0 && ny >= 0 && nx < w && ny < h && ink[ny * w + nx])
                                {
                                    px[y * w + x] = edge;
                                    break;
                                }
                            }
                        }
                    }
                }

                var tex = new Texture2D(Client.Game.GraphicsDevice, w, h);
                tex.SetData(px);
                return tex;
            }

            public void Draw(UltimaBatcher2D batcher, int x, int y, int w, int h, float depth)
            {
                if (_tex == null || _tex.IsDisposed || h < 6)
                {
                    return;
                }

                // its own size when the well is tall enough; shrunk to fit a thin one
                float scale = Math.Min(1f, (h + 2) / (float)_tex.Height);
                int tw = (int)(_tex.Width * scale), th = (int)(_tex.Height * scale);

                if (tw > w)
                {
                    return;
                }

                batcher.Draw(_tex, new Rectangle(x + (w - tw) / 2, y + (h - th + 1) / 2, tw, th), ShaderHueTranslator.GetHueVector(0, false, 1f), depth);
            }

            public void Destroy()
            {
                _tex?.Dispose();
                _tex = null;
                _value = string.Empty;
            }
        }

        // ------------------------------------------------------------------ resize corner

        // A small corner mark, shown while the mouse is over the bar; drag it to resize.
        sealed class Grip : Control
        {
            static readonly Color Mark = new Color(170, 150, 100);
            readonly IexHealthBar _owner;

            public Grip(IexHealthBar owner)
            {
                _owner = owner;
                AcceptMouseInput = true;
                CanMove = false;
                WantUpdateSize = false;
                CanCloseWithRightClick = true;
            }

            protected override void OnMouseDown(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && !_owner.IexLocked) // locked: no resizing (IexWindowLock)
                {
                    _owner._resizing = true;
                    _owner._resizeMouse = Mouse.Position;
                    _owner._resizeSize = new Point(_owner.Width, _owner.Height);
                }
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                bool show = _owner._resizing || UIManager.MouseOverControl != null && UIManager.MouseOverControl.RootParent == _owner;

                if (!show)
                {
                    return true;
                }

                float layerDepth = layerDepthRef;
                int w = Width, h = Height;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Vector3 hv = ShaderHueTranslator.GetHueVector(0, false, 1f);
                    var tex = SolidColorTextureCache.GetTexture(Mark);

                    // A small triangle of dots in the corner.
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col <= row; col++)
                        {
                            batcher.Draw(tex, new Rectangle(x + w - 3 - col * 3, y + h - 3 - (row - col) * 3, 2, 2), hv, layerDepth);
                        }
                    }

                    return true;
                });

                return true;
            }
        }
    }
}
