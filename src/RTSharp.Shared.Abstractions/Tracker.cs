namespace RTSharp.Shared.Abstractions
{
    public class Tracker
    {
        /// <summary>
        /// One of <see cref="WORKING"/>, <see cref="NOT_WORKING"/>, <see cref="NOT_CONTACTED_YET"/> or <see cref="DISABLED"/>,
        /// optionally with <see cref="UPDATING"/>
        /// </summary>
        public enum TRACKER_STATUS : byte
        {
            /// <summary>
            /// Last announce succeeded
            /// </summary>
            WORKING = 1 << 0,
            /// <summary>
            /// Last announce failed
            /// </summary>
            NOT_WORKING = 1 << 1,
            NOT_CONTACTED_YET = 1 << 2,
            DISABLED = 1 << 3,
            /// <summary>
            /// Announce in progress
            /// </summary>
            UPDATING = 1 << 5,
            Max = 1 << 5
        }

        /// <summary>
        /// Tracker ID internal to DataProvider
        /// </summary>
        public required object ID { get; set; } 

        /// <summary>
        /// Tracker Uri
        /// </summary>
        public required string Uri { get; set; }

        /// <summary>
        /// Tracker status
        /// </summary>
        public TRACKER_STATUS Status { get; set; }

        /// <summary>
        /// Is tracker enabled
        /// </summary>
        public bool Enabled => !Status.HasFlag(TRACKER_STATUS.DISABLED);

        /// <summary>
        /// Seeders
        /// </summary>
        public uint Seeders { get; set; }

        /// <summary>
        /// Peers
        /// </summary>
        public uint Peers { get; set; }

        /// <summary>
        /// How many peers downloaded so far
        /// </summary>
        /// <remarks>???</remarks>
        public uint Downloaded { get; set; }

        /// <summary>
        /// Last tracker announce date, null if unknown
        /// </summary>
        public DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Announce interval, null if unknown
        /// </summary>
        public TimeSpan? Interval { get; set; }

        /// <summary>
        /// Next scheduled announce, null if none is scheduled
        /// </summary>
        public DateTime? NextAnnounce { get; set; }

        /// <summary>
        /// Tracker status message
        /// </summary>
        public required string StatusMsg { get; set; }
    }
}
