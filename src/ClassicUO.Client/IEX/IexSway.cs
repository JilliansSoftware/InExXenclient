// Trees and grass moving in the breeze (Options > Video, "Trees and grass sway in the wind").
// A plant is drawn in bands, each pushed sideways by how high it sits above the ground: the
// base stays put and the top moves most. A tree's trunk and its canopy are separate pieces
// standing on the same tile, so they share the same bend and stay joined. Each tile's timing
// comes from where it is, so gusts roll across a forest; storms blow harder.
// Hooks: Game/GameObjects/Views/StaticView.cs (Draw), Game/Map/ChunkMesh.cs (plants are left out
// of the cached chunk meshes so they can move). The art itself is untouched.

using System;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal static class IexSway
    {
        public const int None = 0, Tree = 1, Low = 2;

        static readonly sbyte[] _kinds = new sbyte[0x10000]; // 0 unknown, else kind + 1

        public static bool On => ProfileManager.CurrentProfile?.IexSway ?? false;

        /// <summary>What kind of plant this is (None for anything that doesn't move).</summary>
        public static int Plant(ushort graphic, ref StaticTiles data)
        {
            sbyte k = _kinds[graphic];

            if (k != 0)
            {
                return k - 1;
            }

            int kind = None;
            string name = data.Name?.ToLowerInvariant() ?? "";

            if (StaticFilters.IsRock(graphic) || Rigid(name))
            {
                kind = None;
            }
            else if (StaticFilters.IsTree(graphic, out _) || data.IsFoliage)
            {
                kind = Tree;
            }
            else if (StaticFilters.IsVegetation(graphic))
            {
                kind = Low;
            }
            else if ((name.Contains("tree") || name.Contains("palm")) && data.Height >= 10)
            {
                kind = Tree;
            }
            else if (name.Contains("pampas grass") || name.Contains("reeds") || name.Contains("rushes"))
            {
                kind = Low;
            }

            _kinds[graphic] = (sbyte)(kind + 1);
            return kind;
        }

        // Things on the stock plant lists (or flagged foliage) that are dead or stiff: fallen logs,
        // stumps, mushrooms, cactus, floating lily pads, mud, and ships' masts and sails.
        static bool Rigid(string name) =>
            name.Contains("log") || name.Contains("stump") || name.Contains("mushroom") || name.Contains("cactus")
            || name.Contains("lily") || name.Contains("lilly") || name.Contains("muck")
            || name.Contains("mast") || name.Contains("sail") || name.Contains("spar");

        /// <summary>The kind to draw bent right now (None when the option is off).</summary>
        public static int Kind(ushort graphic, ref StaticTiles data) => On ? Plant(graphic, ref data) : None;

        // How hard the wind blows: storms (IexWeather.Dim up to 0.3) more than double it.
        static float Wind => 1f + (IexWeather.On ? IexWeather.Dim * 5f : 0f);

        /// <summary>Draws a plant bent by the wind (in place of View.DrawStaticAnimated).</summary>
        public static void Draw(UltimaBatcher2D batcher, ushort graphic, int kind, int x, int y, int tileX, int tileY, Vector3 hue, bool shadow, float depth)
        {
            ref UOFileIndex index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
            graphic = (ushort)(graphic + index.AnimOffset);

            ref readonly var art = ref Client.Game.UO.Arts.GetArt(graphic);

            if (art.Texture == null)
            {
                return;
            }

            index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
            index.Width = (short)((art.UV.Width >> 1) - 22);
            index.Height = (short)(art.UV.Height - 44);

            var pos = new Vector2(x - index.Width, y - index.Height);

            if (shadow)
            {
                batcher.DrawShadow(art.Texture, pos, art.UV, false, depth + 0.25f);
            }

            float t = Time.Ticks / 1000f;
            float phase = tileX * 0.37f + tileY * 0.23f; // gusts travel across the map
            int height = art.UV.Height;
            int bands = kind == Tree ? Math.Clamp(height / 24, 1, 8) : 3;
            Span<float> shifts = stackalloc float[bands + 1];

            if (kind == Tree)
            {
                // A slow lean with a little flutter, bending more the higher it is (quadratic).
                float gust = MathF.Sin(t * 0.8f + phase) * 0.65f + MathF.Sin(t * 2.1f + phase * 1.7f) * 0.2f + 0.15f;
                float amount = gust * Wind * 3f / (120f * 120f); // about 3 px at 120 px above the ground

                for (int i = 0; i <= bands; i++)
                {
                    float above = Math.Clamp(height - 22 - height * i / (float)bands, 0f, 220f);
                    shifts[i] = amount * above * above;
                }
            }
            else
            {
                // Grass and flowers flutter quicker. Their roots sit low in the art, so the bend is
                // measured from near its bottom: the tip moves about 3 px whatever the plant's size.
                float gust = MathF.Sin(t * 1.5f + phase) * 0.7f + MathF.Sin(t * 3.7f + phase * 2.3f) * 0.3f;
                float root = Math.Min(10f, height * 0.25f);
                float span = Math.Max(height - root, 1f);

                for (int i = 0; i <= bands; i++)
                {
                    float above = Math.Max(height - root - height * i / (float)bands, 0f);
                    float f = above / span;
                    shifts[i] = gust * Wind * 3f * f * f; // curved: stiff at the root, loose at the tip
                }
            }

            batcher.DrawIexBent(art.Texture, pos, art.UV, hue, depth + 0.5f, shifts);
        }
    }
}
