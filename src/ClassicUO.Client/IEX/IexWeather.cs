// Enhanced weather (Options > Video, "Enhanced weather and water"): rain as thin streaks with
// splashes where it lands, softer snow, and - with enhanced lighting on - a dimmer, greyer
// picture under rain and storms and lightning flashes in storms.
// The stock Weather class still moves the drops and plays its sounds; this only redraws them.
// Hooks: Game/Weather.cs (drawing), Game/Scenes/GameScene.cs (Update, UseLights).

using System;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal static class IexWeather
    {
        private static readonly Color _rainColor = new Color(176, 196, 228);
        private static readonly Color _splashColor = new Color(200, 215, 240);

        private const int MAX_SPLASHES = 96;
        private const uint SPLASH_LIFE = 260;
        private static readonly Splash[] _splashes = new Splash[MAX_SPLASHES];
        private static int _nextSplash;
        private static float _splashDebt;

        private static uint _lastUpdate;
        private static uint _flashStart, _nextFlash, _thunderAt;

        public static bool On => ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.IexEnhancedWeather;

        /// <summary>How much the weather darkens the day right now, 0..0.3. Fades in and out.</summary>
        public static float Dim { get; private set; }

        /// <summary>Lightning right now, 0..1.</summary>
        public static float Flash { get; private set; }

        /// <summary>
        /// By day the client draws no light map at all. Under rain it has to, or there is nothing
        /// to dim: the game scene treats this like darkness.
        /// </summary>
        public static bool ForceLights => Dim > 0.005f && On && IexLighting.On;

        /// <summary>Once a frame, before the light map is drawn.</summary>
        public static void Update(World world)
        {
            uint now = Time.Ticks;
            uint passed = Math.Min(now - _lastUpdate, 250);
            _lastUpdate = now;

            Weather weather = world.Weather;
            bool active = On && IexLighting.On && weather.CurrentCount > 0 && world.Light.Overall < 20; // no weather underground

            float target = 0f;
            bool storm = false;

            if (active)
            {
                float amount = 0.5f + 0.5f * Math.Min(1f, weather.Count / 70f);

                switch (weather.Type)
                {
                    case WeatherType.WT_RAIN: target = 0.14f * amount; break;
                    case WeatherType.WT_STORM_APPROACH: target = 0.28f * amount; storm = true; break;
                    case WeatherType.WT_STORM_BREWING: target = 0.20f * amount; storm = true; break;
                    case WeatherType.WT_SNOW: target = 0.06f * amount; break;
                }
            }

            // about three seconds to settle, so the sky does not snap dark
            Dim += (target - Dim) * Math.Min(1f, passed / 1500f);

            if (Dim < 0.002f && target == 0f)
            {
                Dim = 0f;
            }

            if (!storm)
            {
                _nextFlash = 0;
                Flash = FlashAt(now);

                return;
            }

            if (_nextFlash == 0)
            {
                _nextFlash = now + (uint)RandomHelper.GetValue(4000, 12000);
            }
            else if (now >= _nextFlash)
            {
                _flashStart = now;
                _nextFlash = now + (uint)RandomHelper.GetValue(7000, 24000);
                _thunderAt = now + (uint)RandomHelper.GetValue(350, 1900);
            }

            if (_thunderAt != 0 && now >= _thunderAt)
            {
                _thunderAt = 0;

                int dx = RandomHelper.GetValue(10, 18) * (RandomHelper.RandomBool() ? 1 : -1);
                int dy = RandomHelper.GetValue(10, 18) * (RandomHelper.RandomBool() ? 1 : -1);
                Client.Game.Audio.PlaySoundWithDistance(world, RandomHelper.RandomList(0x028, 0x206), world.Player.X + dx, world.Player.Y + dy);
            }

            Flash = FlashAt(now);
        }

        // A stroke and a weaker second one, gone in under half a second.
        private static float FlashAt(uint now)
        {
            if (_flashStart == 0)
            {
                return 0f;
            }

            float t = now - _flashStart;

            if (t >= 460f)
            {
                _flashStart = 0;

                return 0f;
            }

            if (t < 50f) return t / 50f;
            if (t < 130f) return 1f - 0.75f * (t - 50f) / 80f;
            if (t < 180f) return 0.25f + 0.45f * (t - 130f) / 50f;

            return 0.7f * (1f - (t - 180f) / 280f);
        }

        /// <summary>The darkness colour under the current weather.</summary>
        public static Vector3 Apply(Vector3 ambient)
        {
            if (Dim > 0f)
            {
                // rain light is grey-blue, and darker
                ambient *= (1f - Dim) * Vector3.Lerp(Vector3.One, new Vector3(0.90f, 0.95f, 1.00f), Math.Min(1f, Dim * 4f));
            }

            if (Flash > 0f)
            {
                ambient = Vector3.Lerp(ambient, new Vector3(0.93f, 0.96f, 1.00f), Flash * 0.75f);
            }

            return ambient;
        }

        /// <summary>One rain drop: a thin pale streak along its fall, in place of the stock blue bar.</summary>
        public static void DrawRain(UltimaBatcher2D batcher, int index, Vector2 start, Vector2 end, float depth)
        {
            Vector2 dir = end - start;

            if (dir.LengthSquared() < 0.01f)
            {
                dir = new Vector2(-0.67f, 0.74f);
            }
            else
            {
                dir.Normalize();
            }

            // three depths of rain: the nearer the longer and the clearer
            int layer = index % 3;
            float length = 8f + layer * 5f;
            Vector3 hue = new Vector3(0f, 0f, 0.22f + layer * 0.09f);

            batcher.DrawLine(SolidColorTextureCache.GetTexture(_rainColor), end - dir * length, end, hue, 1, depth);
        }

        /// <summary>One snow flake: sizes and brightness vary instead of identical squares.</summary>
        public static void DrawSnow(UltimaBatcher2D batcher, int index, int x, int y, float depth)
        {
            int size = 1 + index % 3 / 2 + index % 2; // 1, 2 or 3
            Vector3 hue = new Vector3(0f, 0f, 0.55f + (index % 5) * 0.09f);

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.White), new Rectangle(x, y, size, size), hue, depth);
        }

        /// <summary>Splashes where rain lands: a short ring that widens and fades.</summary>
        public static void DrawSplashes(UltimaBatcher2D batcher, int drops, int x, int y, int width, int height, uint passed, float depth)
        {
            uint now = Time.Ticks;

            // roughly a quarter of the drops on screen land each tenth of a second
            _splashDebt += drops * 0.25f * Math.Min(passed, 100u) / 100f;

            while (_splashDebt >= 1f)
            {
                _splashDebt -= 1f;
                ref Splash s = ref _splashes[_nextSplash];
                _nextSplash = (_nextSplash + 1) % MAX_SPLASHES;
                s.X = x + RandomHelper.GetValue(0, Math.Max(1, width));
                s.Y = y + RandomHelper.GetValue(0, Math.Max(1, height));
                s.Start = now;
            }

            var texture = SolidColorTextureCache.GetTexture(_splashColor);

            for (int i = 0; i < MAX_SPLASHES; i++)
            {
                ref Splash s = ref _splashes[i];

                if (s.Start == 0 || now - s.Start >= SPLASH_LIFE)
                {
                    s.Start = 0;

                    continue;
                }

                float t = (now - s.Start) / (float)SPLASH_LIFE;
                int half = 1 + (int)(t * 4f);
                Vector3 hue = new Vector3(0f, 0f, 0.38f * (1f - t));

                // a flat ring seen from above at the game's angle: two short arcs
                batcher.Draw(texture, new Rectangle(s.X - half, s.Y, half * 2, 1), hue, depth);

                if (half > 2)
                {
                    batcher.Draw(texture, new Rectangle(s.X - half + 1, s.Y - 1, half * 2 - 2, 1), hue * new Vector3(1f, 1f, 0.6f), depth);
                }
            }
        }

        private struct Splash
        {
            public int X, Y;
            public uint Start;
        }
    }
}
