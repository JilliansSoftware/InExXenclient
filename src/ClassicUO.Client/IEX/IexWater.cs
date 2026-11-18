// Moving water (Options > Video, "Enhanced weather and water"), drawn over the water tiles at
// the water's own depth so ships, shores and anything standing in front still cover it. The
// water art itself is untouched.
//  - Swells: broad, faint light and dark bands rolling slowly across all water. Each tile is a
//    diamond with its own strength at each corner; neighbours share corners, so no seams.
//  - Crests: small pale arcs that drift with the wind and fade, only on open water (no land
//    within two tiles), so rivers and ponds stay calm.
//  - Glints: a few pale pixels that come and go.
// Storms (IexWeather.Dim) make the swells stronger and quicker and the crests more frequent.
// Hook: Game/Scenes/GameScene.cs (DrawWorld).

using System;
using System.Collections.Generic;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Map;
using ClassicUO.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.IEX
{
    internal static class IexWater
    {
        private static readonly Color _glintColor = new Color(214, 232, 255);
        private static readonly Color _swellLight = new Color(205, 228, 245);
        private static readonly Color _crestColor = new Color(225, 238, 250);

        private const uint PERIOD = 2600; // one glint's rise and fall, in ms
        private const int ONE_IN = 6;     // share of water tiles glinting at a time

        private const float SWELL = 0.09f;      // strongest swell band's opacity in calm weather
        private const uint CREST_PERIOD = 4200; // one crest's life, in ms
        private const uint CREST_ONE_IN = 7;    // share of open-water tiles with a crest at a time

        // Wind across the map, in tiles (+X is screen down-right, +Y down-left): mostly toward
        // the bottom of the screen, a little to the right.
        private const float WIND_X = 0.8f, WIND_Y = 0.6f;

        public static void Draw(UltimaBatcher2D batcher, World world, List<Chunk> visibleChunks, int offsetX, int offsetY)
        {
            if (!IexWeather.On || world?.Map == null)
            {
                return;
            }

            uint now = Time.Ticks;
            var texture = SolidColorTextureCache.GetTexture(_glintColor);
            var light = SolidColorTextureCache.GetTexture(_swellLight);
            var dark = SolidColorTextureCache.GetTexture(Color.Black);
            var crest = SolidColorTextureCache.GetTexture(_crestColor);
            Vector3 hue = Vector3.Zero;

            float storm = Math.Clamp(IexWeather.Dim / 0.3f, 0f, 1f);
            float t = now / 1000f * (1f + storm * 0.8f);
            float swell = SWELL * (1f + storm * 1.2f);

            foreach (Chunk chunk in visibleChunks)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    for (int cy = 0; cy < 8; cy++)
                    {
                        GameObject first = chunk.GetHeadObject(cx, cy);
                        Land land = null;

                        for (GameObject o = first; o != null; o = o.TNext)
                        {
                            if (o is Land l)
                            {
                                land = l;
                            }
                            else if (o is Static s && IsWaterStatic(s.Graphic) && !IsOutline(s.Graphic) && !s.IsDestroyed)
                            {
                                DrawShallows(batcher, world, s, swell, t, storm, offsetX, offsetY); // the shore's water pieces
                            }
                        }

                        if (land == null || land.IsDestroyed || !land.TileData.IsWet)
                        {
                            continue;
                        }

                        float depth = land.CalculateDepthZ();
                        DrawSwell(batcher, land, light, dark, swell, t, depth + 0.55f, offsetX, offsetY);
                        DrawCrest(batcher, world, land, crest, now, storm, depth + 0.6f, offsetX, offsetY);

                        uint h = Hash((uint)land.X, (uint)land.Y);

                        // every tile runs its own clock; in each turn of it, one tile in six glints
                        uint clock = now + h % PERIOD;
                        uint turn = clock / PERIOD;

                        if (Hash(h, turn) % ONE_IN != 0)
                        {
                            continue;
                        }

                        float phase = clock % PERIOD / (float)PERIOD;
                        float strength = MathF.Sin(phase * MathF.PI);
                        strength *= strength;

                        if (strength < 0.05f)
                        {
                            continue;
                        }

                        uint r = Hash(h ^ 0x9E3779B9u, turn);

                        // somewhere inside the tile's diamond (44 wide, 44 tall, centre at +22,+22)
                        int px = (land.X - land.Y) * 22 - offsetX + (int)(r % 21) - 10;
                        int py = (land.X + land.Y) * 22 - (land.Z << 2) - offsetY + (int)((r >> 8) % 11) - 5;
                        int width = 2 + (int)((r >> 16) % 3);

                        hue.Z = 0.42f * strength;
                        batcher.Draw(texture, new Rectangle(px, py, width, 1), hue, depth + 0.6f);
                    }
                }
            }
        }

        // The swell at a point on the map's grid: -1 (trough, darker) .. +1 (crest, lighter).
        private static float Swell(float u, float v, float t)
        {
            float a = MathF.Sin((u * WIND_X + v * WIND_Y) * 0.55f - t * 0.6f);
            float b = MathF.Sin((u * -0.35f + v * 0.95f) * 0.38f - t * 0.37f + 1.3f);
            return a * 0.65f + b * 0.35f;
        }

        private static void DrawSwell(UltimaBatcher2D batcher, Land land, Texture2D light, Texture2D dark, float strength, float t, float depth, int offsetX, int offsetY)
        {
            int x = land.X, y = land.Y;
            float cx = (x - y) * 22 - offsetX, cy = (x + y) * 22 - (land.Z << 2) - offsetY;

            // the diamond's corners; on the map's grid they are (x,y), (x+1,y), (x,y+1), (x+1,y+1)
            var top = new Vector2(cx, cy - 22);
            var right = new Vector2(cx + 22, cy);
            var left = new Vector2(cx - 22, cy);
            var bottom = new Vector2(cx, cy + 22);

            float sTop = Swell(x, y, t), sRight = Swell(x + 1, y, t), sLeft = Swell(x, y + 1, t), sBottom = Swell(x + 1, y + 1, t);

            if (sTop > 0 || sRight > 0 || sLeft > 0 || sBottom > 0)
            {
                var a = new Vector4(Peak(sTop), Peak(sRight), Peak(sLeft), Peak(sBottom)) * strength;
                batcher.DrawIexShadedQuad(light, top, right, left, bottom, a, Vector3.Zero, depth);
            }

            if (sTop < 0 || sRight < 0 || sLeft < 0 || sBottom < 0)
            {
                var a = new Vector4(Peak(-sTop), Peak(-sRight), Peak(-sLeft), Peak(-sBottom)) * strength;
                batcher.DrawIexShadedQuad(dark, top, right, left, bottom, a, Vector3.Zero, depth);
            }
        }

        // narrows the bands toward their peaks
        private static float Peak(float s) => s > 0 ? s * s : 0f;

        private static void DrawCrest(UltimaBatcher2D batcher, World world, Land land, Texture2D texture, uint now, float storm, float depth, int offsetX, int offsetY)
        {
            uint h = Hash((uint)land.X * 7919u, (uint)land.Y);
            uint clock = now + h % CREST_PERIOD;
            uint turn = clock / CREST_PERIOD;
            uint oneIn = storm > 0.5f ? 3u : storm > 0f ? 5u : CREST_ONE_IN;

            if (Hash(h, turn) % oneIn != 0)
            {
                return;
            }

            float life = clock % CREST_PERIOD / (float)CREST_PERIOD;
            float strength = MathF.Sin(life * MathF.PI);
            strength *= strength;

            if (strength < 0.05f || !OpenWater(world, land.X, land.Y))
            {
                return;
            }

            uint r = Hash(h ^ 0x68E31DA4u, turn);

            // start somewhere in the tile, then drift with the wind (on screen: down, a little right)
            float cx = (land.X - land.Y) * 22 - offsetX + (int)(r % 25) - 12;
            float cy = (land.X + land.Y) * 22 - (land.Z << 2) - offsetY + (int)((r >> 8) % 13) - 6;
            cx += (WIND_X - WIND_Y) * 11f * life;
            cy += (WIND_X + WIND_Y) * 11f * life;

            // a shallow arc with its ends raised, in four short strokes
            float half = 5f + (r >> 16) % 4;
            float sag = half * 0.28f;
            var hue = new Vector3(0, 0, 0.5f * strength);
            var prev = new Vector2(cx - half, cy - sag);

            for (int i = 1; i <= 4; i++)
            {
                float f = i / 2f - 1f; // -0.5 .. 1
                var p = new Vector2(cx + f * half, cy - sag * f * f);
                batcher.DrawLine(texture, prev, p, hue, 1f, depth);
                prev = p;
            }
        }

        // Water all around: no land within two tiles.
        private static bool OpenWater(World world, int x, int y)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (!IsWaterAt(world, x + dx, y + dy))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ the shallows

        // The water pieces placed along coasts (over sand), in place of the sea's water ground.
        private const ushort SHALLOW_FIRST = 0x1796, SHALLOW_LAST = 0x17B2;

        private static bool IsWaterStatic(ushort graphic) => graphic >= SHALLOW_FIRST && graphic <= SHALLOW_LAST;

        // Thin edge lines in that range, not water: left exactly as drawn (recoloured or pushed,
        // they show as bright chevrons and dark lines between the shallows and deep water).
        private static bool IsOutline(ushort graphic) => graphic == 0x1796 || graphic == 0x17AD || graphic == 0x17AE;

        // Water ground or a water piece. A tile that isn't loaded counts as water, so the edge of
        // what the client knows about doesn't look like a shore.
        private static bool IsWaterAt(World world, int x, int y)
        {
            if (x < 0 || y < 0 || world.Map.GetChunk(x, y, false) == null)
            {
                return true;
            }

            for (GameObject o = world.Map.GetTile(x, y, false); o != null; o = o.TNext)
            {
                if (o is Land l && l.TileData.IsWet || o is Static s && IsWaterStatic(s.Graphic))
                {
                    return true;
                }
            }

            return false;
        }

        // Which way the land lies from a shore tile, on screen (zero when there is no land beside
        // it). Cached; the map's water doesn't change.
        private static readonly Dictionary<long, Vector3> _shore = new Dictionary<long, Vector3>();
        private static int _shoreMap = -1;

        private static Vector3 ShoreDirection(World world, int x, int y)
        {
            if (_shoreMap != world.Map.Index || _shore.Count > 40000)
            {
                _shore.Clear();
                _shoreMap = world.Map.Index;
            }

            long key = (long)x << 20 | (uint)y;

            if (_shore.TryGetValue(key, out Vector3 found))
            {
                return found;
            }

            float gx = 0, gy = 0;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if ((dx != 0 || dy != 0) && !IsWaterAt(world, x + dx, y + dy))
                    {
                        gx += dx;
                        gy += dy;
                    }
                }
            }

            Vector3 dir = Vector3.Zero;
            var screen = new Vector2((gx - gy) * 22f, (gx + gy) * 22f);

            if (screen.LengthSquared() > 0.01f)
            {
                screen.Normalize();
                dir = new Vector3(screen, gx + gy); // Z > 0: the land is in front (drawn later)
            }

            _shore[key] = dir;
            return dir;
        }

        // A shore water piece: the swells, in its own shape so the sand around it isn't tinted, and
        // the surge: a faint copy of the piece pushed a few pixels up the beach and back, so the
        // waterline breathes. The surge rolls along the coast.
        private static void DrawShallows(UltimaBatcher2D batcher, World world, Static s, float strength, float t, float storm, int offsetX, int offsetY)
        {
            ushort graphic = s.Graphic;
            ref UOFileIndex index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
            graphic = (ushort)(graphic + index.AnimOffset);

            ref readonly var art = ref Client.Game.UO.Arts.GetArt(graphic);

            if (art.Texture == null)
            {
                return;
            }

            int x = s.X, y = s.Y;
            float w = art.UV.Width, h = art.UV.Height;
            float cx = (x - y) * 22 - offsetX, cy = (x + y) * 22 - (s.Z << 2) - offsetY; // the tile's centre
            float left = cx - w / 2f, top = cy + 22 - h;
            float depth = s.CalculateDepthZ();

            // the swells, sampled at the art's corners
            float sTL = SwellAt(x, y, left - cx, top - cy, t), sTR = SwellAt(x, y, left + w - cx, top - cy, t);
            float sBL = SwellAt(x, y, left - cx, top + h - cy, t), sBR = SwellAt(x, y, left + w - cx, top + h - cy, t);
            var tl = new Vector2(left, top);
            var tr = new Vector2(left + w, top);
            var bl = new Vector2(left, top + h);
            var br = new Vector2(left + w, top + h);

            if (sTL > 0 || sTR > 0 || sBL > 0 || sBR > 0)
            {
                var a = new Vector4(Peak(sTL), Peak(sTR), Peak(sBL), Peak(sBR)) * strength;
                batcher.DrawIexShadedQuad(art.Texture, art.UV, tl, tr, bl, br, a, _lightHue, depth + 0.55f);
            }

            if (sTL < 0 || sTR < 0 || sBL < 0 || sBR < 0)
            {
                var a = new Vector4(Peak(-sTL), Peak(-sTR), Peak(-sBL), Peak(-sBR)) * strength;
                batcher.DrawIexShadedQuad(art.Texture, art.UV, tl, tr, bl, br, a, _darkHue, depth + 0.55f);
            }

            // the surge
            Vector3 dir = ShoreDirection(world, x, y);

            if (dir == Vector3.Zero)
            {
                return;
            }

            float rise = 0.5f - 0.5f * MathF.Cos(t * 0.85f + (x + y) * 0.22f + Hash((uint)x, (uint)y) % 100 / 100f); // 0..1, about 7 s
            float reach = SURGE * rise * (1f + storm * 0.6f);

            if (reach < 0.4f)
            {
                return;
            }

            // in front of the sand it washes over when the land lies forward
            float surgeDepth = depth + (dir.Z > 0 ? 1f : 0f) + 0.05f;
            var pos = new Vector2(left + dir.X * reach, top + dir.Y * reach);

            batcher.Draw(art.Texture, pos, art.UV, new Vector3(0, 0, 0.6f - 0.25f * rise), 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, surgeDepth);
        }

        private const float SURGE = 6f; // how far the water reaches up the beach, in pixels

        // hue vectors that recolour an art's shape: white (hue 1153) and black (hue 1), text-hue mode
        private static readonly Vector3 _lightHue = new Vector3(1152, ShaderHueTranslator.SHADER_TEXT_HUE, 0);
        private static readonly Vector3 _darkHue = new Vector3(0, ShaderHueTranslator.SHADER_TEXT_HUE, 0);

        // The swell at a point dx, dy pixels from a tile's centre.
        private static float SwellAt(int x, int y, float dx, float dy, float t) =>
            Swell(x + 0.5f + (dx + dy) / 44f, y + 0.5f + (dy - dx) / 44f, t);

        private static uint Hash(uint a, uint b)
        {
            uint h = a * 0x85EBCA6Bu ^ b * 0xC2B2AE35u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 13;

            return h;
        }
    }
}
