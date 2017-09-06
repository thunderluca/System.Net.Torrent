/*
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
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Torrent.BEncode;
using System.Net.Torrent.Misc;
using System.Security.Cryptography;
using System.Text;

namespace System.Net.Torrent
{
    public class Metadata
    {
        private const string ANNOUNCE_KEY = "announce";
        private const string ANNOUNCE_LIST_KEY = "announce-list";
        private const string COMMENT_KEY = "comment";
        private const string CREATED_BY_KEY = "created by";
        private const string CREATION_DATE_KEY = "creation date";
        private const string INFO_KEY = "info";
        private const string FILES_KEY = "files";
        private const string PATH_KEY = "path";
        private const string LENGTH_KEY = "length";
        private const string NAME_KEY = "name";
        private const string PRIVATE_KEY = "private";
        private const string PIECES_KEY = "pieces";
        private const string PIECE_LENGTH_KEY = "piece length";

        private IBencodingType _root;

        public byte[] Hash { get; set; }

        public string HashString
        {
            get { return Unpack.Hex(Hash); }
            set { Hash = Pack.Hex(value); }
        }

        public string Comment { get; set; }
        public string Announce { get; set; }
        public ICollection<string> AnnounceList { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public string Name { get; set; }
        public Int64 PieceSize { get; set; }
        public ICollection<byte[]> PieceHashes { get; set; }
        public bool Private { get; set; }
        private IDictionary<string, Int64> Files { get; set; }

        public Metadata()
        {
            Init();
        }

        public Metadata(Stream stream)
        {
            Init();

            Load(stream);
        }

        public Metadata(MagnetLink magnetLink)
        {
            Init();

            Load(magnetLink);
        }

        private void Init()
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();
            Files = new Dictionary<string, long>();
        }

        public bool Load(MagnetLink magnetLink)
        {
            if (magnetLink == null) return false;
            if (magnetLink.Hash == null) return false;

            HashString = magnetLink.HashString;

            if (magnetLink.Trackers != null)
            {
                foreach (var tracker in magnetLink.Trackers)
                {
                    AnnounceList.Add(tracker);
                }
            }

            return true;
        }

        public bool Load(Stream stream)
        {
            _root = BencodingUtils.Decode(stream);
            if (_root == null) return false;

            var dictRoot = (_root as BDict);
            if (dictRoot == null) return false;

            if (dictRoot.ContainsKey(ANNOUNCE_KEY))
            {
                Announce = (BString)dictRoot[ANNOUNCE_KEY];
            }

            if (dictRoot.ContainsKey(ANNOUNCE_LIST_KEY))
            {
                var announceList = (BList)dictRoot[ANNOUNCE_LIST_KEY];
                foreach (IBencodingType type in announceList)
                {
                    if (type is BString)
                    {
                        AnnounceList.Add(type as BString);
                    }
                    else
                    {
                        var list = type as BList;
                        if (list == null) continue;

                        var listType = list;
                        foreach (IBencodingType bencodingType in listType)
                        {
                            var s = (BString)bencodingType;
                            AnnounceList.Add(s);
                        }
                    }
                }
            }

            if (dictRoot.ContainsKey(COMMENT_KEY))
            {
                Comment = (BString)dictRoot[COMMENT_KEY];
            }

            if (dictRoot.ContainsKey(CREATED_BY_KEY))
            {
                CreatedBy = (BString)dictRoot[CREATED_BY_KEY];
            }

            if (dictRoot.ContainsKey(CREATION_DATE_KEY))
            {
                long ts = (BInt)dictRoot[CREATION_DATE_KEY];
                CreationDate = new DateTime(1970, 1, 1).AddSeconds(ts);
            }

            if (dictRoot.ContainsKey(INFO_KEY))
            {
                var infoDict = (BDict)dictRoot[INFO_KEY];

                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    var str = BencodingUtils.EncodeBytes(infoDict);
                    Hash = sha1.ComputeHash(str);
                }                

                if (infoDict.ContainsKey(FILES_KEY))
                {
                    //multi file mode
                    var fileList = (BList)infoDict[FILES_KEY];
                    foreach (IBencodingType bencodingType in fileList)
                    {
                        var fileDict = (BDict)bencodingType;
                        
                        var filename = string.Empty;
                        var filesize = default(Int64);

                        if (fileDict.ContainsKey(PATH_KEY))
                        {
                            var filenameList = (BList)fileDict[PATH_KEY];
                            foreach (IBencodingType type in filenameList)
                            {
                                filename += (BString)type;
                                filename += "\\";
                            }
                            filename = filename.Trim('\\');
                        }

                        if (fileDict.ContainsKey(LENGTH_KEY))
                        {
                            filesize = (BInt)fileDict[LENGTH_KEY];
                        }

                        Files.Add(filename, filesize);
                    }
                }

                if (infoDict.ContainsKey(NAME_KEY))
                {
                    Name = (BString)infoDict[NAME_KEY];
                    if (Files.Count == 0 && infoDict.ContainsKey(LENGTH_KEY))
                    {
                        Files.Add(Name, (BInt)infoDict[LENGTH_KEY]);
                    }
                }

                if (infoDict.ContainsKey(PRIVATE_KEY))
                {
                    var isPrivate = (BInt)infoDict[PRIVATE_KEY];
                    Private = isPrivate != 0;
                }

                if (infoDict.ContainsKey(PIECES_KEY))
                {
                    var pieces = (BString)infoDict[PIECES_KEY];
                    for (var x = 0; x < pieces.ByteValue.Length; x += 20)
                    {
                        var hash = pieces.ByteValue.GetBytes(x, 20);
                        PieceHashes.Add(hash);
                    }
                }

                if (infoDict.ContainsKey(PIECE_LENGTH_KEY))
                {
                    PieceSize = (BInt)infoDict[PIECE_LENGTH_KEY];
                }
            }

            return true;
        }

        #region Static Helpers
        public static Metadata FromString(string metadata)
        {
            return FromBuffer(Encoding.ASCII.GetBytes(metadata));
        }

        public static Metadata FromBuffer(byte[] metadata)
        {
            using (var ms = new MemoryStream(metadata))
            {
                return new Metadata(ms);
            }
        }

        public static Metadata FromFile(string filename)
        {
            using (var fs = File.OpenRead(filename))
            {
                return new Metadata(fs);
            }
        }
        #endregion
    }
}
