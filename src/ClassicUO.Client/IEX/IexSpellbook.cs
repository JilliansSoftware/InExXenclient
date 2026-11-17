// IEX: Night Sight is a 4th circle spell on In Ex Xen, kept outside the spellbook's 64 slots
// (its 1st circle slot holds Light; ModernUO: Custom/IEX/Lights/MagicLight.cs). The server says
// which books hold it (0xBF sub-command 0xF0: book serial, flags byte, 1 = Night Sight); the
// spellbook window lists it at the end of the 4th circle and casts it as spell 65 (server 64).

using System.Collections.Generic;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.IO;

namespace ClassicUO.IEX
{
    internal static class IexSpellbook
    {
        /// <summary>Night Sight's spell number in the book window and in cast requests (1-based, like the rest).</summary>
        public const int NightSightIndex = 65;

        public static readonly SpellDefinition NightSight = new SpellDefinition(
            "Night Sight",
            NightSightIndex,
            0x1B5D,
            "In Lor",
            TargetType.Beneficial,
            Reagents.SpidersSilk,
            Reagents.SulfurousAsh
        );

        private static readonly HashSet<uint> _withNightSight = new HashSet<uint>();

        public static bool HasNightSight(uint book) => _withNightSight.Contains(book);

        public static void OnPacket(ref StackDataReader p)
        {
            uint serial = p.ReadUInt32BE();
            bool nightSight = (p.ReadUInt8() & 1) != 0;

            bool changed = nightSight ? _withNightSight.Add(serial) : _withNightSight.Remove(serial);

            if (changed)
            {
                UIManager.GetGump<SpellbookGump>(serial)?.RequestUpdateContents();
            }
        }

        public static void Clear() => _withNightSight.Clear();
    }
}
