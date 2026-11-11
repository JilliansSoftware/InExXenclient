// Water glints (Options > Video, "Enhanced weather and water"): small highlights that come and
// go on open water. A few pale pixels per tile, drawn at the water's own depth so ships, shores
// and anything else standing in front still cover them. The water art itself is untouched.
// Hook: Game/Scenes/GameScene.cs (DrawWorld).

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Map;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal static class IexWater
    {
        private static readonly Color _glintColor = new Color(214, 232, 255);

        private const uint PERIOD = 2600; // one glint's rise and fall, in ms
        private const int ONE_IN = 6;     // share of water tiles glinting at a time

        public static void Draw(UltimaBatcher2D batcher, List<Chunk> visibleChunks, int offsetX, int offsetY)
        {
            if (!IexWeather.On)
            {
                return;
            }

            uint now = Time.Ticks;
            var texture = SolidColorTextureCache.GetTexture(_glintColor);
            Vector3 hue = Vector3.Zero;

            foreach (Chunk chunk in visibleChunks)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    for (int cy = 0; cy < 8; cy++)
                    {
                        GameObject first = chunk.GetHeadObject(cx, cy);

                        while (first != null && first is not Land)
                        {
                            first = first.TNext;
                        }

                        if (first is not Land land || land.IsDestroyed || !land.TileData.IsWet)
                        {
                            continue;
                        }

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
                        batcher.Draw(texture, new Rectangle(px, py, width, 1), hue, land.CalculateDepthZ() + 0.6f);
                    }
                }
            }
        }

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
