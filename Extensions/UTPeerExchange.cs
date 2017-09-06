﻿/*
Copyright (c) 2013, Darren Horrocks
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of Darren Horrocks nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 
*/

using System.Net.Torrent.BEncode;
using System.Net.Torrent.Misc;
using System.Net.Torrent.ProtocolExtensions;

namespace System.Net.Torrent.Extensions
{
    public class UTPeerExchange : IBTExtension
    {
        private ExtendedProtocolExtensions _parent;

        private const string ADDED_KEY = "added";
        private const string ADDED_FLAGS_KEY = "added.f";
        private const string DROPPED_KEY = "dropped";

        public string Protocol => "ut_pex";

        public event Action<IPeerWireClient, IBTExtension, IPEndPoint, byte> Added;
        public event Action<IPeerWireClient, IBTExtension, IPEndPoint> Dropped;

        public void Init(ExtendedProtocolExtensions parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public void Deinit()
        {

        }

        public void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)
        {
            var dictionary = (BDict)BencodingUtils.Decode(handshake);
        }

        public void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)
        {
            BDict dictionary = (BDict) BencodingUtils.Decode(bytes);
            if (!dictionary.ContainsKey(ADDED_KEY) && !dictionary.ContainsKey(ADDED_FLAGS_KEY) && !dictionary.ContainsKey(DROPPED_KEY)) return;

            if (dictionary.ContainsKey(ADDED_KEY) && dictionary.ContainsKey(ADDED_FLAGS_KEY))
            {
                var pexList = (BString)dictionary[ADDED_KEY];
                var pexFlags = (BString)dictionary[ADDED_FLAGS_KEY];

                for (int i = 0; i < pexList.ByteValue.Length/6; i++)
                {
                    var ip = Unpack.UInt32(pexList.ByteValue, i*6, Unpack.Endianness.Little);
                    var port = Unpack.UInt16(pexList.ByteValue, (i * 6) + 4, Unpack.Endianness.Big);
                    var flags = pexFlags.ByteValue[i];

                    var ipAddr = new IPEndPoint(ip, port);

                    Added?.Invoke(peerWireClient, this, ipAddr, flags);
                }
            }
            else //if (d.ContainsKey(DROPPED_KEY))
            {
                BString pexList = (BString)dictionary[DROPPED_KEY];

                for (int i = 0; i < pexList.ByteValue.Length / 6; i++)
                {
                    var ip = Unpack.UInt32(pexList.ByteValue, i * 6, Unpack.Endianness.Little);
                    var port = Unpack.UInt16(pexList.ByteValue, (i * 6) + 4, Unpack.Endianness.Big);

                    var ipAddr = new IPEndPoint(ip, port);

                    Dropped?.Invoke(peerWireClient, this, ipAddr);
                }
            }
        }

        public void SendMessage(IPeerWireClient peerWireClient, IPEndPoint[] addedEndPoints, byte[] flags, IPEndPoint[] droppedEndPoints)
        {
            if (addedEndPoints == null && droppedEndPoints == null) return;

            BDict d = new BDict();

            if (addedEndPoints != null)
            {
                byte[] added = new byte[addedEndPoints.Length * 6];
                for (int x = 0; x < addedEndPoints.Length; x++)
                {
                    addedEndPoints[x].Address.GetAddressBytes().CopyTo(added, x * 6);
                    BitConverter.GetBytes((ushort)addedEndPoints[x].Port).CopyTo(added, (x * 6)+4);
                }

                d.Add("added", new BString { ByteValue = added });
            }

            if (droppedEndPoints != null)
            {
                byte[] dropped = new byte[droppedEndPoints.Length * 6];
                for (int x = 0; x < droppedEndPoints.Length; x++)
                {
                    droppedEndPoints[x].Address.GetAddressBytes().CopyTo(dropped, x * 6);

                    dropped.SetValue((ushort)droppedEndPoints[x].Port, (x * 6) + 2);
                }

                d.Add("dropped", new BString { ByteValue = dropped });
            }

            _parent.SendExtended(peerWireClient, _parent.GetOutgoingMessageID(peerWireClient, this), BencodingUtils.EncodeBytes(d));
        }
    }
}
