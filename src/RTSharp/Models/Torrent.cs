using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TrackerDb;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Models
{
    public static class TorrentUpdateExt
    {
        public static async ValueTask<List<(int Index, Torrent Obj)>> UpdateFromPluginModelMulti(IList<Torrent> Domain, InfoHashDictionary<Shared.Abstractions.Torrent> Plugin)
        {
            var ret = new List<(int Index, Torrent Obj)>();
            if (Plugin.Count == 0)
                return ret;

            var trackers = new HashSet<string>();
            foreach (var torrent in Domain) {
                if (Plugin.TryGetValue(torrent.Hash, out var plugin)) {
                    if (torrent.TrackerSingle != plugin.TrackerSingle && plugin.TrackerSingle != null) {
                        trackers.Add(plugin.TrackerSingle);
                    }
                }
            }

            IEnumerable<TrackerInfo> trackerInfo = [];
            Dictionary<TrackerInfo, Bitmap> images = [];

            if (trackers.Count != 0) {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                trackerInfo = await trackerDb.GetTrackerInfo(trackers.Select(UriUtils.GetDomainForTracker));
                var imagesTasks = trackerInfo.Where(x => x.ImageHash != null).ToDictionary(x => x, x => imageCache.GetCachedImage(x.ImageHash).AsTask());
                await Task.WhenAll(imagesTasks.Values.ToArray());

                images = imagesTasks.ToDictionary(x => x.Key, x => x.Value.Result);
            }

            for (var x = 0;x < Domain.Count;x++) {
                var torrent = Domain[x];
                if (Plugin.TryGetValue(torrent.Hash, out var plugin)) {
                    if (plugin.TrackerSingle != torrent.TrackerSingle && plugin.TrackerSingle != null) {
                        var domain = UriUtils.GetDomainForTracker(plugin.TrackerSingle);
                        var info = trackerInfo.FirstOrDefault(x => x.Domain == domain);

                        if (info != null) {
                            if (images.TryGetValue(info, out var image)) {
                                torrent.TrackerIcon = image;
                            }
                            torrent.TrackerDisplayName = info.Name ?? domain;
                        } else {
                            torrent.TrackerDisplayName = domain;
                        }
                    }

                    await torrent.UpdateFromPluginModel(plugin, false);
                    Plugin.Remove(torrent.Hash);
                    ret.Add((x, torrent));
                }
            }

            return ret;
        }

        public static async ValueTask UpdateMulti(IEnumerable<Torrent> Domain)
        {
            var torrents = new HashSet<Torrent>();
            foreach (var torrent in Domain) {
                if (torrent.TrackerDisplayName == null && torrent.TrackerSingle != null) {
                    torrents.Add(torrent);
                }
            }

            IEnumerable<TrackerInfo> trackerInfo = [];
            Dictionary<TrackerInfo, Bitmap> images = [];

            if (torrents.Count != 0) {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                trackerInfo = await trackerDb.GetTrackerInfo(torrents.Select(x => UriUtils.GetDomainForTracker(x.TrackerSingle)));
                var imagesTasks = trackerInfo.Where(x => x.ImageHash != null).ToDictionary(x => x, x => imageCache.GetCachedImage(x.ImageHash).AsTask());
                await Task.WhenAll(imagesTasks.Values.ToArray());

                images = imagesTasks.ToDictionary(x => x.Key, x => x.Value.Result);
            }

            foreach (var torrent in torrents) {
                var domain = UriUtils.GetDomainForTracker(torrent.TrackerSingle);
                var info = trackerInfo.FirstOrDefault(x => x.Domain == domain);

                if (info != null) {
                    if (images.TryGetValue(info, out var image)) {
                        torrent.TrackerIcon = image;
                    }
                    torrent.TrackerDisplayName = info.Name ?? domain;
                } else {
                    torrent.TrackerDisplayName = domain;
                }
            }
        }
    }

    public partial class Torrent : ObservableObject
    {
        /// <summary>
        /// Torrent info hash, 20 bytes
        /// </summary>
        public byte[] Hash { get; set; }

        public Plugin.RTSharpDataProvider DataOwner { get; set; }

        public Plugin.RTSharpPlugin Plugin { get; set; }

        /// <summary>
        /// Torrent name
        /// </summary>
        [ObservableProperty]
        public partial string Name { get; set; }

        [ObservableProperty]
        public partial TORRENT_STATE InternalState { get; set; }

        /// <summary>
        /// State
        /// </summary>
        [ObservableProperty]
        public partial string State { get; set; }

        /// <summary>
        /// Is torrent private? (no DHT/PeX/LSD)
        /// </summary>
        [ObservableProperty]
        public partial bool? IsPrivate { get; set; }

        /// <summary>
        /// Size in bytes
        /// </summary>
        public ulong Size {
            get;
            set {
                field = value;
                SizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string SizeDisplay { get; set; }

        /// <summary>
        /// Size we want to download (in case of 0 priority files)
        /// </summary>
        [ObservableProperty]
        public partial ulong WantedSize { get; set; }

        /// <summary>
        /// Piece size
        /// </summary>
        public ulong? PieceSize { get; set; }

        /// <summary>
        /// Wasted bytes
        /// </summary>
        [ObservableProperty]
        public partial ulong? Wasted { get; set; }

        /// <summary>
        /// Done percentage
        /// </summary>
        [ObservableProperty]
        public partial float Done { get; set; }

        /// <summary>
        /// Downloaded bytes
        /// </summary>
        public ulong Downloaded {
            get;
            set {
                field = value;
                DownloadedDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string DownloadedDisplay { get; set; }

        /// <summary>
        /// Completed bytes
        /// </summary>
        public ulong CompletedSize {
            get;
            set {
                field = value;
                CompletedSizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string CompletedSizeDisplay { get; set; }

        /// <summary>
        /// Uploaded bytes
        /// </summary>
        public ulong Uploaded {
            get;
            set {
                field = value;
                UploadedDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string UploadedDisplay { get; set; }

        /// <summary>
        /// Share ratio
        /// </summary>
        public float Ratio {
            get;
            set {
                field = value;
                RatioDisplay = value.ToString("N3");
            }
        }

        [ObservableProperty]
        public partial string RatioDisplay { get; set; }

        /// <summary>
        /// Download speed, B/s
        /// </summary>
        public ulong DLSpeed {
            get;
            set {
                field = value;
                DLSpeedDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string DLSpeedDisplay { get; set; }

        /// <summary>
        /// Upload speed, B/S
        /// </summary>
        public ulong UPSpeed {
            get;
            set {
                field = value;
                UPSpeedDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string UPSpeedDisplay { get; set; }

        /// <summary>
        /// ETA, <c>0</c> if already at 100%
        /// </summary>
        /// <seealso cref="Done"/>
        public TimeSpan ETA {
            get;
            set {
                field = value;
                ETADisplay = Converters.ToAgoString(value);
            }
        }

        [ObservableProperty]
        public partial string ETADisplay { get; set; }

        /// <summary>
        /// Torrent label
        /// </summary>
        public string[] Labels {
            get;
            set {
                field = value;
                LabelsDisplay = String.Join(", ", value);
            }
        }

        [ObservableProperty]
        public partial string LabelsDisplay { get; set; }

        /// <summary>
        /// Torrent peers. Connected, Total
        /// </summary>
        public ConnectedTotalPair Peers {
            get;
            set {
                field = value;
                PeersDisplay = value.ToString();
            }
        }

        [ObservableProperty]
        public partial string PeersDisplay { get; set; }

        /// <summary>
        /// Torrent seeders. Connected, Total
        /// </summary>
        public ConnectedTotalPair Seeders {
            get;
            set {
                field = value;
                SeedersDisplay = value.ToString();
            }
        }

        [ObservableProperty]
        public partial string SeedersDisplay { get; set; }

        private TORRENT_PRIORITY InternalPriority;

        /// <summary>
        /// Torrent priority
        /// </summary>
        [ObservableProperty]
        public partial string Priority { get; set; }

        /// <summary>
        /// Unix timestamp of when .torrent file was created
        /// </summary>
        public DateTime? CreatedOnDate {
            get;
            set {
                field = value;
                CreatedOnDateDisplay = value == null ? "" : value.Value.ToString();
            }
        }

        public string CreatedOnDateDisplay;

        /// <summary>
        /// Remaining size to download
        /// </summary>
        /// <seealso cref="Size"/>
        /// <seealso cref="Downloaded"/>
        public ulong RemainingSize {
            get;
            set {
                field = value;
                RemainingSizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        [ObservableProperty]
        public partial string RemainingSizeDisplay { get; set; }

        /// <summary>
        /// Date when torrent finished downloading
        /// </summary>
        public DateTime? FinishedOnDate {
            get;
            set {
                field = value;
                FinishedOnDateDisplay = value == null ? "" : value.Value.ToString();
            }
        }

        [ObservableProperty]
        public partial string FinishedOnDateDisplay { get; set; }

        /// <summary>
        /// Time elapsed when downloading torrent.
        /// This can be provider specific, for example elapsed time can be counted from the moment torrent connects to a seeder, or from a moment it was added.
        /// </summary>
        [ObservableProperty]
        public partial TimeSpan TimeElapsed { get; set; }

        /// <summary>
        /// Unix timestamp of when torrent was added
        /// </summary>
        public DateTime AddedOnDate {
            get;
            set {
                field = value;
                AddedOnDateDisplay = value.ToString();
            }
        }

        public string AddedOnDateDisplay;

        /// <summary>
        /// Primary tracker URI
        /// </summary>
        [ObservableProperty]
        public partial string? TrackerSingle { get; set; }

        [ObservableProperty]
        public partial IImage? TrackerIcon { get; set; }

        [ObservableProperty]
        public partial string? TrackerDisplayName { get; set; }

        /// <summary>
        /// Status message
        /// </summary>
        [ObservableProperty]
        public partial string StatusMsg { get; set; }

        /// <summary>
        /// Torrent comment
        /// </summary>
        public string Comment { get; set; }

        /// <summary>
        /// Remote path of torrent data
        /// </summary>
        [ObservableProperty]
        public partial string RemotePath { get; set; }

        /// <summary>
        /// Is torrent a magnet link dummy waiting to be resolved?
        /// </summary>
        [ObservableProperty]
        public partial bool MagnetDummy { get; set; }

        public Torrent(byte[] Hash, Plugin.RTSharpDataProvider Owner)
        {
            this.Hash = Hash;
            this.DataOwner = Owner;
        }

        public async ValueTask UpdateFromPluginModel(Shared.Abstractions.Torrent In, bool updateTracker = true)
        {
            this.Name = In.Name;

            this.State = EnumExt.ToString(In.State);
            this.InternalState = In.State;

            this.Size = In.Size;
            this.WantedSize = In.WantedSize;
            this.PieceSize = In.PieceSize;
            this.Wasted = In.Wasted;
            this.Done = In.Done;
            this.Downloaded = In.Downloaded;
            this.CompletedSize = In.CompletedSize;
            this.Uploaded = In.Uploaded;
            this.Ratio = (float)In.Uploaded / In.Downloaded;
            if (In.Uploaded == 0)
                this.Ratio = 0;
            this.DLSpeed = In.DLSpeed;
            this.UPSpeed = In.UPSpeed;
            this.ETA = In.ETA;
            this.Labels = In.Labels.ToArray();
            this.Peers = new ConnectedTotalPair(In.Peers.Connected, In.Peers.Total);
            this.Seeders = new ConnectedTotalPair(In.Seeders.Connected, In.Seeders.Total);

            this.Priority = EnumExt.ToString(In.Priority);

            this.InternalPriority = In.Priority;

            if (updateTracker && this.TrackerSingle != In.TrackerSingle) {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                var trackerInfo = await trackerDb.GetTrackerInfo(UriUtils.GetDomainForTracker(In.TrackerSingle));
                if (trackerInfo != null) {
                    if (trackerInfo.ImageHash != null) {
                        var image = await imageCache.GetCachedImage(trackerInfo.ImageHash);
                        if (image != null)
                            this.TrackerIcon = image;
                    }
                    this.TrackerDisplayName = trackerInfo.Name ?? UriUtils.GetDomainForTracker(In.TrackerSingle);
                } else
                    this.TrackerDisplayName = UriUtils.GetDomainForTracker(In.TrackerSingle);
            }

            this.CreatedOnDate = In.CreatedOnDate;
            this.RemainingSize = In.RemainingSize;
            this.FinishedOnDate = In.FinishedOnDate;
            this.TimeElapsed = In.TimeElapsed;
            this.AddedOnDate = In.AddedOnDate;
            this.TrackerSingle = In.TrackerSingle;
            this.StatusMsg = In.StatusMessage;
            this.Comment = In.Comment;
            this.RemotePath = In.RemotePath;
            this.MagnetDummy = In.MagnetDummy;
        }

        public Shared.Abstractions.Torrent ToPluginModel()
        {
            return new Shared.Abstractions.Torrent(Hash) {
                Name = Name,
                DataOwner = DataOwner.Instance,
                State = InternalState,
                Size = Size,
                WantedSize = Size, // TODO: ??????????
                PieceSize = PieceSize,
                Wasted = Wasted,
                Done = (float)Downloaded / Size * 100, // Maybe get from server?
                Downloaded = Downloaded,
                CompletedSize = CompletedSize,
                Uploaded = Uploaded,
                DLSpeed = DLSpeed,
                UPSpeed = UPSpeed,
                ETA = ETA,
                Labels = [.. Labels],
                Peers = (Peers.Connected, Peers.Total),
                Seeders = (Seeders.Connected, Seeders.Total),
                Priority = InternalPriority,
                CreatedOnDate = CreatedOnDate,
                RemainingSize = Size - Downloaded,
                FinishedOnDate = FinishedOnDate,
                TimeElapsed = TimeElapsed,
                AddedOnDate = AddedOnDate,
                TrackerSingle = TrackerSingle,
                StatusMessage = StatusMsg,
                Comment = Comment,
                RemotePath = RemotePath,
                MagnetDummy = MagnetDummy,
                IsPrivate = null,
            };
        }
    }
}
