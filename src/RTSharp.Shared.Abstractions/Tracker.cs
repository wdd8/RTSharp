namespace RTSharp.Shared.Abstractions
{
    public class Tracker
    {
		public enum TRACKER_STATUS : byte
		{
			ACTIVE = 1 << 0,
			NOT_ACTIVE = 1 << 1,
			NOT_CONTACTED_YET = 1 << 2,
			DISABLED = 1 << 3,
			ENABLED = 1 << 4,
			Max = 1 << 4
		}

		/// <summary>
		/// Tracker ID internal to DataProvider
		/// </summary>
		public object ID { get; set; } 

		/// <summary>
		/// Tracker Uri
		/// </summary>
		public Uri Uri { get; set; }

		/// <summary>
		/// Tracker status
		/// </summary>
		public TRACKER_STATUS Status { get; set; }

		/// <summary>
		/// Is tracker enabled
		/// </summary>
		public bool Enabled => Status.HasFlag(TRACKER_STATUS.ENABLED) && !Status.HasFlag(TRACKER_STATUS.DISABLED);

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
		/// Last tracker announce date
		/// </summary>
		public DateTime? LastUpdated { get; set; }

		/// <summary>
		/// Announce interval
		/// </summary>
		public TimeSpan Interval { get; set; }

		/// <summary>
		/// Tracker status message
		/// </summary>
		public string StatusMsg { get; set; }
	}
}
