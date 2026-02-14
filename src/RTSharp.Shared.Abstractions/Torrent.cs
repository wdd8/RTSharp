using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RTSharp.Shared.Abstractions
{
    public class Torrent
    {
        /// <summary>
        /// Torrent owner
        /// </summary>
        public IDataProvider Owner { get; init; }

        /// <summary>
        /// Torrent info hash, 20 bytes
        /// </summary>
        public byte[] Hash { get; }

        /// <summary>
        /// Torrent name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Torrent state
        /// </summary>
        public TORRENT_STATE State { get; set; }

        /// <summary>
        /// Is torrent private? (no DHT/PeX/LSD)
        /// </summary>
        public bool? IsPrivate { get; set; }

        /// <summary>
        /// Size in bytes
        /// </summary>
        public ulong Size { get; set; }

        /// <summary>
        /// Size we want to download (in case of 0 priority files)
        /// </summary>
        public ulong WantedSize { get; set; }

        /// <summary>
        /// Piece size
        /// </summary>
        public ulong? PieceSize { get; set; }

        /// <summary>
        /// Wasted bytes
        /// </summary>
        public ulong? Wasted { get; set; }

        /// <summary>
        /// Done percentage
        /// </summary>
        public float Done { get; set; }

        /// <summary>
        /// Total downloaded bytes
        /// </summary>
        public ulong Downloaded { get; set; }

        /// <summary>
        /// Pieces complete * piece size
        /// </summary>
        public ulong CompletedSize { get; set; }

        /// <summary>
        /// Total uploaded bytes
        /// </summary>
        public ulong Uploaded { get; set; }

        /// <summary>
        /// Download speed, B/s
        /// </summary>
        public ulong DLSpeed { get; set; }

        /// <summary>
        /// Upload speed, B/S
        /// </summary>
        public ulong UPSpeed { get; set; }

        /// <summary>
        /// ETA, <c>0</c> if already at 100%
        /// </summary>
        /// <seealso cref="Done"/>
        public TimeSpan ETA { get; set; }

        /// <summary>
        /// Torrent labels
        /// </summary>
        public HashSet<string> Labels { get; set; }

        /// <summary>
        /// Torrent peers. Connected, Total
        /// </summary>
        public (uint Connected, uint Total) Peers { get; set; }
        /// <summary>
        /// Torrent seeders. Connected, Total
        /// </summary>
        public (uint Connected, uint Total) Seeders { get; set; }

        /// <summary>
        /// Torrent priority
        /// </summary>
        public TORRENT_PRIORITY Priority { get; set; }

        /// <summary>
        /// When .torrent file was created
        /// </summary>
        public DateTime? CreatedOnDate { get; set; }

        /// <summary>
        /// Remaining size to download
        /// </summary>
        /// <seealso cref="Size"/>
        /// <seealso cref="Downloaded"/>
        public ulong RemainingSize { get; set; }

        /// <summary>
        /// When torrent finished downloading
        /// </summary>
        public DateTime? FinishedOnDate { get; set; }

        /// <summary>
        /// Time elapsed when downloading torrent.
        /// This can be provider specific, for example elapsed time can be counted from the moment torrent connects to a seeder, or from a moment it was added.
        /// </summary>
        public TimeSpan TimeElapsed { get; set; }

        /// <summary>
        /// Unix timestamp of when torrent was added
        /// </summary>
        public DateTime AddedOnDate { get; set; }

        /// <summary>
        /// Primary tracker URI
        /// </summary>
        public string? TrackerSingle { get; set; }

        /// <summary>
        /// Torrent status message
        /// </summary>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Torrent comment
        /// </summary>
        public string Comment { get; set; }

        /// <summary>
        /// Remote base path of torrent data. This does not include torrent name at the end.
        /// </summary>
        public string RemotePath { get; set; }

        /// <summary>
        /// Is torrent a magnet link dummy waiting to be resolved?
        /// </summary>
        public bool MagnetDummy { get; set; }

        public Torrent(byte[] Hash)
        {
            this.Hash = Hash;
        }
    }

    public enum TORRENT_PRIORITY
    {
        /// <summary>
        /// Reserved
        /// </summary>
        NA = -1,
        /// <summary>
        /// High priority
        /// </summary>
        HIGH = 3,
        /// <summary>
        /// Default torrent priorty
        /// </summary>
        NORMAL = 2,
        /// <summary>
        /// Low priority
        /// </summary>
        LOW = 1,
        /// <summary>
        /// No priority
        /// </summary>
        OFF = 0
    }

    [Flags]
    public enum TORRENT_STATE
    {
        NONE = 0,
        DOWNLOADING = 1 << 0,
        SEEDING = 1 << 1,
        HASHING = 1 << 2,
        PAUSED = 1 << 3,
        STOPPED = 1 << 4,
        COMPLETE = 1 << 5,
        QUEUED = 1 << 6,

        ACTIVE = 1 << 7,
        INACTIVE = 1 << 8,
        ERRORED = 1 << 9,
        ALLOCATING = 1 << 10
    }

    public static class EnumExt
    {
        public static string ToString(this TORRENT_STATE In)
        {
            string stateStr = "N/A";
            if ((In & TORRENT_STATE.SEEDING) == TORRENT_STATE.SEEDING)
                stateStr = "Seeding";
            if ((In & TORRENT_STATE.STOPPED) == TORRENT_STATE.STOPPED)
                stateStr = "Stopped";
            if ((In & TORRENT_STATE.COMPLETE) == TORRENT_STATE.COMPLETE)
                stateStr = "Complete";
            if ((In & TORRENT_STATE.DOWNLOADING) == TORRENT_STATE.DOWNLOADING)
                stateStr = "Downloading";
            if ((In & TORRENT_STATE.PAUSED) == TORRENT_STATE.PAUSED)
                stateStr = "Paused";
            if ((In & TORRENT_STATE.HASHING) == TORRENT_STATE.HASHING)
                stateStr = "Hashing";
            if ((In & TORRENT_STATE.ERRORED) == TORRENT_STATE.ERRORED)
                stateStr = "☠ " + stateStr;
            if ((In & TORRENT_STATE.ACTIVE) == TORRENT_STATE.ACTIVE)
                stateStr = "⚡ " + stateStr;

            return stateStr;
        }

        public static string ToString(this TORRENT_PRIORITY In)
        {
            if (In == TORRENT_PRIORITY.HIGH)
                return "High";
            else if (In == TORRENT_PRIORITY.LOW)
                return "Low";
            else if (In == TORRENT_PRIORITY.NORMAL)
                return "Normal";
            else
                return In == TORRENT_PRIORITY.OFF ? "Off" : "N/A";
        }
    }

    public unsafe class HashEqualityComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y)
        {
            return x.SequenceEqual(y);
        }

        public unsafe int GetHashCode(byte[] obj)
        {
            if (obj.Length < 4) {
                switch (obj.Length) {
                    case 3:
                        return HashCode.Combine(obj[0], obj[1], obj[2]);
                    case 2:
                        return HashCode.Combine(obj[0], obj[1]);
                    case 1:
                        return HashCode.Combine(obj[0]);
                }
            }

            fixed (byte* ptr = &MemoryMarshal.GetReference(obj.AsSpan())) {
                return *(int*)ptr;
            }
        }
    }

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

            fixed (byte* ptr = &MemoryMarshal.GetReference(obj.Hash.AsSpan())) {
                return *(int*)ptr;
            }
        }
    }
}
