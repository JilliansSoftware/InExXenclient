// Compact health bars: a small dark bar with the name, hits, and (for you and your party)
// mana and stamina, each with its numbers inside. Drag the corner to resize it; the new size
// becomes the default for the next bar of that kind (yours, or anyone else's).
// It is the style of "Use custom health bars" when Profile.IexCompactBars is on (new characters).

using System;
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
    internal sealed class IexHealthBar : BaseHealthBarGump
    {
        const int NameH = 17, Side = 4, Bottom = 3, Gap = 1, MinWidth = 70, MinBar = 4;
        const ushort GreyHue = 912;

        static readonly Color Back = new Color(0, 0, 0, 200);
        static readonly Color Empty = new Color(45, 40, 35);
        static readonly Color Edge = new Color(10, 10, 10);
        static readonly Color EdgeRed = new Color(200, 30, 30);
        static readonly Color Hits = new Color(205, 45, 40);
        static readonly Color Poison = new Color(60, 200, 60);
        static readonly Color Mortal = new Color(235, 205, 40); // yellow hits: can't be hurt
        static readonly Color Mana = new Color(40, 120, 230);
        static readonly Color Stam = new Color(235, 140, 30);
        static readonly Color Grey = new Color(110, 110, 110);

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

        static int MinHeight(bool full) => NameH + Bottom + (full ? 3 * MinBar + 2 * Gap : MinBar);

        static Point DefaultSize(bool self, bool full)
        {
            Profile p = ProfileManager.CurrentProfile;
            Point s = self ? p.IexBarSizeSelf : p.IexBarSizeOther;
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

            Add
            (
                _textBox = new StbTextBox(1, 32, Width - Side * 2, true, FontStyle.Cropped | FontStyle.BlackBorder, NameHue(entity), TEXT_ALIGN_TYPE.TS_CENTER)
                {
                    X = Side,
                    Y = 1,
                    Width = Width - Side * 2,
                    Height = NameH - 2,
                    IsEditable = false,
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

        ushort NameHue(Entity entity)
        {
            if (entity == null || _outOfRange)
            {
                return GreyHue;
            }

            return entity is Mobile m ? Notoriety.GetHue(m.NotorietyFlag) : GreyHue;
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

            if (_textBox != null)
            {
                ushort hue = NameHue(entity);

                if (_textBox.Hue != hue)
                {
                    _textBox.Hue = hue;
                }
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
            if (int.TryParse(xml.GetAttribute("iexw"), out int w) && int.TryParse(xml.GetAttribute("iexh"), out int h))
            {
                _w = w;
                _h = h;
            }

            base.Restore(xml);
        }

        // ------------------------------------------------------------------ the bars

        sealed class BarsView : Control
        {
            readonly IexHealthBar _owner;
            readonly BarText[] _text = { new BarText(), new BarText(), new BarText() };

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

                base.Dispose();
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

                int barsTop = NameH;
                int barW = Width - Side * 2;
                int barH = Math.Max(1, (Height - barsTop - Bottom - Gap * (count - 1)) / count);

                // Red edge: you in war mode, or the one you last targeted.
                bool red = self ? world.Player.InWarMode : !grey && world.TargetManager.LastTargetInfo.Serial == o.LocalSerial;

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

                int width = Width, height = Height;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Fill(batcher, x, y, width, height, Back, layerDepth);
                    Frame(batcher, x, y, width, height, red ? EdgeRed : Edge, layerDepth);

                    for (int i = 0; i < count; i++)
                    {
                        int by = y + barsTop + i * (barH + Gap);
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

                        Fill(batcher, x + Side, by, barW, barH, Empty, layerDepth);

                        int fill = grey ? barW : CalculatePercents(max, now, barW);

                        if (fill > 0)
                        {
                            Fill(batcher, x + Side, by, Math.Min(barW, fill), barH, c, layerDepth);
                        }

                        _text[i].Draw(batcher, x + Side, by, barW, barH, layerDepth);
                    }

                    return true;
                });

                return true;
            }

            static void Fill(UltimaBatcher2D batcher, int x, int y, int w, int h, Color c, float depth)
            {
                Vector3 hv = ShaderHueTranslator.GetHueVector(0, false, c.A / 255f);
                batcher.Draw(SolidColorTextureCache.GetTexture(new Color(c.R, c.G, c.B)), new Rectangle(x, y, w, h), hv, depth);
            }

            static void Frame(UltimaBatcher2D batcher, int x, int y, int w, int h, Color c, float depth)
            {
                Fill(batcher, x, y, w, 1, c, depth);
                Fill(batcher, x, y + h - 1, w, 1, c, depth);
                Fill(batcher, x, y, 1, h, c, depth);
                Fill(batcher, x + w - 1, y, 1, h, c, depth);
            }
        }

        // The numbers in a bar, shrunk to fit; hidden when the bar is too thin to read.
        sealed class BarText
        {
            RenderedText _rt;
            string _value = string.Empty;

            public void Set(string value)
            {
                if (value == _value)
                {
                    return;
                }

                _value = value;
                _rt?.Destroy();
                _rt = value.Length == 0 ? null : RenderedText.Create(value, 0x0481, 1, true, FontStyle.BlackBorder);
            }

            public void Draw(UltimaBatcher2D batcher, int x, int y, int w, int h, float depth)
            {
                if (_rt == null || _rt.IsDestroyed || h < 8 || _rt.Height == 0)
                {
                    return;
                }

                float scale = Math.Min(1f, (h + 3) / (float)_rt.Height);
                int tw = (int)(_rt.Width * scale), th = (int)(_rt.Height * scale);

                if (tw > w)
                {
                    return;
                }

                _rt.Draw(batcher, x + (w - tw) / 2, y + (h - th) / 2, depth, 1f, 0, scale);
            }

            public void Destroy()
            {
                _rt?.Destroy();
                _rt = null;
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
                if (button == MouseButtonType.Left)
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
