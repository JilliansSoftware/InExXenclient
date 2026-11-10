// "Dark windows" option: the stock resizable window backgrounds (the 9-piece ResizePic families
// that client and server windows are built from) are swapped for black stone with gold trim,
// drawn in code (IexDarkArt.cs) along with the stock buttons, checkboxes and scroll bars. Dark
// text inside those windows is lightened, server window titles become gold, and the journal and
// skills scrolls get the same frame. Stored per profile.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.IEX
{
    internal static class IexDarkUi
    {
        public static readonly (int R, int G, int B) Parchment = (232, 220, 190);

        static readonly HashSet<ushort> _active = new HashSet<ushort>();
        static readonly Dictionary<ushort, ushort> _readable = new Dictionary<ushort, ushort>();
        static readonly ConditionalWeakTable<Control, object> _titles = new ConditionalWeakTable<Control, object>();
        static readonly Dictionary<string, ushort> _titleIds = new Dictionary<string, ushort>();
        static ushort _parchmentHue;

        /// <summary>Gold for text in fonts that take a hue (ASCII 1/9 and Unicode); checked by eye.</summary>
        public const ushort GoldHue = 0x0501;

        // ASCII fonts 4 and 6 have their colors painted in and ignore hues.
        const byte BlueFont = 6;
        static bool _applied;

        public static bool Enabled => ProfileManager.CurrentProfile?.IexDarkUi ?? false;

        /// <summary>True while the dark art is installed (follows the profile option).</summary>
        public static bool Active => _applied;

        /// <summary>Install or remove the dark art to match the current profile.</summary>
        public static void Apply()
        {
            bool want = Enabled;

            if (want == _applied)
            {
                return;
            }

            GumpsLoader loader = Client.Game.UO.FileManager.Gumps;
            loader.IexOverrides.Clear();
            _active.Clear();
            _titleIds.Clear();

            if (want)
            {
                IexDarkArt.Build(Client.Game.UO.FileManager, _active);
            }

            _applied = want;
            Client.Game.UO.Gumps.Reload(); // drop cached textures so windows pick up the change

            if (want)
            {
                foreach (Gump gump in ClassicUO.Game.Managers.UIManager.Gumps)
                {
                    FixText(gump);
                }
            }
        }

        public static bool IsDarkFamily(ushort graphic) => _applied && _active.Contains(graphic);

        // ---------------------------------------------------------------- text

        /// <summary>
        /// Called when a window opens: if it is built on a darkened background, dark text in it
        /// is made readable, and a server window's top line becomes a gold title.
        /// </summary>
        public static void FixText(Gump gump)
        {
            if (!_applied || gump == null)
            {
                return;
            }

            ResizePic background = DarkBackground(gump);

            if (background == null)
            {
                return;
            }

            Recolor(gump);

            if (gump.IsFromServer)
            {
                MakeTitle(gump, background);
            }
        }

        /// <summary>Keep a dark scroll window's text readable (its labels can be rebuilt any time).</summary>
        public static void KeepReadable(Control root)
        {
            if (_applied && root != null)
            {
                Recolor(root);
            }
        }

        static ResizePic DarkBackground(Control c)
        {
            foreach (Control child in c.Children)
            {
                if (child is ResizePic pic && _active.Contains(pic.Graphic))
                {
                    return pic;
                }

                ResizePic inner = DarkBackground(child);

                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }

        static void Recolor(Control c)
        {
            foreach (Control child in c.Children)
            {
                switch (child)
                {
                    case Label label:
                        Fix(label.IexText);
                        break;

                    case CroppedText cropped:
                        Fix(cropped.IexText);
                        break;

                    case StbTextBox box:
                        if (box.IexText != null && !box.IexText.IsUnicode && box.Font == BlueFont)
                        {
                            box.Font = 1; // painted-blue font: swap for one that takes gold
                            box.Hue = GoldHue;
                        }

                        Fix(box.IexText);
                        break;

                    case HtmlControl html:
                        Fix(html.IexText);
                        break;

                    case Checkbox check:
                        Fix(check.IexText);
                        break;
                }

                Recolor(child);
            }
        }

        // The text's layout (with its colors baked in) is built when the text is set, so a new
        // color only shows after the text is set again.
        static void Fix(RenderedText t)
        {
            if (t == null)
            {
                return;
            }

            bool changed = false;

            if (!t.IsUnicode && t.Font == BlueFont)
            {
                // Painted-blue ASCII font (headers, tabs in the scroll windows): gold in font 1.
                t.Font = 1;
                t.Hue = GoldHue;
                changed = true;
            }
            else if (t.IsHTML)
            {
                uint html = ReadableHtml(t.HTMLColor);

                if (html != t.HTMLColor)
                {
                    t.HTMLColor = html;
                    changed = true;
                }
            }
            else
            {
                ushort hue = ReadableHue(t.Hue, !t.IsUnicode);

                if (hue != t.Hue)
                {
                    t.Hue = hue;
                    changed = true;
                }
            }

            if (changed && !string.IsNullOrEmpty(t.Text)) // empty boxes just keep the new color for typing
            {
                string text = t.Text;
                t.Text = string.Empty;
                t.Text = text;
            }
        }

        /// <summary>
        /// A text hue that reads on black: near-black becomes parchment, dark colors keep their
        /// color but lighter, light colors stay as they are. Unchanged when dark windows are off.
        /// </summary>
        public static ushort ReadableHue(ushort hue, bool ascii = false)
        {
            if (!_applied || hue == 0xFFFF)
            {
                return hue;
            }

            ushort key = (ushort)(ascii ? hue ^ 0x8000 : hue);

            if (_readable.TryGetValue(key, out ushort cached))
            {
                return cached;
            }

            // ASCII glyphs are mid-tone, so their hue shows nearer the middle of its ramp.
            uint c = Client.Game.UO.FileManager.Hues.GetPolygoneColor((ushort)(ascii ? 18 : 30), hue);
            var light = Lighten((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            ushort result = light == null ? hue : light.Value == Parchment ? ParchmentHue : Nearest(light.Value);
            _readable[key] = result;
            return result;
        }

        /// <summary>Same for HTML colors (0xBBGGRRAA).</summary>
        static uint ReadableHtml(uint color)
        {
            if (color == 0xFFFFFFFF || color == 0xFFFFFFFE)
            {
                return color;
            }

            var light = Lighten((int)((color >> 8) & 0xFF), (int)((color >> 16) & 0xFF), (int)((color >> 24) & 0xFF));

            if (light == null)
            {
                return color;
            }

            var (r, g, b) = light.Value;
            return ((uint)b << 24) | ((uint)g << 16) | ((uint)r << 8) | 0xFF;
        }

        // null = already readable
        static (int R, int G, int B)? Lighten(int r, int g, int b)
        {
            if (Luma(r, g, b) >= 100)
            {
                return null;
            }

            int max = Math.Max(r, Math.Max(g, b));

            if (max - Math.Min(r, Math.Min(g, b)) < 40)
            {
                return Parchment; // black or grey
            }

            // Keep the color, push it bright, then a little toward white.
            float k = 235f / max;
            int L(int v) => (int)(v * k * 0.75f + 255 * 0.25f);
            return (L(r), L(g), L(b));
        }

        static int Luma(int r, int g, int b) => (r * 299 + g * 587 + b * 114) / 1000;

        static ushort Nearest((int R, int G, int B) target)
        {
            HuesLoader hues = Client.Game.UO.FileManager.Hues;
            long best = long.MaxValue;
            ushort pick = 0x0481;

            for (int h = 1; h < hues.HuesCount; h++)
            {
                uint c = hues.GetPolygoneColor(30, (ushort)h);
                long dr = (c & 0xFF) - target.R, dg = ((c >> 8) & 0xFF) - target.G, db = ((c >> 16) & 0xFF) - target.B;
                long dist = dr * dr + dg * dg + db * db;

                if (dist < best)
                {
                    best = dist;
                    pick = (ushort)h;
                }
            }

            return pick;
        }

        internal static ushort ParchmentHue => _parchmentHue != 0 ? _parchmentHue : _parchmentHue = Nearest(Parchment);


        // ---------------------------------------------------------------- titles

        /// <summary>
        /// A window title in the shard style: ASCII font 4 in gold. That font ignores hues, so
        /// the text is drawn once, recolored and shown as a picture (gump numbers from 0xFF00,
        /// which the art never uses).
        /// </summary>
        public static GumpPic TitlePic(string text)
        {
            if (!_titleIds.TryGetValue(text, out ushort id))
            {
                id = (ushort)(0xFF00 + _titleIds.Count);
                var fi = Client.Game.UO.FileManager.Fonts.GenerateASCII(4, text, 0, 0, TEXT_ALIGN_TYPE.TS_LEFT, 0, false, 0);

                if (fi.Data == null || _titleIds.Count >= 0xFF)
                {
                    return null;
                }

                var px = new uint[fi.Width * fi.Height];

                for (int i = 0; i < px.Length; i++)
                {
                    uint c = fi.Data[i];

                    if ((c >> 24) == 0)
                    {
                        continue;
                    }

                    float v = Math.Max(c & 0xFF, Math.Max((c >> 8) & 0xFF, (c >> 16) & 0xFF)) / 255f;
                    px[i] = v < 0.12f ? 0xFF080606u : IexDarkArt.GoldRamp((v - 0.12f) / 0.6f);
                }

                Client.Game.UO.FileManager.Gumps.IexOverrides[id] = (px, fi.Width, fi.Height);
                _titleIds[text] = id;
            }

            return new GumpPic(0, 0, id, 0) { AcceptMouseInput = false };
        }

        /// <summary>Text for the scroll windows' title art (journal, skills).</summary>
        public static string ScrollTitle(int titleGumpId)
        {
            switch (titleGumpId)
            {
                case 0x082A: return "Journal";
                case 0x0834: return "Skills";
                default: return null;
            }
        }

        static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        // The top line of a server window, if it is a short single line near the top of its
        // dark background, is redrawn as a gold title.
        static void MakeTitle(Gump gump, ResizePic background)
        {
            foreach (Control c in gump.Children)
            {
                if (_titles.TryGetValue(c, out _))
                {
                    return; // already has one (a refreshed window is rebuilt and gets a new one)
                }
            }

            Control best = null;
            string bestText = null;

            foreach (Control c in gump.Children)
            {
                if (!c.IsVisible || c.Y > background.Y + 45)
                {
                    continue;
                }

                string text = c switch
                {
                    Label l => l.IexText.Text,
                    CroppedText ct => ct.IexText.Text,
                    HtmlControl h => h.IexText.Text,
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = Tags.Replace(text, string.Empty).Trim();

                if (text.Length == 0 || text.Length > 40 || text.Contains('\n') || c.Height > 26)
                {
                    continue;
                }

                if (best == null || c.Y < best.Y)
                {
                    best = c;
                    bestText = text;
                }
            }

            if (best == null)
            {
                return;
            }

            bool centered = best is HtmlControl html && html.IexText.Text.IndexOf("<center", StringComparison.OrdinalIgnoreCase) >= 0;
            GumpPic title = TitlePic(bestText);

            if (title == null)
            {
                return;
            }

            title.X = centered ? best.X + (best.Width - title.Width) / 2 : best.X;
            title.Y = best.Y + (best.Height - title.Height) / 2;
            best.IsVisible = false;
            _titles.AddOrUpdate(title, null);
            gump.Add(title, best.Page);
        }
    }
}
