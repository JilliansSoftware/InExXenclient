// Art for the "Dark windows" option: black stone with gold trim for every stock window
// background family, drawn in code from a cross-section (edge, gold, stone band, gold, shadow,
// fill). Depends only on the asset loader, so it can be run outside the game to preview.

using System;
using System.Collections.Generic;
using ClassicUO.Assets;

namespace ClassicUO.IEX
{
    internal static class IexDarkArt
    {
        // Window background families (first of nine pieces). Only families whose nine pieces all
        // exist in the installed art are swapped.
        static readonly ushort[] Families =
        {
            0x0A28, 0x0A3C, 0x0BB8, 0x0DAC, 0x0E10, 0x13BE, 0x13EC, 0x1400, 0x141E, 0x1432,
            0x23F0, 0x2422, 0x242C, 0x2436, 0x2454, 0x2486, 0x24A4, 0x24B8, 0x24EA, 0x251C,
        };

        // Palette (picked in the mockups; reduced to UO's 15-bit colors when drawn).
        static readonly (int R, int G, int B) Edge = (6, 6, 8);
        static readonly (int R, int G, int B) Band = (62, 62, 68);
        static readonly (int R, int G, int B) BandHi = (100, 100, 108);
        static readonly (int R, int G, int B) BandLo = (30, 30, 34);
        static readonly (int R, int G, int B) Fill = (16, 16, 19);
        static readonly (int R, int G, int B) Shadow = (4, 4, 5);
        static readonly (int R, int G, int B) GoldHi = (248, 216, 112);
        static readonly (int R, int G, int B) Gold = (224, 168, 48);
        static readonly (int R, int G, int B) GoldLo = (136, 88, 16);
        enum Side { Top, Left, Bottom, Right }

        public static void Build(UOFileManager files, ICollection<ushort> active)
        {
            GumpsLoader loader = files.Gumps;
            // Stone texture from the stock grey stone background, used darkened in the band.
            float[] stone = null;
            int sw = 0, sh = 0;
            var tile = loader.GetGump(0x13BE + 4);

            if (tile.Width > 0)
            {
                sw = tile.Width;
                sh = tile.Height;
                stone = new float[sw * sh];
                float sum = 0;

                for (int i = 0; i < stone.Length; i++)
                {
                    uint c = tile.Pixels[i];
                    stone[i] = ((c & 0xFF) + ((c >> 8) & 0xFF) + ((c >> 16) & 0xFF)) / 3f;
                    sum += stone[i];
                }

                float avg = sum / stone.Length;

                for (int i = 0; i < stone.Length; i++)
                {
                    stone[i] = avg > 0 ? stone[i] / avg : 1f;
                }
            }

            int len = sw > 0 ? Math.Min(sw, 64) : 32;

            foreach (ushort family in Families)
            {
                int top = 0, left = 0, bottom = 0, right = 0;
                bool complete = true;

                for (int i = 0; i < 9; i++)
                {
                    var g = loader.GetGump((uint)(family + i));

                    if (g.Width <= 0)
                    {
                        complete = false;
                        break;
                    }

                    // ResizePic order: 0 TL, 1 T, 2 TR, 3 L, 4 C, 5 R, 6 BL, 7 B, 8 BR
                    switch (i)
                    {
                        case 1: top = g.Height; break;
                        case 3: left = g.Width; break;
                        case 5: right = g.Width; break;
                        case 7: bottom = g.Height; break;
                    }
                }

                if (!complete)
                {
                    continue;
                }

                // Never thicker than the original border, so server layouts still fit inside.
                int f = Math.Clamp(Math.Min(Math.Min(top, bottom), Math.Min(left, right)), 3, 13);
                var frame = new Frame(f, stone, sw, sh);

                Put(loader, family + 0, f, f, frame, (x, y) => Corner(frame, x, y, f, f, false, false));
                Put(loader, family + 1, len, f, frame, (x, y) => frame.At(Side.Top, y, x));
                Put(loader, family + 2, f, f, frame, (x, y) => Corner(frame, x, y, f, f, true, false));
                Put(loader, family + 3, f, len, frame, (x, y) => frame.At(Side.Left, x, y));
                Put(loader, family + 4, len, len, frame, (x, y) => frame.FillAt(x, y));
                Put(loader, family + 5, f, len, frame, (x, y) => frame.At(Side.Right, f - 1 - x, y));
                Put(loader, family + 6, f, f, frame, (x, y) => Corner(frame, x, y, f, f, false, true));
                Put(loader, family + 7, len, f, frame, (x, y) => frame.At(Side.Bottom, f - 1 - y, x));
                Put(loader, family + 8, f, f, frame, (x, y) => Corner(frame, x, y, f, f, true, true));

                active.Add(family);
            }

            Controls(files, loader, stone, sw, sh);
        }

        // ---------------------------------------------------------------- controls

        // Stock gem buttons: three states each (normal, pressed, highlighted), all 63x23.
        static readonly (int First, string Label)[] Buttons =
        {
            (0x00EE, "Apply"), (0x00F1, "Cancel"), (0x00F4, "Default"), (0x00F7, "Okay"),
            // paperdoll
            (0x07EF, "HELP"), (0x07D6, "OPTIONS"), (0x07D9, "LOG OUT"), (0x07DC, "JOURNAL"), (0x57B5, "QUESTS"),
            (0x07DF, "SKILLS"), (0x57B2, "GUILD"), (0x07E5, "PEACE"), (0x07E8, "WAR"), (0x07EB, "STATUS"),
        };

        // Painted art shipped with the client (see branding/art/make_paperdoll.py).
        static readonly (string Resource, int Width, int Height, int[] Ids)[] Painted =
        {
            ("IEX.Art.paperdoll.rgba.gz", 262, 340, new[] { 0x07D0, 0x07D1 }), // paperdoll frame (self, others), 16px taller
            ("IEX.Art.virtue.rgba.gz", 32, 32, new[] { 0x0071 }),              // pentagram virtue button
            // branding/art/make_status.py
            ("IEX.Art.status.rgba.gz", 427, 184, new[] { 0x2A6C }),            // status window
            ("IEX.Art.statusbar.rgba.gz", 154, 60, new[] { 0x0803 }),          // own bar, peace
            ("IEX.Art.statusbar-war.rgba.gz", 154, 60, new[] { 0x0807 }),      // own bar, war
            ("IEX.Art.statusbar-other.rgba.gz", 154, 60, new[] { 0x0804 }),    // others' bars (grey)
            ("IEX.Art.statusbar-innocent.rgba.gz", 154, 60, new[] { BarInnocent }),
            ("IEX.Art.statusbar-ally.rgba.gz", 154, 60, new[] { BarAlly }),
            ("IEX.Art.statusbar-enemy.rgba.gz", 154, 60, new[] { BarEnemy }),
            ("IEX.Art.statusbar-murderer.rgba.gz", 154, 60, new[] { BarMurderer }),
            ("IEX.Art.statusbar-invulnerable.rgba.gz", 154, 60, new[] { BarInvulnerable }),
        };

        // Others' bars with the stone already coloured by notoriety (the stock client tints
        // the whole picture, which would turn the gold trim blue or red too).
        public const int BarInnocent = 0xFE40, BarAlly = 0xFE41, BarEnemy = 0xFE42, BarMurderer = 0xFE43, BarInvulnerable = 0xFE44;

        /// <summary>The coloured bar for a notoriety, or 0 to keep the grey one.</summary>
        public static ushort NotorietyBar(ClassicUO.Game.Data.NotorietyFlag flag)
        {
            switch (flag)
            {
                case ClassicUO.Game.Data.NotorietyFlag.Innocent: return BarInnocent;
                case ClassicUO.Game.Data.NotorietyFlag.Ally: return BarAlly;
                case ClassicUO.Game.Data.NotorietyFlag.Enemy: return BarEnemy;
                case ClassicUO.Game.Data.NotorietyFlag.Murderer: return BarMurderer;
                case ClassicUO.Game.Data.NotorietyFlag.Invulnerable: return BarInvulnerable;
                default: return 0;
            }
        }

        static void PaintedArt(GumpsLoader loader)
        {
            var assembly = typeof(IexDarkArt).Assembly;

            foreach (var (resource, w, h, ids) in Painted)
            {
                using var stream = assembly.GetManifestResourceStream(resource);

                if (stream == null)
                {
                    continue;
                }

                using var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
                var bytes = new byte[w * h * 4];
                int read = 0;

                while (read < bytes.Length)
                {
                    int n = gz.Read(bytes, read, bytes.Length - read);

                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                if (read != bytes.Length)
                {
                    continue;
                }

                var px = new uint[w * h];
                Buffer.BlockCopy(bytes, 0, px, 0, bytes.Length); // RGBA bytes = R | G<<8 | B<<16 | A<<24

                foreach (int id in ids)
                {
                    // Our own numbers (0xF200 up) are unused by the art. Stock art is only
                    // replaced where it has the width the client lays things out for (the
                    // status window differs in UOP gump files); ours may be taller (the
                    // paperdoll's three-line nameplate).
                    var stock = loader.GetGump((uint)id);

                    if (id >= 0xF200 || stock.Width == w && stock.Height <= h)
                    {
                        loader.IexOverrides[id] = (px, w, h);
                    }
                }
            }
        }

        static void Controls(UOFileManager files, GumpsLoader loader, float[] stone, int sw, int sh)
        {
            Glyphs font = Glyphs.Load(files.GetUOFilePath("unifont1.mul"));
            PaintedArt(loader);
            SkillsWindowArt(loader);

            // Paperdoll item slots: gold rim (0x2344, clear middle) on a dark fill (0x243A).
            var slot = loader.GetGump(0x2344);

            if (slot.Width > 0)
            {
                var px = new uint[slot.Width * slot.Height];

                for (int y = 0; y < slot.Height; y++)
                {
                    for (int x = 0; x < slot.Width; x++)
                    {
                        int d = Math.Min(Math.Min(x, y), Math.Min(slot.Width - 1 - x, slot.Height - 1 - y));
                        bool lit = Math.Min(x, y) == d;
                        px[y * slot.Width + x] = d == 0 ? Pack(Edge) : d == 1 ? Pack(lit ? GoldHi : GoldLo) : d == 2 ? Pack(Shadow) : 0;
                    }
                }

                loader.IexOverrides[0x2344] = (px, slot.Width, slot.Height);
            }

            Recolored(loader, 0x243A, (r, g, b, x, y) => Pack((Fill.R + Noise(x, y, 3), Fill.G + Noise(x, y, 3), Fill.B + Noise(x, y, 3))));

            // Text painted into the skills scroll: "New Group" (blue) to gold; the help line
            // (dark letters, tan engraving) to parchment letters with a dark shadow.
            Recolored(loader, 0x083A, (r, g, b, x, y) =>
            {
                float v = Math.Max(r, Math.Max(g, b)) / 255f;
                return v < 0.15f ? Pack(Edge) : GoldRamp((v - 0.15f) / 0.6f);
            });
            Recolored(loader, 0x0836, (r, g, b, x, y) => (r + g + b) / 3 < 90 ? Pack((232, 220, 190)) : Pack((24, 22, 20)));

            foreach (var (first, label) in Buttons)
            {
                for (int state = 0; state < 3; state++)
                {
                    var g = loader.GetGump((uint)(first + state));

                    if (g.Width <= 0)
                    {
                        continue;
                    }

                    bool pressed = state == 1;
                    var frame = new Frame(6, stone, sw, sh, pressed ? (52, 52, 60) : (34, 34, 40));
                    uint[] px = Box(g.Width, g.Height, frame);
                    font?.Draw(px, g.Width, g.Height, label, pressed ? 1 : 0, Pack(GoldHi), Pack(Edge));
                    loader.IexOverrides[first + state] = (px, g.Width, g.Height);
                }
            }

            // Checkbox (0xD2 empty, 0xD3 checked), 19x20.
            for (int id = 0x00D2; id <= 0x00D3; id++)
            {
                var g = loader.GetGump((uint)id);

                if (g.Width <= 0)
                {
                    continue;
                }

                uint[] px = Box(g.Width, g.Height, new Frame(4, null, 0, 0, (8, 8, 10)), inset: 2);

                if (id == 0x00D3)
                {
                    Check(px, g.Width, g.Height);
                }

                loader.IexOverrides[id] = (px, g.Width, g.Height);
            }

            // Scroll bar: 0xFA/0xFB up (pressed/normal), 0xFC/0xFD down, 0xFE thumb, 0xFF-0x101 track.
            Arrow(loader, 0x00FA, up: true, pressed: true);
            Arrow(loader, 0x00FB, up: true, pressed: false);
            Arrow(loader, 0x00FC, up: false, pressed: true);
            Arrow(loader, 0x00FD, up: false, pressed: false);

            var thumb = loader.GetGump(0x00FE);

            if (thumb.Width > 0)
            {
                uint[] px = Box(thumb.Width, thumb.Height, new Frame(4, null, 0, 0, (44, 44, 50)));

                for (int k = -1; k <= 1; k++)
                {
                    int y = thumb.Height / 2 + k * 3;

                    for (int x = 3; x < thumb.Width - 3; x++)
                    {
                        px[y * thumb.Width + x] = Pack(Gold);
                    }
                }

                loader.IexOverrides[0x00FE] = (px, thumb.Width, thumb.Height);
            }

            for (int id = 0x00FF; id <= 0x0101; id++)
            {
                var g = loader.GetGump((uint)id);

                if (g.Width <= 0)
                {
                    continue;
                }

                var px = new uint[g.Width * g.Height];

                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        int n = Noise(x, y, 2);
                        (int R, int G, int B) c = x == 0 || x == g.Width - 1 ? Edge
                            : x == 1 ? GoldHi
                            : x == g.Width - 2 ? GoldLo
                            : x == 2 ? Shadow
                            : (8 + n, 8 + n, 10 + n);
                        px[y * g.Width + x] = Pack(c);
                    }
                }

                loader.IexOverrides[id] = (px, g.Width, g.Height);
            }

            // Scroll expander grip (journal, skills): 0x82E normal, 0x82F pressed.
            for (int id = 0x082E; id <= 0x082F; id++)
            {
                var g = loader.GetGump((uint)id);

                if (g.Width <= 0)
                {
                    continue;
                }

                uint[] px = Box(g.Width, g.Height, new Frame(6, stone, sw, sh, id == 0x082F ? (52, 52, 60) : (34, 34, 40)));

                for (int k = -1; k <= 1; k++)
                {
                    int y = g.Height / 2 + k * 4;

                    for (int x = 7; x < g.Width - 7; x++)
                    {
                        px[y * g.Width + x] = Pack(GoldHi);
                        px[(y + 1) * g.Width + x] = Pack(GoldLo);
                    }
                }

                loader.IexOverrides[id] = (px, g.Width, g.Height);
            }
        }

        // A whole framed box: every pixel belongs to its nearest edge (mitered corners).
        static uint[] Box(int w, int h, Frame frame, int inset = 0)
        {
            var px = new uint[w * h];

            for (int y = inset; y < h - inset; y++)
            {
                for (int x = inset; x < w - inset; x++)
                {
                    int l = x - inset, t = y - inset, r = w - 1 - inset - x, b = h - 1 - inset - y;
                    int d = Math.Min(Math.Min(l, t), Math.Min(r, b));
                    Side side = d == t ? Side.Top : d == l ? Side.Left : d == b ? Side.Bottom : Side.Right;
                    px[y * w + x] = frame.At(side, d, side == Side.Top || side == Side.Bottom ? x : y);
                }
            }

            return px;
        }

        static void Check(uint[] px, int w, int h)
        {
            int cx = w / 2, cy = h / 2;
            int[,] pts = { { -4, 0 }, { -3, 1 }, { -2, 2 }, { -1, 3 }, { 0, 2 }, { 1, 1 }, { 2, 0 }, { 3, -1 }, { 4, -2 } };

            for (int i = 0; i < pts.GetLength(0); i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    int x = cx + pts[i, 0], y = cy + pts[i, 1] + k - 1;
                    px[y * w + x] = Pack(k == 0 ? GoldHi : Gold);
                }
            }
        }

        static void Arrow(GumpsLoader loader, int id, bool up, bool pressed)
        {
            var g = loader.GetGump((uint)id);

            if (g.Width <= 0)
            {
                return;
            }

            uint[] px = Box(g.Width, g.Height, new Frame(4, null, 0, 0, pressed ? (52, 52, 60) : (34, 34, 40)));
            int cx = g.Width / 2, cy = g.Height / 2;

            for (int row = 0; row < 5; row++)
            {
                int y = up ? cy - 2 + row : cy + 2 - row;

                for (int x = cx - row; x <= cx + row; x++)
                {
                    px[y * g.Width + x] = Pack(row == 0 ? GoldHi : Gold);
                }
            }

            loader.IexOverrides[id] = (px, g.Width, g.Height);
        }

        /// <summary>UO's bitmap Unicode font, read straight from unifont*.mul.</summary>
        sealed class Glyphs
        {
            readonly byte[] _data;

            Glyphs(byte[] data) => _data = data;

            public static Glyphs Load(string path)
            {
                try
                {
                    return System.IO.File.Exists(path) ? new Glyphs(System.IO.File.ReadAllBytes(path)) : null;
                }
                catch (System.IO.IOException)
                {
                    return null;
                }
            }

            bool Glyph(char ch, out int off, out int xo, out int yo, out int w, out int h)
            {
                off = BitConverter.ToInt32(_data, ch * 4);
                xo = yo = w = h = 0;

                if (off <= 0 || off + 4 > _data.Length)
                {
                    return false;
                }

                xo = (sbyte)_data[off];
                yo = (sbyte)_data[off + 1];
                w = (sbyte)_data[off + 2];
                h = (sbyte)_data[off + 3];
                return w > 0 && h > 0;
            }

            int Width(string s)
            {
                int width = 0;

                foreach (char ch in s)
                {
                    width += ch == ' ' ? 4 : Glyph(ch, out _, out int xo, out _, out int w, out _) ? xo + w + 1 : 0;
                }

                return width;
            }

            // Centered, with a one-pixel dark shadow so it reads on the button face.
            public void Draw(uint[] px, int pw, int ph, string s, int shift, uint color, uint shadow)
            {
                int x = (pw - Width(s)) / 2 + shift;
                int y0 = (ph - 12) / 2 - 2 + shift;

                foreach (char ch in s)
                {
                    if (ch == ' ')
                    {
                        x += 4;
                        continue;
                    }

                    if (!Glyph(ch, out int off, out int xo, out int yo, out int w, out int h))
                    {
                        continue;
                    }

                    int stride = (w + 7) / 8;

                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int gy = 0; gy < h; gy++)
                        {
                            for (int gx = 0; gx < w; gx++)
                            {
                                if ((_data[off + 4 + gy * stride + gx / 8] & (0x80 >> (gx % 8))) == 0)
                                {
                                    continue;
                                }

                                int tx = x + xo + gx + (pass == 0 ? 1 : 0), ty = y0 + yo + gy + (pass == 0 ? 1 : 0);

                                if (tx >= 0 && ty >= 0 && tx < pw && ty < ph)
                                {
                                    px[ty * pw + tx] = pass == 0 ? shadow : color;
                                }
                            }
                        }
                    }

                    x += xo + w + 1;
                }
            }
        }

        static uint Corner(Frame frame, int x, int y, int w, int h, bool right, bool bottom)
        {
            int dx = right ? w - 1 - x : x;
            int dy = bottom ? h - 1 - y : y;

            // Mitered: whichever edge is nearer owns the pixel.
            if (dy < dx || (dy == dx && !bottom))
            {
                return frame.At(bottom ? Side.Bottom : Side.Top, dy, x);
            }

            return frame.At(right ? Side.Right : Side.Left, dx, y);
        }

        static void Put(GumpsLoader loader, int id, int w, int h, Frame frame, Func<int, int, uint> pixel)
        {
            var px = new uint[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[y * w + x] = pixel(x, y);
                }
            }

            loader.IexOverrides[id] = (px, w, h);
        }

        /// <summary>The frame's cross-section: edge, gold, stone band, gold, shadow, then fill.</summary>
        sealed class Frame
        {
            readonly int _t, _band;
            readonly float[] _stone;
            readonly int _sw, _sh;

            readonly (int R, int G, int B) _fill;

            public Frame(int thickness, float[] stone, int sw, int sh, (int R, int G, int B)? fill = null)
            {
                _fill = fill ?? Fill;
                _stone = stone;
                _sw = sw;
                _sh = sh;
                // edge(1) + gold(t) + band + gold(t) + shadow(1) must fit in the thickness
                _t = thickness >= 9 ? 2 : 1;
                _band = Math.Max(0, thickness - 2 - 2 * _t);
            }

            // d = distance from the outer edge, along = position along that edge (for texture).
            public uint At(Side side, int d, int along)
            {
                bool lit = side == Side.Top || side == Side.Left;
                int x = side == Side.Top || side == Side.Bottom ? along : d;
                int y = side == Side.Top || side == Side.Bottom ? d : along;

                if (d == 0)
                {
                    return Pack(Edge);
                }

                d -= 1;

                if (d < _t)
                {
                    return Pack(GoldFor(d, lit));
                }

                d -= _t;

                if (d < _band)
                {
                    if (d == 0)
                    {
                        return Pack(Textured(lit ? BandHi : BandLo, x, y));
                    }

                    if (d == _band - 1 && _band > 2)
                    {
                        return Pack(Textured(lit ? BandLo : BandHi, x, y));
                    }

                    return Pack(Textured(Band, x, y));
                }

                d -= _band;

                if (d < _t)
                {
                    // inner gold line: bevel reversed, it is the far side of a raised band
                    return Pack(GoldFor(d, lit));
                }

                d -= _t;

                if (d == 0)
                {
                    return Pack(Shadow);
                }

                return FillAt(x, y);
            }

            public uint FillAt(int x, int y)
            {
                int n = Noise(x, y, 3);
                return Pack((_fill.R + n, _fill.G + n, _fill.B + n));
            }

            (int R, int G, int B) GoldFor(int k, bool lit)
            {
                if (k > 0 && k < _t - 0)
                {
                    return Gold;
                }

                return lit ? GoldHi : GoldLo;
            }

            (int R, int G, int B) Textured((int R, int G, int B) c, int x, int y)
            {
                float m = _stone != null ? _stone[(y % _sh) * _sw + (x % _sw)] : 1f + Noise(x, y, 5) / 40f;
                return ((int)(c.R * m), (int)(c.G * m), (int)(c.B * m));
            }
        }

        static int Noise(int x, int y, int amount)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (int)((h >> 16) % (uint)(amount * 2 + 1)) - amount;
        }

        // Art for the skills window (IexSkillsGump), at numbers the archive never uses.
        public const ushort UseButton = 0xFE10, UseButtonPressed = 0xFE11;
        public const ushort LockUp = 0xFE20, LockDown = 0xFE21, LockLocked = 0xFE22;
        public const ushort ResizeGrip = 0xFE30;

        static void SkillsWindowArt(GumpsLoader loader)
        {
            // Use button: small gold-rimmed box with a gold play arrow.
            for (int pressed = 0; pressed < 2; pressed++)
            {
                const int s = 11;
                var px = new uint[s * s];

                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        int d = Math.Min(Math.Min(x, y), Math.Min(s - 1 - x, s - 1 - y));
                        bool lit = Math.Min(x, y) == d;
                        px[y * s + x] = d == 0 ? Pack(lit ? GoldHi : GoldLo) : Pack(pressed == 1 ? (60, 54, 40) : (34, 34, 40));
                    }
                }

                for (int c = 0; c < 4; c++)
                {
                    for (int r = c; r < 7 - c; r++)
                    {
                        px[(2 + r) * s + 4 + c] = Pack(GoldHi);
                    }
                }

                loader.IexOverrides[pressed == 1 ? UseButtonPressed : UseButton] = (px, s, s);
            }

            // Lock states: raise, lower, locked (gold glyphs, 7x9, clear background).
            Icon(loader, LockUp, new[] { "...#...", "..###..", ".#####.", "#######", "...#...", "...#...", "...#...", "...#...", "......." });
            Icon(loader, LockDown, new[] { ".......", "...#...", "...#...", "...#...", "...#...", "#######", ".#####.", "..###..", "...#..." });
            Icon(loader, LockLocked, new[] { "..###..", ".#...#.", ".#...#.", "#######", "#######", "###.###", "###.###", "#######", "#######" });

            // Resize grip: gold corner steps.
            Icon(loader, ResizeGrip, new[] { "........#", ".......##", "......#.#", ".....##.#", "....#.#.#", "...##.#.#", "..#.#.#.#", ".##.#.#.#", "#########" });
        }

        static void Icon(GumpsLoader loader, ushort id, string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            var px = new uint[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (rows[y][x] == '#')
                    {
                        px[y * w + x] = Pack(y < h / 2 ? GoldHi : Gold);
                    }
                }
            }

            loader.IexOverrides[id] = (px, w, h);
        }

        /// <summary>The stock picture with every visible pixel run through a color mapping.</summary>
        static void Recolored(GumpsLoader loader, int id, Func<int, int, int, int, int, uint> map)
        {
            var g = loader.GetGump((uint)id);

            if (g.Width <= 0)
            {
                return;
            }

            var px = new uint[g.Width * g.Height];

            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    uint c = g.Pixels[y * g.Width + x];

                    if ((c >> 24) != 0)
                    {
                        px[y * g.Width + x] = map((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF), x, y);
                    }
                }
            }

            loader.IexOverrides[id] = (px, g.Width, g.Height);
        }

        /// <summary>Gold shade for t in 0..1 (dark to bright), packed.</summary>
        public static uint GoldRamp(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            var (a, b) = t < 0.5f ? (GoldLo, Gold) : (Gold, GoldHi);
            float k = t < 0.5f ? t * 2 : (t - 0.5f) * 2;
            return Pack(((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k)));
        }

        // R | G<<8 | B<<16 | A<<24, reduced to 15-bit like the rest of UO's art.
        static uint Pack((int R, int G, int B) c)
        {
            static uint Ch(int v) => (uint)((Math.Clamp(v, 0, 255) >> 3) * 255 / 31);
            return Ch(c.R) | (Ch(c.G) << 8) | (Ch(c.B) << 16) | 0xFF000000;
        }

    }
}
