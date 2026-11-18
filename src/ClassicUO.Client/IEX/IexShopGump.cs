// The shard's shop window (Options "Modern shop window", needs the Dark or Light theme): one
// resizable window for buying and selling instead of the two classic scrolls.
//   left   the vendor's list: picture, name, price, how many; an amount box per line (0 to start) with - and + under it
//          (double-click a line for one more, Shift + double-click for all of it)
//   top    search box, sort (vendor order / name / price), and on the sell window "Add all"
//          (everything shown goes in the cart)
//   right  the cart, with the total, your gold, Clear and Accept
// Stock ShopGump still receives the packets: this is a subclass created by ShopGump.Create.

using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Network;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal sealed class IexShopGump : ShopGump
    {
        public static bool Use => IexDarkUi.Active && (ProfileManager.CurrentProfile?.IexModernShop ?? false);

        const int MinWidth = 440, MinHeight = 300;
        const int Frame = 13, RowHeight = 46, CartRowHeight = 18, ArtSize = 40;
        static readonly string[] SortNames = { "Vendor order", "Name", "Price: low", "Price: high" };

        sealed class Entry
        {
            public uint Serial;
            public ushort Graphic, Hue;
            public int Available;
            public uint Price;
            public string Name;
            public int Order;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<uint, int> _cart = new Dictionary<uint, int>();
        readonly Dictionary<uint, StbTextBox> _amountBoxes = new Dictionary<uint, StbTextBox>();

        string _search = string.Empty;
        int _sort;
        bool _listDirty, _settingAmount;

        StbTextBox _searchBox;
        DataBox _rows, _cartRows;
        Label _total, _gold;

        bool _resizing;
        Point _resizeMouse, _resizeSize;

        public IexShopGump(World world, uint serial, bool isBuyGump, int x, int y) : base(world, serial, isBuyGump)
        {
            X = x;
            Y = y;
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            AcceptKeyboardInput = true;
            WantUpdateSize = false;

            Point size = ProfileManager.CurrentProfile.IexShopSize;

            if (size.X <= 0 || size.Y <= 0) // earlier builds saved the size empty ({}), read back as 0,0
            {
                size = new Point(580, 420);
            }
            Width = Math.Max(MinWidth, size.X);
            Height = Math.Max(MinHeight, size.Y);
            Build();
        }

        // ------------------------------------------------------------------ fed by the packet handlers

        public override void AddItem(uint serial, ushort graphic, ushort hue, ushort amount, uint price, string name, bool fromcliloc)
        {
            if (!SerialHelper.IsValid(serial) || _entries.Any(e => e.Serial == serial))
            {
                return;
            }

            _entries.Add(new Entry
            {
                Serial = serial,
                Graphic = graphic,
                Hue = hue,
                Available = amount,
                Price = price,
                Name = StringHelper.CapitalizeAllWords(name ?? string.Empty),
                Order = _entries.Count
            });
            _listDirty = true;
        }

        public override void SetNameTo(Item item, string name)
        {
            Entry e = _entries.FirstOrDefault(x => x.Serial == item.Serial);

            if (e != null && !string.IsNullOrEmpty(name))
            {
                e.Name = StringHelper.CapitalizeAllWords(name);
                _listDirty = true;
            }
        }

        // ------------------------------------------------------------------ layout

        void Build()
        {
            bool searchFocused = _searchBox != null && UIManager.KeyboardFocusControl == _searchBox;
            RemoveAll(this);
            _amountBoxes.Clear();

            Add(new ResizePic(0x0E10) { Width = Width, Height = Height });

            // Title plate
            GumpPic title = IexDarkUi.TitlePic(IsBuyGump ? "Buy" : "Sell");
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

            // Toolbar: search, sort, and "Add all" on the sell window.
            int y = Frame + plateH + 6;
            int right = Width - Frame - 4;

            if (!IsBuyGump)
            {
                int w = TextButton(0, y, "Add all", AddAllShown, measureOnly: true);
                right -= w;
                TextButton(right, y, "Add all", AddAllShown);
                right -= 8;
            }

            var sort = new Combobox(right - 100, y, 100, SortNames, _sort, font: 1);
            sort.OnOptionSelected += (s, i) =>
            {
                _sort = i;
                RebuildList();
            };
            Add(sort);
            right -= 108;

            AddSearch(Frame + 4, y, right - Frame - 4);
            y += 28;

            // Body: vendor list (left), cart (right), footer under the cart.
            int footerH = 52;
            int bottom = Height - Frame;
            int listW = (Width - 2 * Frame - 8) * 3 / 5;
            int cartX = Frame + listW + 8, cartW = Width - Frame - cartX;

            Add(new ResizePic(0x0BB8) { X = Frame, Y = y, Width = listW, Height = bottom - y, AcceptMouseInput = false });
            var list = new ScrollArea(Frame + 4, y + 4, listW - 6, bottom - y - 8, true) { AcceptMouseInput = true, CanMove = true };
            _rows = new DataBox(0, 0, listW - 22, 1) { AcceptMouseInput = true, CanMove = true, WantUpdateSize = false };
            list.Add(_rows);
            Add(list);

            int cartBottom = bottom - footerH;
            Add(new ResizePic(0x0BB8) { X = cartX, Y = y, Width = cartW, Height = cartBottom - y, AcceptMouseInput = false });
            var cart = new ScrollArea(cartX + 4, y + 4, cartW - 6, cartBottom - y - 8, true) { AcceptMouseInput = true, CanMove = true };
            _cartRows = new DataBox(0, 0, cartW - 22, 1) { AcceptMouseInput = true, CanMove = true, WantUpdateSize = false };
            cart.Add(_cartRows);
            Add(cart);

            int fy = cartBottom + 6;
            _total = new Label(string.Empty, true, IexDarkUi.AccentHue, font: 1) { X = cartX + 4, Y = fy, AcceptMouseInput = false };
            Add(_total);

            if (IsBuyGump)
            {
                _gold = new Label(string.Empty, true, IexDarkUi.DimHue, font: 1) { X = cartX + 4, Y = fy + 16, AcceptMouseInput = false };
                Add(_gold);
            }
            else
            {
                _gold = null;
            }

            int by = bottom - 22;
            int acceptW = TextButton(0, by, "Accept", Accept, measureOnly: true);
            TextButton(Width - Frame - acceptW, by, "Accept", Accept, accent: true);
            int clearW = TextButton(0, by, "Clear", ClearCart, measureOnly: true);
            TextButton(Width - Frame - acceptW - 6 - clearW, by, "Clear", ClearCart);

            // Resize grip, bottom-right.
            var grip = new GumpPic(Width - 12, Height - 12, IexDarkArt.ResizeGrip, 0) { AcceptMouseInput = true, CanMove = false };
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

            RebuildList();

            if (searchFocused)
            {
                _searchBox.SetKeyboardFocus();
            }
        }

        void AddSearch(int x, int y, int w)
        {
            Add(new ResizePic(0x0BB8) { X = x, Y = y, Width = w, Height = 20, AcceptMouseInput = false });

            var hint = new Label("Search...", true, IexDarkUi.DimHue, font: 1) { X = x + 6, Y = y + 3, AcceptMouseInput = false, IsVisible = _search.Length == 0 };
            Add(hint);

            _searchBox = new StbTextBox(1, 40, w - 10, true, hue: IexDarkUi.TextHue)
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

        // A framed text button, like the skills window's tabs. Returns its width.
        int TextButton(int x, int y, string text, Action click, bool measureOnly = false, bool accent = false)
        {
            var label = new Label(text, true, accent ? IexDarkUi.AccentHue : IexDarkUi.TextHue, font: 1) { AcceptMouseInput = false };
            int w = label.Width + 16;

            if (measureOnly)
            {
                label.Dispose();
                return w;
            }

            Add(new ResizePic(accent ? (ushort)0x0E10 : (ushort)0x0BB8) { X = x, Y = y, Width = w, Height = 20, AcceptMouseInput = false });
            label.X = x + 8;
            label.Y = y + 2;
            Add(label);
            var hit = new HitBox(x, y, w, 20, alpha: 0.1f);
            hit.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    click();
                }
            };
            Add(hit);
            return w;
        }

        // ------------------------------------------------------------------ vendor list

        IEnumerable<Entry> Shown()
        {
            IEnumerable<Entry> list = _entries;

            if (_search.Length > 0)
            {
                list = list.Where(e => e.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            switch (_sort)
            {
                case 1: return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                case 2: return list.OrderBy(e => e.Price).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                case 3: return list.OrderByDescending(e => e.Price).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
                default: return list.OrderBy(e => e.Order);
            }
        }

        void RebuildList()
        {
            _listDirty = false;

            if (_rows == null)
            {
                return;
            }

            RemoveAll(_rows);
            _amountBoxes.Clear();
            int y = 0, w = _rows.Width;

            foreach (Entry e in Shown())
            {
                _rows.Add(Row(e, y, w));
                y += RowHeight;
            }

            _rows.Height = Math.Max(1, y);
            RebuildCart();
        }

        Control Row(Entry e, int y, int w)
        {
            var row = new RowControl { X = 0, Y = y, Width = w, Height = RowHeight };
            row.Add(new ItemArt(e.Serial, e.Graphic, e.Hue, IsBuyGump) { X = 2, Y = 2, Width = ArtSize, Height = ArtSize });

            const int boxW = 52;
            int textW = w - ArtSize - boxW - 16;
            row.Add(new Label(e.Name, true, IexDarkUi.TextHue, textW, font: 1, style: FontStyle.Cropped) { X = ArtSize + 8, Y = 5, AcceptMouseInput = false });
            row.Add(new Label($"{e.Price} gp each, {e.Available} {(IsBuyGump ? "for sale" : "yours")}", true, IexDarkUi.DimHue, textW, font: 1, style: FontStyle.Cropped)
            {
                X = ArtSize + 8, Y = 22, AcceptMouseInput = false
            });

            // Amount box: how many of this line are in the cart (0 to start), with - and + under it.
            int bx = w - boxW - 2, by = 2;
            row.Add(new ResizePic(0x0BB8) { X = bx, Y = by, Width = boxW, Height = 20, AcceptMouseInput = false });
            var box = new StbTextBox(1, 5, boxW - 8, true, hue: IexDarkUi.AccentHue)
            {
                X = bx + 4,
                Y = by + 2,
                Width = boxW - 8,
                Height = 16,
                NumbersOnly = true
            };
            _cart.TryGetValue(e.Serial, out int inCart);
            box.SetText(inCart.ToString());

            int stepW = (boxW - 2) / 2;
            StepButton(row, bx, by + 22, stepW, "-", "One fewer (Shift: none)", () =>
            {
                _cart.TryGetValue(e.Serial, out int now);
                SetAmount(e, Keyboard.Shift ? 0 : now - 1, fromBox: false);
            });
            StepButton(row, bx + boxW - stepW, by + 22, stepW, "+", "One more (Shift: all)", () =>
            {
                _cart.TryGetValue(e.Serial, out int now);
                SetAmount(e, Keyboard.Shift ? e.Available : now + 1, fromBox: false);
            });
            box.TextChanged += (s, a) =>
            {
                if (_settingAmount)
                {
                    return;
                }

                int.TryParse(box.Text, out int n);
                SetAmount(e, n, fromBox: true);
            };
            row.Add(box);
            _amountBoxes[e.Serial] = box;

            if (World.ClientFeatures.TooltipsEnabled)
            {
                row.SetTooltip(e.Serial);
            }

            row.MouseDoubleClick += (s, a) =>
            {
                if (a.Button == MouseButtonType.Left)
                {
                    _cart.TryGetValue(e.Serial, out int now);
                    SetAmount(e, Keyboard.Shift ? e.Available : now + 1, fromBox: false);
                    a.Result = true;
                }
            };

            return row;
        }

        // A small framed - or + under a line's amount box.
        static void StepButton(Control row, int x, int y, int w, string text, string tooltip, Action click)
        {
            row.Add(new ResizePic(0x0BB8) { X = x, Y = y, Width = w, Height = 18, AcceptMouseInput = false });
            var label = new Label(text, true, IexDarkUi.AccentHue, font: 1) { AcceptMouseInput = false };
            label.X = x + (w - label.Width) / 2;
            label.Y = y + 1;
            row.Add(label);
            var hit = new HitBox(x, y, w, 18, tooltip, 0.1f);
            hit.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    click();
                }
            };
            row.Add(hit);
        }

        void SetAmount(Entry e, int n, bool fromBox)
        {
            n = Math.Clamp(n, 0, Math.Min(e.Available, ushort.MaxValue));

            if (n > 0)
            {
                _cart[e.Serial] = n;
            }
            else
            {
                _cart.Remove(e.Serial);
            }

            // Show the clamped number (or the new one after a double-click) in the line's box.
            if (_amountBoxes.TryGetValue(e.Serial, out StbTextBox box))
            {
                string want = n.ToString();
                string now = box.Text ?? string.Empty;

                // While typing, an emptied box stays empty (counts as 0) so a new number can go in.
                if (!fromBox || now.Length > 0 && now != want)
                {
                    _settingAmount = true;
                    box.SetText(want);
                    _settingAmount = false;
                }
            }

            RebuildCart();
        }

        void AddAllShown()
        {
            foreach (Entry e in Shown().ToList())
            {
                SetAmount(e, e.Available, fromBox: false);
            }
        }

        void ClearCart()
        {
            foreach (Entry e in _entries.Where(x => _cart.ContainsKey(x.Serial)).ToList())
            {
                SetAmount(e, 0, fromBox: false);
            }
        }

        // ------------------------------------------------------------------ cart

        void RebuildCart()
        {
            if (_cartRows == null)
            {
                return;
            }

            RemoveAll(_cartRows);
            int y = 0, w = _cartRows.Width;
            long total = 0;

            foreach (Entry e in _entries.OrderBy(x => x.Order))
            {
                if (!_cart.TryGetValue(e.Serial, out int n))
                {
                    continue;
                }

                long sub = (long)n * e.Price;
                total += sub;

                var row = new RowControl { X = 0, Y = y, Width = w, Height = CartRowHeight };
                var price = new Label(sub.ToString(), true, IexDarkUi.TextHue, font: 1) { Y = 0, AcceptMouseInput = false };
                price.X = w - 16 - price.Width;
                row.Add(new Label($"{n} x {e.Name}", true, IexDarkUi.TextHue, Math.Max(20, price.X - 4), font: 1, style: FontStyle.Cropped) { X = 2, Y = 0, AcceptMouseInput = false });
                row.Add(price);

                var remove = new Label("x", true, IexDarkUi.DimHue, font: 1) { X = w - 10, Y = 0, AcceptMouseInput = false };
                row.Add(remove);
                Entry entry = e;
                var hit = new HitBox(w - 13, 0, 13, CartRowHeight, "Take out of the cart", 0.1f);
                hit.MouseUp += (s, a) =>
                {
                    if (a.Button == MouseButtonType.Left)
                    {
                        SetAmount(entry, 0, fromBox: false);
                    }
                };
                row.Add(hit);

                _cartRows.Add(row);
                y += CartRowHeight;
            }

            _cartRows.Height = Math.Max(1, y);

            if (_total != null)
            {
                _total.Text = IsBuyGump ? $"Total: {total} gp" : $"You get: {total} gp";
            }
        }

        void Accept()
        {
            Tuple<uint, ushort>[] items = _cart.Select(c => new Tuple<uint, ushort>(c.Key, (ushort)c.Value)).ToArray();

            if (IsBuyGump)
            {
                NetClient.Socket.Send_BuyRequest(LocalSerial, items);
            }
            else
            {
                NetClient.Socket.Send_SellRequest(LocalSerial, items);
            }

            Dispose();
        }

        // ------------------------------------------------------------------ update, resize

        public override void Update()
        {
            base.Update(); // ShopGump.Update sees no stock controls and only runs Gump.Update

            if (IsDisposed || !World.InGame)
            {
                return;
            }

            // Names can arrive after the list (tooltips); rebuild unless an amount is being typed.
            if (_listDirty && !_amountBoxes.Values.Contains(UIManager.KeyboardFocusControl))
            {
                RebuildList();
            }

            if (_gold != null && World.Player != null)
            {
                _gold.Text = $"Your gold: {World.Player.Gold}";
            }

            if (_resizing)
            {
                Point d = Mouse.Position - _resizeMouse;
                int w = Math.Max(MinWidth, _resizeSize.X + d.X), h = Math.Max(MinHeight, _resizeSize.Y + d.Y);

                if (!Mouse.LButtonPressed)
                {
                    _resizing = false;
                }

                if (!_resizing ? w != Width || h != Height : Math.Abs(w - Width) >= 6 || Math.Abs(h - Height) >= 6)
                {
                    Width = w;
                    Height = h;
                    ProfileManager.CurrentProfile.IexShopSize = new Point(w, h);
                    Build();
                }
            }
        }

        // Take the old controls out now (see IexSkillsGump.RemoveAll).
        static void RemoveAll(Control parent)
        {
            foreach (Control c in parent.Children.ToArray())
            {
                parent.Remove(c);
                c.Dispose();
            }
        }

        sealed class RowControl : Control
        {
            public RowControl()
            {
                AcceptMouseInput = true;
                CanMove = true;
                WantUpdateSize = false;
            }
        }

        // ------------------------------------------------------------------ item picture

        // The item's art fitted into its box; a creature for sale is drawn standing (as stock does).
        sealed class ItemArt : Control
        {
            readonly uint _serial;
            readonly ushort _graphic, _hue;
            readonly bool _buy;

            public ItemArt(uint serial, ushort graphic, ushort hue, bool buy)
            {
                _serial = serial;
                _graphic = graphic;
                _hue = hue;
                _buy = buy;
                AcceptMouseInput = false;
                WantUpdateSize = false;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;

                if (_buy && SerialHelper.IsMobile(_serial))
                {
                    var animations = Client.Game.UO.Animations;
                    ushort graphic = _graphic < animations.MaxAnimationCount ? _graphic : (ushort)0;
                    byte group = StandGroup(animations, graphic);
                    var frames = animations.GetAnimationFrames(graphic, group, 1, out ushort hue2, out _, false);

                    if (frames.Length != 0 && frames[0].Texture != null)
                    {
                        var info = frames[0];
                        Vector3 hueVector = ShaderHueTranslator.GetHueVector(hue2, false, 1f);
                        Rectangle dest = Fit(info.UV.Width, info.UV.Height, x, y);
                        renderLists.AddGumpWithAtlas(batcher =>
                        {
                            batcher.Draw(info.Texture, dest, info.UV, hueVector, layerDepth);
                            return true;
                        });
                    }
                }
                else
                {
                    ref readonly var art = ref Client.Game.UO.Arts.GetArt(_graphic);

                    if (art.Texture != null)
                    {
                        var texture = art.Texture;
                        Rectangle bounds = Client.Game.UO.Arts.GetRealArtBounds(_graphic);
                        Rectangle src = new Rectangle(art.UV.X + bounds.X, art.UV.Y + bounds.Y, bounds.Width, bounds.Height);
                        Rectangle dest = Fit(bounds.Width, bounds.Height, x, y);
                        Vector3 hueVector = ShaderHueTranslator.GetHueVector(_hue, Client.Game.UO.FileManager.TileData.StaticData[_graphic].IsPartialHue, 1f);
                        renderLists.AddGumpWithAtlas(batcher =>
                        {
                            batcher.Draw(texture, dest, src, hueVector, layerDepth);
                            return true;
                        });
                    }
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }

            // Centred in the box, shrunk (never enlarged) to fit.
            Rectangle Fit(int w, int h, int x, int y)
            {
                float s = Math.Min(1f, Math.Min(Width / (float)Math.Max(1, w), Height / (float)Math.Max(1, h)));
                int dw = Math.Max(1, (int)(w * s)), dh = Math.Max(1, (int)(h * s));
                return new Rectangle(x + (Width - dw) / 2, y + (Height - dh) / 2, dw, dh);
            }

            static byte StandGroup(Renderer.Animations.Animations animations, ushort graphic)
            {
                var type = animations.GetAnimType(graphic);

                switch (Client.Game.UO.FileManager.Animations.GetGroupIndex(graphic, type))
                {
                    case ClassicUO.Assets.AnimationGroups.Low: return (byte)ClassicUO.Assets.LowAnimationGroup.Stand;
                    case ClassicUO.Assets.AnimationGroups.High: return (byte)ClassicUO.Assets.HighAnimationGroup.Stand;
                    case ClassicUO.Assets.AnimationGroups.People: return (byte)ClassicUO.Assets.PeopleAnimationGroup.Stand;
                }

                return 0;
            }
        }
    }
}
