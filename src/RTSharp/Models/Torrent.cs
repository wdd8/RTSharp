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

        public Plugin.DataProvider Owner { get; set; }

        /// <summary>
        /// Torrent name
        /// </summary>
        [ObservableProperty]
        public string name;

        [ObservableProperty]
        public TORRENT_STATE internalState;

        /// <summary>
        /// State
        /// </summary>
        [ObservableProperty]
        public string state;

        /// <summary>
        /// Is torrent private? (no DHT/PeX/LSD)
        /// </summary>
        [ObservableProperty]
        public bool? isPrivate;

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
        public string sizeDisplay;

        /// <summary>
        /// Size we want to download (in case of 0 priority files)
        /// </summary>
        [ObservableProperty]
        public ulong wantedSize;

        /// <summary>
        /// Piece size
        /// </summary>
        public ulong? PieceSize { get; set; }

        /// <summary>
        /// Wasted bytes
        /// </summary>
        [ObservableProperty]
        public ulong? wasted;

        /// <summary>
        /// Done percentage
        /// </summary>
        [ObservableProperty]
        public float done;

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
        public string downloadedDisplay;

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
        public string completedSizeDisplay;

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
        public string uploadedDisplay;

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
        public string ratioDisplay;

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
        public string dLSpeedDisplay;

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
        public string uPSpeedDisplay;

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
        public string eTADisplay;

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
        public string labelsDisplay;

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
        public string peersDisplay;

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
        public string seedersDisplay;

        private TORRENT_PRIORITY InternalPriority;

        /// <summary>
        /// Torrent priority
        /// </summary>
        [ObservableProperty]
        public string priority;

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
        public string remainingSizeDisplay;

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
        public string finishedOnDateDisplay;

        /// <summary>
        /// Time elapsed when downloading torrent.
        /// This can be provider specific, for example elapsed time can be counted from the moment torrent connects to a seeder, or from a moment it was added.
        /// </summary>
        [ObservableProperty]
        public TimeSpan timeElapsed;

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
        public string? trackerSingle;

        [ObservableProperty]
        public IImage? trackerIcon;

        [ObservableProperty]
        public string? trackerDisplayName;

        /// <summary>
        /// Status message
        /// </summary>
        [ObservableProperty]
        public string statusMsg;

        /// <summary>
        /// Torrent comment
        /// </summary>
        public string Comment { get; set; }

        /// <summary>
        /// Remote path of torrent data
        /// </summary>
        [ObservableProperty]
        public string remotePath;

        /// <summary>
        /// Is torrent a magnet link dummy waiting to be resolved?
        /// </summary>
        [ObservableProperty]
        public bool magnetDummy;

        public Torrent(byte[] Hash, Plugin.DataProvider Owner)
        {
            this.Hash = Hash;
            this.Owner = Owner;
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
                Owner = Owner.Instance,
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
                MagnetDummy = MagnetDummy
            };
        }
    }
}
