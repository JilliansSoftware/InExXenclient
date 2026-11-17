// Window theme (Options > Fonts): Original keeps the stock art; Dark and Light swap the stock
// resizable window backgrounds (the 9-piece ResizePic families that client and server windows
// are built from) for art drawn in code (IexDarkArt.cs), along with the stock buttons,
// checkboxes and scroll bars: black stone with gold trim, or sandstone with bronze trim. Text
// that would not read on the new background is recolored (dark text lightened on Dark, light
// text darkened on Light), server window titles take the theme's title lettering, and the
// journal and skills scrolls get the same frame. Stored per profile.

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
    internal enum IexTheme
    {
        Original,
        Dark,
        Light
    }

    internal static class IexDarkUi
    {
        public static readonly string[] ThemeNames = { "Original", "Dark", "Light" };

        public static readonly (int R, int G, int B) Parchment = (232, 220, 190);

        // Light theme text: dark brown ink, a redder brown for titles and selections, and a
        // faded brown for hints.
        static readonly (int R, int G, int B) Ink = (64, 38, 16), Bronze = (132, 60, 14), Faded = (120, 92, 60);

        static readonly HashSet<ushort> _active = new HashSet<ushort>();
        static readonly Dictionary<ushort, ushort> _readable = new Dictionary<ushort, ushort>();
        static readonly ConditionalWeakTable<Control, object> _titles = new ConditionalWeakTable<Control, object>();
        static readonly Dictionary<string, ushort> _titleIds = new Dictionary<string, ushort>();
        static ushort _parchmentHue, _inkHue, _bronzeHue, _fadedHue;

        /// <summary>Gold for text in fonts that take a hue (ASCII 1/9 and Unicode); checked by eye.</summary>
        public const ushort GoldHue = 0x0501;

        // ASCII fonts 4 and 6 have their colors painted in and ignore hues.
        const byte BlueFont = 6;
        static IexTheme _applied;

        /// <summary>The theme the current profile asks for.</summary>
        public static IexTheme Wanted
        {
            get
            {
                Profile profile = ProfileManager.CurrentProfile;

                if (profile == null)
                {
                    return IexTheme.Original;
                }

                if (profile.IexTheme >= 0 && profile.IexTheme <= (int)IexTheme.Light)
                {
                    return (IexTheme)profile.IexTheme;
                }

                // Profiles from before the three-way choice only have the dark switch.
                return profile.IexDarkUi ? IexTheme.Dark : IexTheme.Original;
            }
        }

        /// <summary>True while themed art (dark or light) is installed.</summary>
        public static bool Active => _applied != IexTheme.Original;

        /// <summary>True while the light (sandstone) art is installed.</summary>
        public static bool Light => _applied == IexTheme.Light;

        /// <summary>Body text on the themed art: parchment on dark, brown ink on light.</summary>
        public static ushort TextHue => Light ? InkHue : ParchmentHue;

        /// <summary>Titles and selected entries: gold on dark, red-brown on light.</summary>
        public static ushort AccentHue => Light ? BronzeHue : GoldHue;

        /// <summary>Character names on the painted art: gold on dark, ink on light.</summary>
        public static ushort NameHue => Light ? InkHue : GoldHue;

        /// <summary>
        /// Where the paperdoll's virtue button goes: on the pentagram medallion of the themed
        /// frames (branding/art/make_paperdoll.py, VIRTUE), or the stock spot.
        /// </summary>
        public static (int X, int Y) VirtuePosition => Light ? (84, 10) : Active ? (87, 30) : (80, 4);

        /// <summary>Hints and unselected tabs.</summary>
        public static ushort DimHue => Light ? FadedHue : (ushort)0x03B2;

        /// <summary>Light text gets a black edge on the dark art; dark ink needs none.</summary>
        public static FontStyle TextEdge => Light ? FontStyle.None : FontStyle.BlackBorder;

        /// <summary>Install or remove the themed art to match the current profile.</summary>
        public static void Apply()
        {
            IexTheme want = Wanted;

            if (want == _applied)
            {
                return;
            }

            GumpsLoader loader = Client.Game.UO.FileManager.Gumps;
            loader.IexOverrides.Clear();
            _active.Clear();
            _titleIds.Clear();
            _readable.Clear(); // what reads well depends on the theme

            if (want != IexTheme.Original)
            {
                IexDarkArt.Build(Client.Game.UO.FileManager, _active, want == IexTheme.Light);
            }

            _applied = want;
            Client.Game.UO.Gumps.Reload(); // drop cached textures so windows pick up the change

            // Open paperdolls placed the virtue button and name for the old frame.
            foreach (Gump gump in ClassicUO.Game.Managers.UIManager.Gumps)
            {
                if (gump is PaperDollGump paperdoll && !paperdoll.IsDisposed)
                {
                    paperdoll.IexRetheme();
                }
            }

            if (want != IexTheme.Original)
            {
                foreach (Gump gump in ClassicUO.Game.Managers.UIManager.Gumps)
                {
                    FixText(gump);
                }
            }
        }

        public static bool IsDarkFamily(ushort graphic) => Active && _active.Contains(graphic);

        // ---------------------------------------------------------------- text

        /// <summary>
        /// Called when a window opens: if it is built on a themed background, text that would
        /// not read on it is recolored, and a server window's top line becomes a title.
        /// </summary>
        public static void FixText(Gump gump)
        {
            if (!Active || gump == null)
            {
                return;
            }

            ResizePic background = DarkBackground(gump);

            if (background == null)
            {
                return;
            }

            Recolor(gump, gump);

            if (gump.IsFromServer)
            {
                MakeTitle(gump, background);
            }
        }

        /// <summary>Keep a themed scroll window's text readable (its labels can be rebuilt any time).</summary>
        public static void KeepReadable(Control root)
        {
            if (Active && root != null)
            {
                Recolor(root, root);
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

        static void Recolor(Control c, Control root)
        {
            foreach (Control child in c.Children)
            {
                // On the light theme only text that really sits on sandstone is darkened:
                // windows also put light text on their own dark panels and on see-through
                // (darkened) regions, where it must stay light.
                if (Light && !OnSandstone(child, root))
                {
                    Recolor(child, root);
                    continue;
                }

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
                            box.Font = 1; // painted-blue font: swap for one that takes a hue
                            box.Hue = AccentHue;
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

                Recolor(child, root);
            }
        }

        // Light theme: is this control's middle over a themed background, with no darkening
        // layer (see-through region, black panel) over the same spot?
        static bool OnSandstone(Control c, Control root)
        {
            if (!(c is Label || c is CroppedText || c is StbTextBox || c is HtmlControl || c is Checkbox))
            {
                return true; // not text: nothing to decide
            }

            int x = c.Width / 2, y = Math.Min(c.Height / 2, 8);

            for (Control k = c; k != null && k != root; k = k.Parent)
            {
                x += k.X;
                y += k.Y;
            }

            bool sand = false;
            Covers(root, 0, 0, x, y, ref sand);
            return sand;
        }

        // Walks the window in drawing order; the last background or darkening layer under the
        // point decides.
        static void Covers(Control c, int ox, int oy, int x, int y, ref bool sand)
        {
            foreach (Control child in c.Children)
            {
                int cx = ox + child.X, cy = oy + child.Y;

                if (child.IsVisible && x >= cx && y >= cy && x < cx + child.Width && y < cy + child.Height)
                {
                    if (child is ResizePic pic)
                    {
                        sand = _active.Contains(pic.Graphic);
                    }
                    else if (child is CheckerTrans || child is AlphaBlendControl)
                    {
                        sand = false;
                    }
                }

                if (child.Children.Count > 0)
                {
                    Covers(child, cx, cy, x, y, ref sand);
                }
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

            if (Light && (t.FontStyle & FontStyle.BlackBorder) != 0)
            {
                return; // edged text reads on any background
            }

            if (!t.IsUnicode && t.Font == BlueFont)
            {
                // Painted-blue ASCII font (headers, tabs in the scroll windows): the theme's
                // accent color in font 1.
                t.Font = 1;
                t.Hue = AccentHue;
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

                // Server windows often color their text in the markup itself (white, yellow):
                // on sandstone those colors are darkened too.
                if (Light && !string.IsNullOrEmpty(t.Text))
                {
                    string retagged = FontColor.Replace(t.Text, DarkenTag);

                    if (retagged != t.Text)
                    {
                        t.Text = string.Empty;
                        t.Text = retagged;
                        changed = false; // just rebuilt with the new colors
                    }
                }
            }
            else
            {
                ushort hue = Readable(t.Hue, !t.IsUnicode);

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
        /// A hue for edged text (journal lines) on the themed art: on dark, near-black becomes
        /// parchment and dark colors are lightened. On light it is left alone, since its black
        /// edge already reads on sandstone as it does on the stock parchment.
        /// </summary>
        public static ushort ReadableHue(ushort hue, bool ascii = false)
        {
            return Light ? hue : Readable(hue, ascii);
        }

        // A text hue that reads on the themed background. Dark: near-black becomes parchment,
        // dark colors keep their color but lighter. Light: white and grey become ink, bright
        // colors keep their color but darker. Unchanged when no theme is on.
        static ushort Readable(ushort hue, bool ascii)
        {
            if (!Active)
            {
                return hue;
            }

            if (hue == 0xFFFF)
            {
                return Light && !ascii ? InkHue : hue; // Unicode's "no hue" is white
            }

            ushort key = (ushort)(ascii ? hue ^ 0x8000 : hue);

            if (_readable.TryGetValue(key, out ushort cached))
            {
                return cached;
            }

            // ASCII glyphs are mid-tone, so their hue shows nearer the middle of its ramp.
            uint c = Client.Game.UO.FileManager.Hues.GetPolygoneColor((ushort)(ascii ? 18 : 30), hue);
            var fixedColor = Adjust((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            ushort result = fixedColor == null ? hue
                : fixedColor.Value == Parchment ? ParchmentHue
                : fixedColor.Value == Ink ? InkHue
                : Nearest(fixedColor.Value);
            _readable[key] = result;
            return result;
        }

        // color=#RRGGBB inside HTML markup
        static readonly Regex FontColor = new Regex("(color\\s*=\\s*[\"']?#?)([0-9a-f]{6})\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static string DarkenTag(Match m)
        {
            int rgb = Convert.ToInt32(m.Groups[2].Value, 16);
            var dark = Darken((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return dark == null ? m.Value : m.Groups[1].Value + dark.Value.R.ToString("X2") + dark.Value.G.ToString("X2") + dark.Value.B.ToString("X2");
        }

        /// <summary>Same for HTML colors (0xBBGGRRAA).</summary>
        static uint ReadableHtml(uint color)
        {
            if (color == 0xFFFFFFFF || color == 0xFFFFFFFE)
            {
                // "no color": drawn white
                return Light ? ((uint)Ink.B << 24) | ((uint)Ink.G << 16) | ((uint)Ink.R << 8) | 0xFF : color;
            }

            var fixedColor = Adjust((int)((color >> 8) & 0xFF), (int)((color >> 16) & 0xFF), (int)((color >> 24) & 0xFF));

            if (fixedColor == null)
            {
                return color;
            }

            var (r, g, b) = fixedColor.Value;
            return ((uint)b << 24) | ((uint)g << 16) | ((uint)r << 8) | 0xFF;
        }

        // null = already readable
        static (int R, int G, int B)? Adjust(int r, int g, int b) => Light ? Darken(r, g, b) : Lighten(r, g, b);

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

        static (int R, int G, int B)? Darken(int r, int g, int b)
        {
            if (Luma(r, g, b) <= 95)
            {
                return null;
            }

            int max = Math.Max(r, Math.Max(g, b));

            if (max - Math.Min(r, Math.Min(g, b)) < 40)
            {
                return Ink; // white or grey
            }

            // Keep the color, pull it down until it is as dark as a mid brown.
            float k = 80f / Luma(r, g, b);
            return ((int)(r * k), (int)(g * k), (int)(b * k));
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
        static ushort InkHue => _inkHue != 0 ? _inkHue : _inkHue = Nearest(Ink);
        static ushort BronzeHue => _bronzeHue != 0 ? _bronzeHue : _bronzeHue = Nearest(Bronze);
        static ushort FadedHue => _fadedHue != 0 ? _fadedHue : _fadedHue = Nearest(Faded);


        // ---------------------------------------------------------------- titles

        /// <summary>
        /// A window title in the shard style: ASCII font 4 in the theme's title lettering
        /// (gold on dark, dark bronze on light). That font ignores hues, so
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
                    px[i] = IexDarkArt.TitleColor(v);
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
        // themed background, is redrawn as a title.
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
