using RTSharp.DataProvider.Rtorrent.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RTSharp.DataProvider.Rtorrent.Server.Utils
{
    public unsafe class TorrentEqualityComparer : IEqualityComparer<Torrent>
    {
        public bool Equals(Torrent x, Torrent y)
        {
            return x.Hash.SequenceEqual(y.Hash);
        }

        public unsafe int GetHashCode(Torrent obj)
        {
            if (obj.Hash.Length < 4) {
                switch (obj.Hash.Length) {
                    case 3:
                        return HashCode.Combine(obj.Hash[0], obj.Hash[1], obj.Hash[2]);
                    case 2:
                        return HashCode.Combine(obj.Hash[0], obj.Hash[1]);
                    case 1:
                        return HashCode.Combine(obj.Hash[0]);
                }
            }

            fixed (byte* ptr = &MemoryMarshal.GetReference(obj.Hash.Span)) {
                return *(int*)ptr;
            }
        }
    }
}
