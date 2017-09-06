using System.Net.Torrent.Misc;

namespace System.Net.Torrent.ProtocolExtensions
{
    public class DHTPortExtension : IProtocolExtension
    {
        public event Action<IPeerWireClient, UInt16> Port;
        public bool RemoteUsesDHT { get; private set; }

        public byte[] ByteMask => new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x1 };

        public byte[] CommandIDs => new byte[] { 9 };

        public bool OnHandshake(IPeerWireClient client) => false;

        public bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)
        {
            if (commandId != 9) return false;

            var port = Unpack.UInt16(payload, 0, Unpack.Endianness.Big);

            OnPort(client, port);
            return true;
        }

        private void OnPort(IPeerWireClient client, UInt16 port) => Port?.Invoke(client, port);
    }
}
