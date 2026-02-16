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
        public partial IImage? Icon { get; set; }

        /// <summary>
        /// Tracker URI
        /// </summary>
        public string Uri { get; set; }

        public string Domain => UriUtils.GetDomainForTracker(Uri);

        /// <summary>
        /// User set display name
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Display combination of Uri/DisplayName
        /// </summary>
        [ObservableProperty]
        public partial string Display { get; set; }

        /// <summary>
        /// Status
        /// </summary>
        [ObservableProperty]
        public partial string Status { get; set; }
        private TRACKER_STATUS StatusInternal { get; set; }

        /// <summary>
        /// Tracker seeders
        /// </summary>
        [ObservableProperty]
        public partial uint Seeders { get; set; }

        /// <summary>
        /// Tracker peers
        /// </summary>
        [ObservableProperty]
        public partial uint Peers { get; set; }

        /// <summary>
        /// Downloaded entries
        /// </summary>
        [ObservableProperty]
        public partial uint Downloaded { get; set; }

        /// <summary>
        /// Last updated
        /// </summary>
        [ObservableProperty]
        public partial DateTime? LastUpdatedDate { get; set; }

        /// <summary>
        /// Update interval
        /// </summary>
        [ObservableProperty]
        public partial TimeSpan Interval { get; set; }

        /// <summary>
        /// Tracker message
        /// </summary>
        [ObservableProperty]
        public partial string Message { get; set; }

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
                Display = Uri;
            else {
                Display = DisplayName;
            }
        }
    }
}
