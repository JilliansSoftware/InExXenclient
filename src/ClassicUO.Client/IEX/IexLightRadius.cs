// IEX: how far each light reaches, as the server says (ModernUO: Custom/IEX/Lights/LightRadii.cs):
// lamps, and anyone glowing with a Light spell (Custom/IEX/Lights/MagicLight.cs).
// Lights are a commodity on In Ex Xen: a torch lights more than a lantern, a lantern more than a
// candle, and staff can give any light its own reach. The game scene asks for the reach of each
// lit item it draws; unknown serials are batched into one request a frame (0xBF sub-command 0xEF:
// count byte, then serials) and the answers (the same, with a reach byte in tiles after each
// serial; 0 = our own default for that light shape) are kept until the world is cleared. A server
// that never answers costs one request per lit item.

using System.Collections.Generic;
using System.IO;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.IEX
{
    internal static class IexLightRadius
    {
        private const ushort SubCommand = 0xEF;
        private const int MaxPerRequest = 32;
        private const int MaxKept = 4096;

        private static readonly Dictionary<uint, byte> _radii = new Dictionary<uint, byte>();
        private static readonly HashSet<uint> _asked = new HashSet<uint>();
        private static readonly List<uint> _pending = new List<uint>();

        /// <summary>The server's reach for this item in tiles, or 0 while unknown or when it has none.</summary>
        public static byte Get(uint serial)
        {
            if (_radii.TryGetValue(serial, out byte tiles))
            {
                return tiles;
            }

            if (_asked.Count < MaxKept && _asked.Add(serial))
            {
                _pending.Add(serial);
            }

            return 0;
        }

        /// <summary>Sends what was asked for this frame.</summary>
        public static void Flush()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            NetClient socket = NetClient.Socket;

            if (socket == null || !socket.IsConnected)
            {
                _pending.Clear();

                return;
            }

            for (int start = 0; start < _pending.Count; start += MaxPerRequest)
            {
                int count = System.Math.Min(MaxPerRequest, _pending.Count - start);
                var writer = new StackDataWriter(8 + count * 4);

                writer.WriteUInt8(0xBF);
                writer.WriteZero(2);
                writer.WriteUInt16BE(SubCommand);
                writer.WriteUInt8((byte)count);

                for (int i = 0; i < count; i++)
                {
                    writer.WriteUInt32BE(_pending[start + i]);
                }

                writer.Seek(1, SeekOrigin.Begin);
                writer.WriteUInt16BE((ushort)writer.BytesWritten);

                socket.Send(writer.BufferWritten);
                writer.Dispose();
            }

            _pending.Clear();
        }

        /// <summary>The server's answer, or a reach staff just changed.</summary>
        public static void OnPacket(ref StackDataReader p)
        {
            int count = p.ReadUInt8();

            for (int i = 0; i < count && p.Remaining >= 5; i++)
            {
                uint serial = p.ReadUInt32BE();
                _radii[serial] = p.ReadUInt8();
                _asked.Add(serial);
            }
        }

        /// <summary>Forgets one serial, so it is asked about again when seen again.</summary>
        public static void Forget(uint serial)
        {
            if (_radii.Remove(serial) | _asked.Remove(serial))
            {
                _pending.Remove(serial);
            }
        }

        public static void Clear()
        {
            _radii.Clear();
            _asked.Clear();
            _pending.Clear();
        }
    }
}
