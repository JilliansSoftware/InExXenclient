// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClassicUO.Renderer
{
    public struct GlyphAtlasEntry
    {
        public Texture2D Texture;
        public Rectangle UV;
        public int BearingX;
        public int BearingY;
        public int AdvanceWidth;
        public int GlyphWidth;
        public int GlyphHeight;
        public int Pad; // IEX: sized glyphs carry a margin for their edge (see GetSizedEntry)

        public bool IsValid => Texture != null;

        public static readonly GlyphAtlasEntry Empty = new GlyphAtlasEntry();
    }

    public sealed class FontGlyphAtlas : IDisposable
    {
        private readonly Dictionary<long, GlyphAtlasEntry> _cache = new Dictionary<long, GlyphAtlasEntry>();
        private readonly Dictionary<(long, uint), GlyphAtlasEntry> _coloredCache = new Dictionary<(long, uint), GlyphAtlasEntry>();
        private readonly Dictionary<(long, uint, int), GlyphAtlasEntry> _sizedCache = new Dictionary<(long, uint, int), GlyphAtlasEntry>(); // IEX
        private readonly TextureAtlas _atlas;
        private readonly FontsLoader _fontsLoader;

        public FontGlyphAtlas(FontsLoader fontsLoader, GraphicsDevice device)
        {
            _fontsLoader = fontsLoader;
            _atlas = new TextureAtlas(device, 2048, 2048, SurfaceFormat.Color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long MakeKey(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic)
        {
            // Pack into long: font(8) | char(16) | unicode(1) | border(1) | solid(1) | italic(1)
            return ((long)font << 20)
                 | ((long)(ushort)character << 4)
                 | (isUnicode ? 8L : 0L)
                 | (hasBorder ? 4L : 0L)
                 | (isSolid ? 2L : 0L)
                 | (isItalic ? 1L : 0L);
        }

        /// <summary>
        /// Get a glyph entry without baked color (color = 0, rendered as white for Unicode).
        /// Used for non-HTML text where hue is applied via shader at draw time.
        /// </summary>
        public ref readonly GlyphAtlasEntry GetEntry(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic)
        {
            long key = MakeKey(font, character, isUnicode, hasBorder, isSolid, isItalic);

            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_cache, key, out bool exists);

            if (!exists)
            {
                entry = CreateEntry(font, character, isUnicode, hasBorder, isSolid, isItalic, 0);
            }

            return ref entry;
        }

        /// <summary>
        /// Get a glyph entry with a specific baked ARGB color.
        /// Used for HTML text where each character can have a different color.
        /// </summary>
        public GlyphAtlasEntry GetColoredEntry(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic, uint color)
        {
            if (color == 0)
            {
                return GetEntry(font, character, isUnicode, hasBorder, isSolid, isItalic);
            }

            long baseKey = MakeKey(font, character, isUnicode, hasBorder, isSolid, isItalic);
            var key = (baseKey, color);

            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_coloredCache, key, out bool exists);

            if (!exists)
            {
                entry = CreateEntry(font, character, isUnicode, hasBorder, isSolid, isItalic, color);
            }

            return entry;
        }

        private GlyphAtlasEntry CreateEntry(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic, uint colorOrHue)
        {
            FontsLoader.SingleGlyphInfo glyphInfo;

            if (isUnicode)
            {
                glyphInfo = _fontsLoader.RenderSingleGlyphUnicode(font, character, hasBorder, isSolid, isItalic, colorOrHue);
            }
            else
            {
                // For ASCII, colorOrHue is the hue index (ushort)
                glyphInfo = _fontsLoader.RenderSingleGlyphASCII(font, character, (ushort)colorOrHue);
            }

            if (glyphInfo.Data == null || glyphInfo.Data.Length == 0 || glyphInfo.Width <= 0 || glyphInfo.Height <= 0)
            {
                return new GlyphAtlasEntry
                {
                    Texture = null,
                    BearingX = glyphInfo.BearingX,
                    BearingY = glyphInfo.BearingY,
                    AdvanceWidth = glyphInfo.AdvanceWidth,
                    GlyphWidth = 0,
                    GlyphHeight = 0
                };
            }

            var texture = _atlas.AddSprite(
                glyphInfo.Data,
                glyphInfo.Width,
                glyphInfo.Height,
                out var uv
            );

            return new GlyphAtlasEntry
            {
                Texture = texture,
                UV = uv,
                BearingX = glyphInfo.BearingX,
                BearingY = glyphInfo.BearingY,
                AdvanceWidth = glyphInfo.AdvanceWidth,
                GlyphWidth = glyphInfo.Width,
                GlyphHeight = glyphInfo.Height
            };
        }

        // ── IEX: glyphs built at a text size (125%, 150%...) instead of stretched ──
        //
        // Stretching a pixel font by 1.25 or 1.5 gives strokes of uneven thickness. Here the
        // glyph (without its black edge) is resampled to the exact size by area averaging,
        // which keeps strokes even and softens the steps, and the 1px black edge is drawn
        // afterwards so it stays 1px. A glyph rarely lands on a whole pixel at these sizes,
        // so it is built for its sub-pixel phase: quarter pixels across (all that 25% steps
        // can produce) and its own fixed phase down, so baselines line up.

        /// <summary>
        /// A glyph at <paramref name="percent"/> size. BearingX/BearingY/AdvanceWidth stay in
        /// unscaled pixels (BearingX/Y are those of the glyph without edge); GlyphWidth and
        /// GlyphHeight are the sized bitmap, drawn 1:1 with its top-left Pad pixels before the
        /// glyph. <paramref name="phaseX"/> is the quarter pixel (0..3) of the glyph's left side.
        /// </summary>
        public GlyphAtlasEntry GetSizedEntry(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic, uint color, int percent, int phaseX)
        {
            long baseKey = MakeKey(font, character, isUnicode, hasBorder, isSolid, isItalic);
            var key = (baseKey, color, (percent << 2) | (phaseX & 3));

            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_sizedCache, key, out bool exists);

            if (!exists)
            {
                entry = CreateSizedEntry(font, character, isUnicode, hasBorder, isSolid, isItalic, color, percent / 100f, (phaseX & 3) / 4f);
            }

            return entry;
        }

        private GlyphAtlasEntry CreateSizedEntry(byte font, char character, bool isUnicode, bool hasBorder, bool isSolid, bool isItalic, uint colorOrHue, float s, float fx)
        {
            // The edge is added after sizing, so start from the glyph without it.
            FontsLoader.SingleGlyphInfo g = isUnicode
                ? _fontsLoader.RenderSingleGlyphUnicode(font, character, false, isSolid, isItalic, colorOrHue)
                : _fontsLoader.RenderSingleGlyphASCII(font, character, (ushort)colorOrHue);

            var result = new GlyphAtlasEntry
            {
                BearingX = g.BearingX,
                BearingY = g.BearingY,
                AdvanceWidth = g.AdvanceWidth
            };

            if (g.Data == null || g.Data.Length == 0 || g.Width <= 0 || g.Height <= 0)
            {
                return result;
            }

            uint[] px = SizeGlyph(g, hasBorder && isUnicode, s, fx, out int bw, out int bh, out int pad);

            result.Texture = _atlas.AddSprite(px, bw, bh, out var uv);
            result.UV = uv;
            result.GlyphWidth = bw;
            result.GlyphHeight = bh;
            result.Pad = pad;

            return result;
        }

        /// <summary>The sized pixels of a glyph (premultiplied), with a 1px black edge if asked.</summary>
        public static uint[] SizeGlyph(FontsLoader.SingleGlyphInfo g, bool edge, float s, float fx, out int bw, out int bh, out int pad)
        {
            pad = edge ? 1 : 0;
            float top = g.BearingY * s;
            float fy = top - MathF.Floor(top);
            int w = g.Width, h = g.Height;
            int tw = (int)MathF.Ceiling(fx + w * s), th = (int)MathF.Ceiling(fy + h * s);
            bw = tw + pad * 2;
            bh = th + pad * 2;

            var alpha = new float[bw * bh];
            var cr = new float[bw * bh];
            var cg = new float[bw * bh];
            var cb = new float[bw * bh];
            float area = 1f / (s * s);

            for (int ty = 0; ty < th; ty++)
            {
                float y0 = MathF.Max(0, (ty - fy) / s), y1 = MathF.Min(h, (ty + 1 - fy) / s);

                for (int tx = 0; tx < tw; tx++)
                {
                    float x0 = MathF.Max(0, (tx - fx) / s), x1 = MathF.Min(w, (tx + 1 - fx) / s);
                    float a = 0, r = 0, gg = 0, b = 0;

                    for (int sy = (int)y0; sy < y1; sy++)
                    {
                        float wy = MathF.Min(sy + 1, y1) - MathF.Max(sy, y0);

                        for (int sx = (int)x0; sx < x1; sx++)
                        {
                            uint c = g.Data[sy * w + sx];
                            float wa = (MathF.Min(sx + 1, x1) - MathF.Max(sx, x0)) * wy * ((c >> 24) / 255f);

                            if (wa <= 0)
                            {
                                continue;
                            }

                            a += wa;
                            r += wa * (c & 0xFF);
                            gg += wa * ((c >> 8) & 0xFF);
                            b += wa * ((c >> 16) & 0xFF);
                        }
                    }

                    if (a > 0)
                    {
                        int i = (ty + pad) * bw + tx + pad;
                        cr[i] = r / a;
                        cg[i] = gg / a;
                        cb[i] = b / a;
                        alpha[i] = MathF.Min(1f, a / area * 1.25f); // keeps thin strokes solid
                    }
                }
            }

            var px = new uint[bw * bh];

            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++)
                {
                    int i = y * bw + x;
                    float a = alpha[i];
                    bool solid = false;

                    if (edge)
                    {
                        for (int ny = Math.Max(0, y - 1); ny <= Math.Min(bh - 1, y + 1) && !solid; ny++)
                        {
                            for (int nx = Math.Max(0, x - 1); nx <= Math.Min(bw - 1, x + 1); nx++)
                            {
                                if (alpha[ny * bw + nx] >= 0.24f)
                                {
                                    solid = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (a <= 0 && !solid)
                    {
                        continue;
                    }

                    // Premultiplied; over the black edge where there is one.
                    uint r = Math.Max(1u, (uint)(cr[i] * a)), gg = Math.Max(1u, (uint)(cg[i] * a)), b = Math.Max(1u, (uint)(cb[i] * a));
                    uint al = solid ? 255u : (uint)(a * 255f);
                    px[i] = (al << 24) | (b << 16) | (gg << 8) | r;
                }
            }

            return px;
        }

        public void Dispose()
        {
            _atlas?.Dispose();
            _cache.Clear();
            _coloredCache.Clear();
            _sizedCache.Clear();
        }
    }
}
