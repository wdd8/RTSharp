using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RTSharp.Models
{
    public sealed partial class TorrentSelectionSummary : ObservableObject
    {
        private const int MAX_INDIVIDUAL_VALUES = 4;

        [ObservableProperty]
        public partial string CountDisplay { get; set; } = "";

        [ObservableProperty]
        public partial float Done { get; set; }

        [ObservableProperty]
        public partial string DownloadedDisplay { get; set; } = Converters.ZeroBytes;

        [ObservableProperty]
        public partial string UploadedDisplay { get; set; } = Converters.ZeroBytes;

        [ObservableProperty]
        public partial string RemainingSizeDisplay { get; set; } = Converters.ZeroBytes;

        [ObservableProperty]
        public partial string WastedDisplay { get; set; } = Converters.ZeroBytes;

        [ObservableProperty]
        public partial string DLSpeedDisplay { get; set; } = Converters.ZeroBytesPerSec;

        [ObservableProperty]
        public partial string UPSpeedDisplay { get; set; } = Converters.ZeroBytesPerSec;

        [ObservableProperty]
        public partial string RatioDisplay { get; set; } = "0.000";

        [ObservableProperty]
        public partial string SeedersDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string PeersDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string ETADisplay { get; set; } = "";

        [ObservableProperty]
        public partial string WantedSizeDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string StatesDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string ConnectionsDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string TrackersDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string LabelsDisplay { get; set; } = "";

        [ObservableProperty]
        public partial string AddedOnDisplay { get; set; } = "";

        public void Update(IReadOnlyList<Torrent> Torrents)
        {
            ulong wantedSize = 0, completedSize = 0, downloaded = 0, uploaded = 0, remainingSize = 0, wasted = 0;
            ulong dlSpeed = 0, upSpeed = 0;
            uint seedersConnected = 0, seedersTotal = 0, peersConnected = 0, peersTotal = 0;
            bool anyWasted = false;
            var maxEta = TimeSpan.Zero;
            DateTime? earliestAdded = null, latestAdded = null;

            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var connections = new HashSet<string>(StringComparer.Ordinal);
            var trackers = new HashSet<string>(StringComparer.Ordinal);
            var labels = new HashSet<string>(StringComparer.Ordinal);

            foreach (var torrent in Torrents) {
                wantedSize += torrent.WantedSize;
                completedSize += torrent.CompletedSize;
                downloaded += torrent.Downloaded;
                uploaded += torrent.Uploaded;
                remainingSize += torrent.RemainingSize;
                if (torrent.Wasted != null) {
                    wasted += torrent.Wasted.Value;
                    anyWasted = true;
                }
                dlSpeed += torrent.DLSpeed;
                upSpeed += torrent.UPSpeed;
                seedersConnected += torrent.Seeders.Connected;
                seedersTotal += torrent.Seeders.Total;
                peersConnected += torrent.Peers.Connected;
                peersTotal += torrent.Peers.Total;

                if (!torrent.IsCompleted && torrent.ETA > maxEta)
                    maxEta = torrent.ETA;

                if (earliestAdded == null || torrent.AddedOnDate < earliestAdded)
                    earliestAdded = torrent.AddedOnDate;
                if (latestAdded == null || torrent.AddedOnDate > latestAdded)
                    latestAdded = torrent.AddedOnDate;

                if (!String.IsNullOrEmpty(torrent.State)) {
                    states.TryGetValue(torrent.State, out var stateCount);
                    states[torrent.State] = stateCount + 1;
                }

                connections.Add(torrent.DataOwner.Config.Plugin.Name);

                var tracker = torrent.TrackerDisplayName ?? torrent.TrackerSingle;
                if (!String.IsNullOrEmpty(tracker))
                    trackers.Add(tracker);

                foreach (var label in torrent.Labels)
                    labels.Add(label);
            }

            CountDisplay = $"{Torrents.Count} torrents selected";
            Done = wantedSize == 0 ? 100 : (float)completedSize / wantedSize * 100;

            WantedSizeDisplay = $"{Converters.GetSIDataSize(wantedSize)} ({Converters.GetSIDataSize(completedSize)} completed)";
            DownloadedDisplay = Converters.GetSIDataSize(downloaded);
            UploadedDisplay = Converters.GetSIDataSize(uploaded);
            RemainingSizeDisplay = Converters.GetSIDataSize(remainingSize);
            WastedDisplay = anyWasted ? Converters.GetSIDataSize(wasted) : "";
            DLSpeedDisplay = Converters.GetSIDataSpeed(dlSpeed);
            UPSpeedDisplay = Converters.GetSIDataSpeed(upSpeed);
            RatioDisplay = (downloaded == 0 ? 0f : (float)uploaded / downloaded).ToString("N3", CultureInfo.CurrentCulture);
            SeedersDisplay = new ConnectedTotalPair(seedersConnected, seedersTotal).ToString();
            PeersDisplay = new ConnectedTotalPair(peersConnected, peersTotal).ToString();
            ETADisplay = maxEta == TimeSpan.Zero ? "" : Converters.ToAgoString(maxEta);

            StatesDisplay = String.Join(", ", states.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Value} {x.Key}"));
            ConnectionsDisplay = Collapse(connections, "connections");
            TrackersDisplay = Collapse(trackers, "trackers");
            LabelsDisplay = Collapse(labels, "labels");

            if (earliestAdded == null)
                AddedOnDisplay = "";
            else if (earliestAdded == latestAdded)
                AddedOnDisplay = earliestAdded.Value.ToLocalTime().ToString();
            else
                AddedOnDisplay = $"{earliestAdded.Value.ToLocalTime()} - {latestAdded!.Value.ToLocalTime()}";
        }

        private static string Collapse(HashSet<string> Values, string Noun)
        {
            if (Values.Count == 0)
                return "";
            if (Values.Count > MAX_INDIVIDUAL_VALUES)
                return $"{Values.Count} {Noun}";

            return String.Join(", ", Values.Order(StringComparer.Ordinal));
        }
    }
}
