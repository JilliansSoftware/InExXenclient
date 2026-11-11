// Enhanced lighting (Options > Video, "Enhanced lighting"): tinted darkness, warm fire light
// with a slight flicker, softened light edges and glow. The art itself is never filtered:
// only the light laid over it changes. The shader half lives in
// ClassicUO.Renderer/IEX/IexLightingPass.cs; the hooks are in Game/Scenes/GameScene.cs.

using System;
using ClassicUO.Configuration;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal static class IexLighting
    {
        /// <summary>Row of the light colour table used for fire light (unused by the stock client).</summary>
        public const ushort WarmShader = 59;

        /// <summary>Colour of fire light: torches, candles, braziers, forges, lanterns.</summary>
        public const uint WarmRgb = 0xFF_CC_8C;

        public static bool On => ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.IexEnhancedLighting;

        // Darkness colour by the world's light level (0 = day ... 30 = black), before brightness:
        // a warm cast while the sun is low, deep blue at night, a colder blue-grey underground.
        // ModernUO sends 0 by day, 12 at night and 26 in dungeons, and steps between at dusk and dawn.
        private static readonly (int Level, Vector3 Tint)[] _tints =
        {
            (0, new Vector3(1.00f, 1.00f, 1.00f)),
            (4, new Vector3(1.00f, 0.90f, 0.76f)),
            (9, new Vector3(0.82f, 0.82f, 0.95f)),
            (13, new Vector3(0.66f, 0.78f, 1.00f)),
            (20, new Vector3(0.66f, 0.78f, 1.00f)),
            (26, new Vector3(0.74f, 0.84f, 1.00f)),
        };

        /// <summary>
        /// The colour the light map starts from. <paramref name="brightness"/> is the stock grey
        /// (0..1); the tint keeps that brightness, so nights are no darker or lighter than before.
        /// </summary>
        public static Vector3 Ambient(float brightness, int overall)
        {
            Vector3 tint = Tint(overall);
            float luma = tint.X * 0.2126f + tint.Y * 0.7152f + tint.Z * 0.0722f;
            tint *= brightness / luma;

            return Vector3.Clamp(tint, Vector3.Zero, Vector3.One);
        }

        private static Vector3 Tint(int overall)
        {
            if (overall <= _tints[0].Level)
            {
                return _tints[0].Tint;
            }

            for (int i = 1; i < _tints.Length; i++)
            {
                if (overall <= _tints[i].Level)
                {
                    float t = (overall - _tints[i - 1].Level) / (float)(_tints[i].Level - _tints[i - 1].Level);

                    return Vector3.Lerp(_tints[i - 1].Tint, _tints[i].Tint, t);
                }
            }

            return _tints[_tints.Length - 1].Tint;
        }

        /// <summary>True for the light colours that are fire: our warm row and the stock oranges.</summary>
        public static bool IsFire(ushort color) => color == WarmShader || color == 30 || color == 31;

        /// <summary>A seed that stays with one light: never 0, which means "no flicker".</summary>
        public static ushort Seed(uint serial, int x, int y)
        {
            uint h = serial != 0 ? serial * 2654435761u : (uint)(x * 73856093) ^ (uint)(y * 19349663);

            return (ushort)((h >> 8) | 1);
        }

        /// <summary>
        /// Flicker of one fire light at this moment: its size (around 1) and its strength (up to 1).
        /// Three slow waves out of step with each other, so no two flames move together.
        /// </summary>
        public static void Flicker(ushort seed, out float scale, out float strength)
        {
            float t = Time.Ticks * 0.001f;
            float p = seed * 0.0131f;

            float n = 0.55f * MathF.Sin(t * 6.1f + p)
                + 0.30f * MathF.Sin(t * 11.3f + p * 1.7f)
                + 0.15f * MathF.Sin(t * 19.7f + p * 2.9f);

            scale = 1f + 0.02f * n;
            strength = 0.95f + 0.05f * n;
        }
    }
}
