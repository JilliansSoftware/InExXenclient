// The painted parts of the compact health bars (IexHealthBar.cs), made in the client:
//   PaintArt  - the liquid in a bar: mottled and brush-grained like the frame's painted stone,
//               lit at the top and darker at the foot; it tiles sideways so it can drift.
//   SocketArt - a well carved into the stone: rounded ends, a thin gold rim (dim where the
//               stone's lip shades it, bright below where it catches the light), a dark hollow,
//               and the shadow the lip casts over whatever fills it.
//   RibbonArt - the dark leather banner with swallowtail ends and gold edges behind the name.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.IEX
{
    internal sealed partial class IexHealthBar
    {
        static readonly Color GoldHi = new Color(232, 192, 96), Gold = new Color(196, 148, 52), GoldLo = new Color(112, 76, 22);

        static uint Premul(float r, float g, float b, float a)
        {
            a = Math.Clamp(a, 0f, 1f);
            return new Color((int)Math.Clamp(r * a, 0, 255), (int)Math.Clamp(g * a, 0, 255), (int)Math.Clamp(b * a, 0, 255), (int)(a * 255)).PackedValue;
        }

        // Smooth value noise that wraps every `period` cells sideways (so the paint tiles).
        sealed class Noise
        {
            readonly float[] _v;
            readonly int _w, _h;

            public Noise(int seed, int w, int h)
            {
                var r = new Random(seed);
                _w = w;
                _h = h;
                _v = new float[w * h];

                for (int i = 0; i < _v.Length; i++)
                {
                    _v[i] = (float)r.NextDouble();
                }
            }

            static float S(float t) => t * t * (3 - 2 * t);

            public float At(float x, float y)
            {
                int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
                float fx = S(x - x0), fy = S(y - y0);
                int xa = ((x0 % _w) + _w) % _w, xb = (xa + 1) % _w;
                int ya = Math.Clamp(y0, 0, _h - 1), yb = Math.Clamp(y0 + 1, 0, _h - 1);
                float a = _v[ya * _w + xa] + (_v[ya * _w + xb] - _v[ya * _w + xa]) * fx;
                float b = _v[yb * _w + xa] + (_v[yb * _w + xb] - _v[yb * _w + xa]) * fx;
                return a + (b - a) * fy;
            }
        }

        static class PaintArt
        {
            public const int W = 256, H = 16;

            static readonly Dictionary<uint, Texture2D> _paints = new Dictionary<uint, Texture2D>();

            // Top to foot: a lit lip, the colour, then darker toward the bottom.
            static readonly float[] Shade = { 1.5f, 1.28f, 1.12f, 1.04f, 1f, 1f, 0.98f, 0.95f, 0.92f, 0.88f, 0.84f, 0.79f, 0.73f, 0.66f, 0.58f, 0.48f };

            public static Texture2D For(Color c)
            {
                if (_paints.TryGetValue(c.PackedValue, out Texture2D tex) && !tex.IsDisposed)
                {
                    return tex;
                }

                var coarse = new Noise((int)c.PackedValue, 16, 4);       // big blotches: 16 px across
                var fine = new Noise((int)c.PackedValue ^ 0x5A5A, 64, 9); // small mottling
                var grain = new Noise((int)c.PackedValue ^ 0x1234, 32, 17); // long brush strokes
                var px = new uint[W * H];

                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        float n = coarse.At(x / 16f, y / 5f) * 0.55f + fine.At(x / 4f, y / 2f) * 0.3f + grain.At(x / 8f, y) * 0.15f;
                        float k = Shade[y] * (0.78f + 0.42f * n);
                        px[y * W + x] = Premul(c.R * k, c.G * k, c.B * k, 1f);
                    }
                }

                tex = new Texture2D(Client.Game.GraphicsDevice, W, H);
                tex.SetData(px);
                _paints[c.PackedValue] = tex;
                return tex;
            }
        }

        static class SocketArt
        {
            public static float Radius(int h) => Math.Min(5f, h / 2f);

            // Signed distance from a pixel centre to a rounded rectangle (negative inside).
            static float Dist(float px, float py, float x0, float y0, float x1, float y1, float r)
            {
                float cx = Math.Clamp(px, x0 + r, x1 - r), cy = Math.Clamp(py, y0 + r, y1 - r);
                float dx = px - cx, dy = py - cy;
                float outside = MathF.Sqrt(dx * dx + dy * dy) - r;
                return outside;
            }

            /// <summary>How far the hollow's row y starts in from the socket's left (and right) edge.</summary>
            public static int Inset(int y, int h)
            {
                float r = Radius(h) - 1, yc = y + 0.5f, top = 1 + r, bottom = h - 1 - r;
                float dy = yc < top ? top - yc : yc > bottom ? yc - bottom : 0;

                if (r <= 0 || dy <= 0)
                {
                    return 1;
                }

                return 1 + (int)MathF.Round(r - MathF.Sqrt(Math.Max(0, r * r - dy * dy)));
            }

            /// <summary>The socket behind the fill: rim and dark hollow.</summary>
            public static Texture2D Back(int w, int h) => Build(w, h, false);

            /// <summary>Over the fill: the rim again and the shadow of the stone's lip.</summary>
            public static Texture2D Front(int w, int h) => Build(w, h, true);

            static Texture2D Build(int w, int h, bool front)
            {
                if (w <= 2 || h <= 2)
                {
                    return null;
                }

                float r = Radius(h);
                var px = new uint[w * h];

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float d = Dist(x + 0.5f, y + 0.5f, 0, 0, w, h, r);
                        float outer = Math.Clamp(0.5f - d, 0, 1);    // inside the socket at all
                        float inner = Math.Clamp(-0.5f - d, 0, 1);   // inside the hollow (1 px in)
                        float rim = outer - inner;

                        // rim: shaded under the lip at the top, catching the light along the bottom
                        float lit = (float)y / Math.Max(1, h - 1);
                        Color g = lit < 0.5f ? Color.Lerp(GoldLo, Gold, lit * 2) : Color.Lerp(Gold, GoldHi, (lit - 0.5f) * 2);

                        float rr = g.R * rim, gg = g.G * rim, bb = g.B * rim, a = rim;

                        if (front)
                        {
                            // the lip's shadow over the liquid: heavy along the top, light down the ends
                            float s = y switch { 1 => 0.6f, 2 => 0.35f, 3 => 0.15f, _ => 0f };
                            float ends = Math.Max(0, 1 - Math.Min(x, w - 1 - x) / 3f) * 0.3f;
                            float shade = Math.Min(0.75f, s + ends) * inner;
                            a += shade;
                        }
                        else
                        {
                            // the dark hollow, darkest up under the lip
                            float k = 0.6f + 0.4f * lit;
                            rr += 16 * k * inner;
                            gg += 13 * k * inner;
                            bb += 12 * k * inner;
                            a += inner;
                        }

                        px[y * w + x] = new Color((int)Math.Min(255, rr), (int)Math.Min(255, gg), (int)Math.Min(255, bb), (int)(Math.Min(1f, a) * 255)).PackedValue;
                    }
                }

                var tex = new Texture2D(Client.Game.GraphicsDevice, w, h);
                tex.SetData(px);
                return tex;
            }
        }

        static class RibbonArt
        {
            static readonly Color Leather = new Color(70, 26, 20);

            /// <summary>How far each swallowtail end reaches in from the tip.</summary>
            public static int Tail(int h) => Math.Max(4, h * 2 / 3);

            public static Texture2D Build(int w, int h)
            {
                if (w <= 8 || h <= 4)
                {
                    return null;
                }

                int tail = Tail(h);
                var mottle = new Noise(77, 32, 6);
                var px = new uint[w * h];
                float mid = (h - 1) / 2f;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int fromEnd = Math.Min(x, w - 1 - x);
                        bool isTail = fromEnd < tail;

                        // the notch: a triangle cut into each end, deepest at the middle row
                        float across = Math.Abs(y - mid) / Math.Max(1, mid); // 0 middle, 1 edges
                        float notch = tail * 0.55f * (1 - across);

                        if (isTail && fromEnd < notch)
                        {
                            continue;
                        }

                        // the tails sit a little behind the body (darker), with a crease where they fold
                        float k = isTail ? 0.62f : 1f;

                        if (fromEnd == tail || fromEnd == tail - 1)
                        {
                            k *= 0.55f;
                        }

                        k *= 0.72f + 0.45f * (1 - across * across); // rounded across, lit through the middle
                        k *= 0.85f + 0.3f * mottle.At(x / 6f, y / 3f);

                        Color c = new Color((int)Math.Min(255, Leather.R * k), (int)Math.Min(255, Leather.G * k), (int)Math.Min(255, Leather.B * k));

                        // gold stitched edges along the top and bottom of the body
                        if (!isTail && (y == 1 || y == h - 2))
                        {
                            c = y == 1 ? GoldLo : Gold;
                        }
                        else if (y == 0 || y == h - 1)
                        {
                            c = new Color(18, 8, 6); // a dark outline
                        }

                        px[y * w + x] = c.PackedValue;
                    }
                }

                var tex = new Texture2D(Client.Game.GraphicsDevice, w, h);
                tex.SetData(px);
                return tex;
            }
        }
    }
}
