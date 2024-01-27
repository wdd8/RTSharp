using System.Diagnostics;

namespace RTSharp.Shared.Abstractions
{
	[DebuggerDisplay("{Path}")]
	public class File
	{
		public enum PRIORITY
		{
			/// <summary>
			/// Don't download
			/// </summary>
			DONT_DOWNLOAD = 0,
			/// <summary>
			/// Default priority
			/// </summary>
			NORMAL = 1,
			/// <summary>
			/// High priority
			/// </summary>
			HIGH = 2,
			/// <summary>
			/// Not available
			/// </summary>
			NA = 3
		}

		public enum DOWNLOAD_STRATEGY
		{
			/// <summary>
			/// Default
			/// </summary>
			NORMAL = 0,
			/// <summary>
			/// Prioritize downloading first
			/// </summary>
			PRIORITIZE_FIRST = 1,
			/// <summary>
			/// Prioritize downloading last
			/// </summary>
			PRIORITIZE_LAST = 2,
			/// <summary>
			/// Not available
			/// </summary>
			NA = 3
		}

		/// <summary>
		/// File path
		/// </summary>
		public string Path { get; set; }

		/// <summary>
		/// Size in bytes
		/// </summary>
		public ulong Size { get; set; }

		/// <summary>
		/// Downloaded chunks
		/// </summary>
		public ulong DownloadedChunks { get; set; }

		/// <summary>
		/// Percent done
		/// </summary>
		public float Done {
			get {
				var done = (float)Downloaded / Size * 100;
				return done > 100 ? 100 : done;
			}
		}

		/// <summary>
		/// Downloaded size in bytes
		/// </summary>
		public ulong Downloaded { get; set; }

		/// <summary>
		/// Priority
		/// </summary>
		public PRIORITY Priority { get; set; }

		/// <summary>
		/// Download strategy
		/// </summary>
		public DOWNLOAD_STRATEGY DownloadStrategy { get; set; }
	}
}
