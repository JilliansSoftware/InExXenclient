// In Ex Xen client additions. Upstream files only get small hooks marked "// IEX:".

using System.IO;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.IEX
{
    internal static class IexClient
    {
        public const string Name = "In Ex Xen";

        // Login screen links. Empty = link hidden.
        public const string WebsiteUrl = "";
        public const string DiscordUrl = "";
        public const string ClassicUOUrl = "https://www.classicuo.eu";

        // Sent to the server after entering the world so it can turn away outdated clients
        // (ModernUO: Custom/IEX/ClientVersionCheck.cs). 0xBF sub-command, ASCII version string.
        private const ushort VersionSubCommand = 0xEE;

        public static string Version => CUOEnviroment.Version;

        public static string WindowTitle(string prefix)
        {
            return string.IsNullOrEmpty(prefix) ? $"{Name} - {Version}" : $"{prefix} - {Name} - {Version}";
        }

        public static string VersionLabel => $"{Name} {Version} (ClassicUO)";

        public static string Link(string url, string text)
        {
            return $"<body link=\"#FF00FF00\" vlink=\"#FF00FF00\" ><a href=\"{url}\">{text}";
        }

        public static void SendVersion(NetClient socket)
        {
            var writer = new StackDataWriter(64);

            writer.WriteUInt8(0xBF);
            writer.WriteZero(2);
            writer.WriteUInt16BE(VersionSubCommand);
            writer.WriteASCII(Version); // null-terminated

            writer.Seek(1, SeekOrigin.Begin);
            writer.WriteUInt16BE((ushort)writer.BytesWritten);

            socket.Send(writer.BufferWritten);
            writer.Dispose();
        }
    }
}
