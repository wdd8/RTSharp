using System;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Utils;
using static RTSharp.Shared.Abstractions.Tracker;

namespace RTSharp.Models
{
	public partial class Tracker : ObservableObject
    {
		/// <summary>
		/// Icon representing <see cref="Origin"/>
		/// </summary>
		[ObservableProperty]
		public IImage? icon;

		/// <summary>
		/// Tracker URI
		/// </summary>
		public Uri Uri { get; set; }

		public string Domain => UriUtils.GetDomainForTracker(Uri);

		/// <summary>
		/// User set display name
		/// </summary>
		public string? DisplayName { get; set; }

		/// <summary>
		/// Display combination of Uri/DisplayName
		/// </summary>
		[ObservableProperty]
		public string display;

		/// <summary>
		/// Status
		/// </summary>
		[ObservableProperty]
		public string status;

		private TRACKER_STATUS StatusInternal { get; set; }

		/// <summary>
		/// Tracker seeders
		/// </summary>
		[ObservableProperty]
		public uint seeders;

		/// <summary>
		/// Tracker peers
		/// </summary>
		[ObservableProperty]
		public uint peers;

		/// <summary>
		/// Downloaded entries
		/// </summary>
		[ObservableProperty]
		public uint downloaded;

		/// <summary>
		/// Last updated
		/// </summary>
		[ObservableProperty]
		public DateTime? lastUpdatedDate;

		/// <summary>
		/// Update interval
		/// </summary>
		[ObservableProperty]
		public TimeSpan interval;

		/// <summary>
		/// Tracker message
		/// </summary>
		[ObservableProperty]
		public string message;

		public void UpdateFromPluginModel(Shared.Abstractions.Tracker In)
		{
			this.StatusInternal = In.Status;

			this.Status = FlagsMapper.MapConcat(In.Status, x => x switch {
				TRACKER_STATUS.ACTIVE => "Active",
				TRACKER_STATUS.DISABLED => "Disabled",
				TRACKER_STATUS.ENABLED => "Enabled",
				TRACKER_STATUS.NOT_ACTIVE => "Not active",
				TRACKER_STATUS.NOT_CONTACTED_YET => "Not contacted yet",
				_ => throw new ArgumentOutOfRangeException()
			}, ", ");
			this.StatusInternal = In.Status;

			this.Seeders = In.Seeders;
			this.Peers = In.Peers;
			this.Downloaded = In.Downloaded;
			this.LastUpdatedDate = In.LastUpdated;
			this.Interval = In.Interval;
			this.Message = In.StatusMsg;
		}

		public static Tracker FromPluginModel(Shared.Abstractions.Tracker In)
		{
			var ret = new Tracker {
				Uri = In.Uri
			};

			ret.UpdateDisplay();
			ret.UpdateFromPluginModel(In);

			return ret;
		}

		public void UpdateDisplay()
		{
			if (String.IsNullOrEmpty(DisplayName))
				Display = Uri.AbsoluteUri;
			else {
				Display = DisplayName;
			}
		}
	}
}
