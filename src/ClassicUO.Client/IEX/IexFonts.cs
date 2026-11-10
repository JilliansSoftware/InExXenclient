// Font overrides for server-chosen fonts that read poorly.

using ClassicUO.Game.Data;

namespace ClassicUO.IEX
{
    internal static class IexFonts
    {
        // Single-click labels: the server asks for font 3 (wide-spaced Unicode 3 / faint ASCII 3).
        // Unicode font 1 matches speech and is far easier to read.
        public const byte LabelFont = 1;

        public static void Apply(MessageType type, ref byte font, ref bool unicode)
        {
            if (type == MessageType.Label)
            {
                font = LabelFont;
                unicode = true;
            }
        }
    }
}
