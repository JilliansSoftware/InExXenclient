// In Ex Xen: names for statics that tiledata only calls "tree".

using ClassicUO.Assets;

namespace ClassicUO.IEX
{
    internal static class IexTreeNames
    {
        // The four jungle trees, trunk pieces only (the canopy stays "leaves", like every other tree).
        // The shard treats all four as the one source of jungle wood (ModernUO: Custom/IEX/ColoredWood.cs).
        private static readonly (ushort First, ushort Last, string Name)[] _names =
        [
            (0x0D41, 0x0D44, "kapok tree"),
            (0x0D57, 0x0D5B, "kapok tree"),
            (0x0D6E, 0x0D72, "brazil nut tree"),
            (0x0D84, 0x0D86, "mahogany tree")
        ];

        public static void Apply(TileDataLoader tileData)
        {
            var statics = tileData.StaticData;

            foreach (var (first, last, name) in _names)
            {
                for (int id = first; id <= last && id < statics.Length; id++)
                {
                    statics[id].Name = name;
                }
            }
        }
    }
}
