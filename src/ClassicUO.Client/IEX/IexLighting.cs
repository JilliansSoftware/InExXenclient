// Enhanced lighting (Options > Video, "Enhanced lighting"): tinted darkness, warm fire light
// with a slight flicker, softened light edges and glow, a colour grade that follows the hour,
// darker screen edges underground, and light cast by spells and explosions. The art itself is never filtered:
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

        /// <summary>Rows of the light colour table for spell light: flame and energy.</summary>
        public const ushort FlameShader = 58;
        public const uint FlameRgb = 0xFF_9A_40;
        public const ushort EnergyShader = 57;
        public const uint EnergyRgb = 0x8C_A4_FF;

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

        // The finished picture by the world's light level: colour balance, colour strength,
        // and how far the screen edges fall away. Day is untouched.
        private static readonly (int Level, Vector3 Tint, float Saturation, float Vignette)[] _grades =
        {
            (0, new Vector3(1.00f, 1.00f, 1.00f), 1.00f, 0.00f),
            (4, new Vector3(1.04f, 1.00f, 0.93f), 1.00f, 0.00f),  // low sun: golden
            (9, new Vector3(1.00f, 1.00f, 1.00f), 0.93f, 0.06f),
            (13, new Vector3(0.97f, 0.99f, 1.04f), 0.86f, 0.16f), // night: cooler, a little drained
            (20, new Vector3(0.97f, 0.99f, 1.04f), 0.86f, 0.20f),
            (26, new Vector3(0.98f, 1.00f, 1.03f), 0.80f, 0.36f), // underground
        };

        /// <summary>Colour grade and vignette for this light level and weather.</summary>
        public static void Grade(int overall, float weatherDim, out Vector3 tint, out float saturation, out float vignette)
        {
            int i = 1;

            while (i < _grades.Length - 1 && overall > _grades[i].Level)
            {
                i++;
            }

            ref var a = ref _grades[i - 1];
            ref var b = ref _grades[i];
            float t = Math.Clamp((overall - a.Level) / (float)(b.Level - a.Level), 0f, 1f);

            tint = Vector3.Lerp(a.Tint, b.Tint, t);
            saturation = a.Saturation + (b.Saturation - a.Saturation) * t;
            vignette = a.Vignette + (b.Vignette - a.Vignette) * t;

            // rain and storms drain colour too
            saturation *= 1f - weatherDim;
        }

        /// <summary>
        /// Light cast by a spell effect, by its graphic: which colour row, how large (1 = the
        /// light sprite at its own size) and which light sprite. False for effects that cast none.
        /// </summary>
        public static bool EffectLight(ushort graphic, out ushort color, out float scale, out byte id)
        {
            id = 1;

            if (graphic >= 0x36B0 && graphic <= 0x36D3) // explosions
            {
                color = FlameShader;
                scale = 1.15f;
            }
            else if (graphic >= 0x36D4 && graphic <= 0x3727) // fireball, magic arrow, fire bolts, flamestrike
            {
                color = FlameShader;
                scale = graphic >= 0x3709 ? 1f : 0.6f;
            }
            else if (graphic >= 0x379F && graphic <= 0x37B8) // energy bolt
            {
                color = EnergyShader;
                scale = 0.6f;
            }
            else if (graphic >= 0x373A && graphic <= 0x379E) // sparkles: heals, blessings, curses
            {
                color = 0;
                scale = 0.45f;
            }
            else
            {
                color = 0;
                scale = 0f;

                return false;
            }

            return true;
        }

        /// <summary>True for the light colours that are fire: our warm row and the stock oranges.</summary>
        public static bool IsFire(ushort color) => color == WarmShader || color == FlameShader || color == 30 || color == 31;

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
