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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Torrent.BEncode;
using System.Net.Torrent.Misc;
using System.Text;

namespace System.Net.Torrent
{
    public class HTTPTrackerClient : BaseScraper, ITrackerClient
    {
        private const string PEERS_KEY = "peers";
        private const string INTERVAL_KEY = "interval";
        private const string COMPLETE_KEY = "complete";
        private const string INCOMPLETE_KEY = "incomplete";
        private const string DOWNLOADED_KEY = "downloaded";
        private const string FILES_KEY = "files";

        public HTTPTrackerClient(int timeout) 
            : base(timeout)
        {

        }

        private static IEnumerable<IPEndPoint> GetPeers(byte[] peerData)
        {
            for (var i = 0; i < peerData.Length; i += 6)
            {
                long addr = Unpack.UInt32(peerData, i, Unpack.Endianness.Big);
                var port = Unpack.UInt16(peerData, i + 4, Unpack.Endianness.Big);

                yield return new IPEndPoint(addr, port);
            }
        }

        public IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId) => 
            hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));

        public AnnounceInfo Announce(string url, string hash, string peerId) => Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);

        public AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, 
            int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)
        {
            var hashBytes = Pack.Hex(hash);
            var peerIdBytes = Encoding.ASCII.GetBytes(peerId);

            var realUrl = url.Replace("scrape", "announce") + "?";

            var hashEncoded = string.Empty;
            foreach (byte b in hashBytes)
            {
                hashEncoded += string.Format("%{0:X2}", b);
            }

            var peerIdEncoded = string.Empty;
            foreach (byte b in peerIdBytes)
            {
                peerIdEncoded += string.Format("%{0:X2}", b);
            }

            realUrl = BuildUrl(hashEncoded, peerIdEncoded, listenPort, bytesUploaded, bytesDownloaded, bytesLeft);

            var webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = $"{nameof(System)}.{nameof(Net)}.{nameof(Torrent)}";
            var webResponse = (HttpWebResponse)webRequest.GetResponse();

            var stream = webResponse.GetResponseStream();

            if (stream == null) return null;

            var binaryReader = new BinaryReader(stream);

            var bytes = new byte[0];

            while (true)
            {
                try
                {
                    var b = new byte[1];
                    b[0] = binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            var decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0)
            {
                return null;
            }

            if (!decoded.ContainsKey(PEERS_KEY))
            {
                return null;
            }

            if (!(decoded[PEERS_KEY] is BString))
            {
                throw new NotSupportedException("Dictionary based peers not supported");
            }

            var waitTime = 0;
            var seeders = 0;
            var leachers = 0;

            if (decoded.ContainsKey(INTERVAL_KEY))
            {
                waitTime = (BInt)decoded[INTERVAL_KEY];
            }

            if (decoded.ContainsKey(COMPLETE_KEY))
            {
                seeders = (BInt)decoded[COMPLETE_KEY];
            }

            if (decoded.ContainsKey(INCOMPLETE_KEY))
            {
                leachers = (BInt)decoded[INCOMPLETE_KEY];
            }

            var peerBinary = (BString)decoded[PEERS_KEY];

            return new AnnounceInfo(GetPeers(peerBinary.ByteValue), waitTime, seeders, leachers);
        }

        private string BuildUrl(string hashEncoded, string peerIdEncoded, int listenPort, long bytesUploaded, long bytesDownloaded, long bytesLeft) =>
            $"info_hash={hashEncoded}&peer_id={peerIdEncoded}&port={listenPort}&uploaded={bytesUploaded}&downloaded={bytesDownloaded}&left={bytesLeft}&event=started&compact=1";

        public IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)
        {
            var returnVal = new Dictionary<string, ScrapeInfo>();

            var realUrl = url.Replace("announce", "scrape") + "?";

            var hashEncoded = string.Empty;
            foreach (string hash in hashes)
            {
                var hashBytes = Pack.Hex(hash);

                hashEncoded = hashBytes.Aggregate(hashEncoded, (current, b) => current + string.Format("%{0:X2}", b));

                realUrl += $"info_hash={hashEncoded}&";
            }

            var webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = $"{nameof(System)}.{nameof(Net)}.{nameof(Torrent)}";
            var webResponse = (HttpWebResponse)webRequest.GetResponse();

            var stream = webResponse.GetResponseStream();

            if (stream == null) return null;

            var binaryReader = new BinaryReader(stream);

            var bytes = new byte[0];
            
            while (true)
            {
                try
                {
                    var b = new byte[1];
                    b[0] = binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            var decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0) return null;

            if (!decoded.ContainsKey(FILES_KEY)) return null;

            var bDecoded = (BDict)decoded[FILES_KEY];

            foreach (var k in bDecoded.Keys)
            {
                var dictionary = (BDict)bDecoded[k];

                if (dictionary.ContainsKey(COMPLETE_KEY) && dictionary.ContainsKey(DOWNLOADED_KEY) && dictionary.ContainsKey(INCOMPLETE_KEY))
                {
                    var rk = Unpack.Hex(BencodingUtils.ExtendedASCIIEncoding.GetBytes(k));
                    returnVal.Add(rk, new ScrapeInfo((uint)((BInt)dictionary[COMPLETE_KEY]).Value, (uint)((BInt)dictionary[DOWNLOADED_KEY]).Value, (uint)((BInt)dictionary[INCOMPLETE_KEY]).Value, ScraperType.HTTP));
                }
            }

            return returnVal;
        }
    }
}
