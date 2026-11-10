// "Text size" option: scales overhead text, the journal and the chat lines (not gumps, whose
// layouts are fixed pixel art; the Options zoom slider covers those). Stored per profile.

using System;
using ClassicUO.Configuration;
using ClassicUO.Game;

namespace ClassicUO.IEX
{
    internal static class IexText
    {
        public const int Min = 100, Max = 200, Step = 25;

        public static float Scale
        {
            get
            {
                int v = ProfileManager.CurrentProfile?.IexTextScale ?? Min;
                return v <= Min || v > Max ? 1f : v / 100f;
            }
        }

        public static int Snap(int percent) => Math.Clamp((int)Math.Round(percent / (double)Step) * Step, Min, Max);

        // On-screen size of a text after scaling.
        public static int Width(RenderedText t) => (int)(t.Width * Scale);
        public static int Height(RenderedText t) => (int)(t.Height * Scale);
        public static int Height(int unscaledHeight) => (int)(unscaledHeight * Scale);
    }
}
