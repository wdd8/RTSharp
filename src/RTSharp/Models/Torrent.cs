using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Database.TrackerDb;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Models
{
    public static class TorrentUpdateExt
    {
        public static async ValueTask<List<Torrent>> NewOrUpdateFromPluginModelMulti(Core.TorrentPolling.TorrentStore Domain, RTSharpDataProvider DataProvider, InfoHashDictionary<Shared.Abstractions.Torrent> Plugin)
        {
            var ret = new List<Torrent>();
            if (Plugin.Count == 0)
                return [];

            var dpId = DataProvider.PluginInstance.InstanceId;

            var trackers = new HashSet<string>();
            foreach (var (hash, plugin) in Plugin) {
                Domain.TryGet(new GlobalTorrentKey(hash, dpId), out var domain);
                if (domain?.TrackerSingle != plugin.TrackerSingle && plugin.TrackerSingle != null) {
                    trackers.Add(plugin.TrackerSingle);
                }
            }

            IEnumerable<TrackerInfo> trackerInfo = [];
            Dictionary<TrackerInfo, Bitmap> images = [];

            if (trackers.Count != 0) {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                trackerInfo = await trackerDb.GetTrackerInfo(trackers.Select(UriUtils.GetDomainForTracker));
                var imagesTasks = trackerInfo.Where(x => x.ImageHash != null).ToDictionary(x => x, x => imageCache.GetCachedImage(x.ImageHash!).AsTask());
                await Task.WhenAll(imagesTasks.Values.ToArray());

                images = imagesTasks.ToDictionary(x => x.Key, x => x.Value.Result!);
            }

            foreach (var (hash, plugin) in Plugin) {
                if (!Domain.TryGet(new GlobalTorrentKey(hash, dpId), out var torrent)) {
                    torrent = new Torrent(hash, DataProvider) {
                        Comment = ""
                    };
                }

                if (plugin.TrackerSingle != torrent.TrackerSingle && plugin.TrackerSingle != null) {
                    var domain = UriUtils.GetDomainForTracker(plugin.TrackerSingle);
                    var info = trackerInfo.FirstOrDefault(x => x.Domain == domain);

                    if (info != null) {
                        images.TryGetValue(info, out var image);
                        torrent.PendingTrackerEdit(image, info.Name ?? domain);
                    } else {
                        torrent.PendingTrackerEdit(null, domain);
                    }
                }

                await torrent.PendingEdit(plugin, false);
                ret.Add(torrent);
            }

            return ret;
        }
    }

    public partial class Torrent : ObservableObject
    {
        /// <summary>
        /// Torrent info hash
        /// </summary>
        public byte[] Hash { get; set; }

        public Plugin.RTSharpDataProvider DataOwner { get; set; }

        /// <summary>
        /// Position in <see cref="Core.TorrentPolling.TorrentStore.VisibleItems"/>, or -1 if not currently visible.
        /// <see cref="Core.TorrentPolling.TorrentStore" /> usage only
        /// </summary>
        internal int VisibleIndex = -1;

        /// <summary>
        /// Torrent name
        /// </summary>
        private string _name = null!;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private TORRENT_STATE _internalState;
        public TORRENT_STATE InternalState
        {
            get => _internalState;
            set => SetProperty(ref _internalState, value);
        }

        /// <summary>
        /// State
        /// </summary>
        private string _state = null!;
        public string State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        /// <summary>
        /// Is torrent private? (no DHT/PeX/LSD)
        /// </summary>
        private bool? _isPrivate;
        public bool? IsPrivate
        {
            get => _isPrivate;
            set => SetProperty(ref _isPrivate, value);
        }

        /// <summary>
        /// Size in bytes
        /// </summary>
        private ulong _size;
        public ulong Size
        {
            get => _size;
            set {
                if (_size == value) return;
                _size = value;
                SizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        private string _sizeDisplay = null!;
        public string SizeDisplay
        {
            get => _sizeDisplay;
            set => SetProperty(ref _sizeDisplay, value);
        }

        /// <summary>
        /// Size we want to download (in case of 0 priority files)
        /// </summary>
        private ulong _wantedSize;
        public ulong WantedSize
        {
            get => _wantedSize;
            set => SetProperty(ref _wantedSize, value);
        }

        /// <summary>
        /// Piece size
        /// </summary>
        public ulong? PieceSize { get; set; }

        /// <summary>
        /// Wasted bytes
        /// </summary>
        private ulong? _wasted;
        public ulong? Wasted
        {
            get => _wasted;
            set => SetProperty(ref _wasted, value);
        }

        /// <summary>
        /// Done percentage
        /// </summary>
        private float _done;
        public float Done
        {
            get => _done;
            set => SetProperty(ref _done, value);
        }

        /// <summary>
        /// Downloaded bytes
        /// </summary>
        private ulong _downloaded;
        public ulong Downloaded
        {
            get => _downloaded;
            set {
                if (_downloaded == value) return;
                _downloaded = value;
                DownloadedDisplay = Converters.GetSIDataSize(value);
            }
        }

        private string _downloadedDisplay = null!;
        public string DownloadedDisplay
        {
            get => _downloadedDisplay;
            set => SetProperty(ref _downloadedDisplay, value);
        }

        /// <summary>
        /// Completed bytes
        /// </summary>
        private ulong _completedSize;
        public ulong CompletedSize
        {
            get => _completedSize;
            set {
                if (_completedSize == value) return;
                _completedSize = value;
                CompletedSizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        private string _completedSizeDisplay = null!;
        public string CompletedSizeDisplay
        {
            get => _completedSizeDisplay;
            set => SetProperty(ref _completedSizeDisplay, value);
        }

        /// <summary>
        /// Uploaded bytes
        /// </summary>
        private ulong _uploaded;
        public ulong Uploaded
        {
            get => _uploaded;
            set {
                if (_uploaded == value) return;
                _uploaded = value;
                UploadedDisplay = Converters.GetSIDataSize(value);
            }
        }

        private string _uploadedDisplay = null!;
        public string UploadedDisplay
        {
            get => _uploadedDisplay;
            set => SetProperty(ref _uploadedDisplay, value);
        }

        /// <summary>
        /// Share ratio
        /// </summary>
        private float _ratio;
        public float Ratio
        {
            get => _ratio;
            set {
                if (_ratio == value) return;
                _ratio = value;
                RatioDisplay = value.ToString("N3");
            }
        }

        private string _ratioDisplay = null!;
        public string RatioDisplay
        {
            get => _ratioDisplay;
            set => SetProperty(ref _ratioDisplay, value);
        }

        /// <summary>
        /// Download speed, B/s
        /// </summary>
        private ulong _dlSpeed;
        public ulong DLSpeed
        {
            get => _dlSpeed;
            set {
                if (_dlSpeed == value) return;
                _dlSpeed = value;
                DLSpeedDisplay = Converters.GetSIDataSpeed(value);
            }
        }

        private string _dlSpeedDisplay = null!;
        public string DLSpeedDisplay
        {
            get => _dlSpeedDisplay;
            set => SetProperty(ref _dlSpeedDisplay, value);
        }

        /// <summary>
        /// Upload speed, B/s
        /// </summary>
        private ulong _upSpeed;
        public ulong UPSpeed
        {
            get => _upSpeed;
            set {
                if (_upSpeed == value) return;
                _upSpeed = value;
                UPSpeedDisplay = Converters.GetSIDataSpeed(value);
            }
        }

        private string _upSpeedDisplay = null!;
        public string UPSpeedDisplay
        {
            get => _upSpeedDisplay;
            set => SetProperty(ref _upSpeedDisplay, value);
        }

        /// <summary>
        /// ETA, <c>0</c> if already at 100%
        /// </summary>
        /// <seealso cref="Done"/>
        private TimeSpan _eta;
        public TimeSpan ETA
        {
            get => _eta;
            set {
                if (_eta == value) return;
                _eta = value;
                ETADisplay = Converters.ToAgoString(value);
            }
        }

        private string _etaDisplay = null!;
        public string ETADisplay
        {
            get => _etaDisplay;
            set => SetProperty(ref _etaDisplay, value);
        }

        /// <summary>
        /// Torrent label
        /// </summary>
        private string[] _labels = [];
        public string[] Labels
        {
            get => _labels;
            private set {
                _labels = value;
                LabelsDisplay = String.Join(", ", value);
            }
        }

        public void SetLabels(string[] labels)
        {
            Labels = labels;
            OnPropertyChanged(nameof(Labels));
        }

        private string _labelsDisplay = null!;
        public string LabelsDisplay
        {
            get => _labelsDisplay;
            set => SetProperty(ref _labelsDisplay, value);
        }

        /// <summary>
        /// Torrent peers. Connected, Total
        /// </summary>
        private ConnectedTotalPair _peers;
        public ConnectedTotalPair Peers
        {
            get => _peers;
            set {
                if (_peers == value) return;
                _peers = value;
                PeersDisplay = value.ToString();
            }
        }

        private string _peersDisplay = null!;
        public string PeersDisplay
        {
            get => _peersDisplay;
            set => SetProperty(ref _peersDisplay, value);
        }

        /// <summary>
        /// Torrent seeders. Connected, Total
        /// </summary>
        private ConnectedTotalPair _seeders;
        public ConnectedTotalPair Seeders
        {
            get => _seeders;
            set {
                if (_seeders == value) return;
                _seeders = value;
                SeedersDisplay = value.ToString();
            }
        }

        private string _seedersDisplay = null!;
        public string SeedersDisplay
        {
            get => _seedersDisplay;
            set => SetProperty(ref _seedersDisplay, value);
        }

        private TORRENT_PRIORITY InternalPriority;

        /// <summary>
        /// Torrent priority
        /// </summary>
        private string _priority = null!;
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        /// <summary>
        /// Unix timestamp of when .torrent file was created
        /// </summary>
        private DateTime? _createdOnDate;
        public DateTime? CreatedOnDate
        {
            get => _createdOnDate;
            set {
                if (_createdOnDate == value) return;
                _createdOnDate = value;
                CreatedOnDateDisplay = value == null ? "" : value.Value.ToString();
            }
        }

        private string _createdOnDateDisplay = null!;
        public string CreatedOnDateDisplay
        {
            get => _createdOnDateDisplay;
            set => SetProperty(ref _createdOnDateDisplay, value);
        }

        /// <summary>
        /// Remaining size to download
        /// </summary>
        /// <seealso cref="Size"/>
        /// <seealso cref="Downloaded"/>
        private ulong _remainingSize;
        public ulong RemainingSize
        {
            get => _remainingSize;
            set {
                if (_remainingSize == value) return;
                _remainingSize = value;
                RemainingSizeDisplay = Converters.GetSIDataSize(value);
            }
        }

        private string _remainingSizeDisplay = null!;
        public string RemainingSizeDisplay
        {
            get => _remainingSizeDisplay;
            set => SetProperty(ref _remainingSizeDisplay, value);
        }

        /// <summary>
        /// Date when torrent finished downloading
        /// </summary>
        private DateTime? _finishedOnDate;
        public DateTime? FinishedOnDate
        {
            get => _finishedOnDate;
            set {
                if (_finishedOnDate == value) return;
                _finishedOnDate = value;
                FinishedOnDateDisplay = value == null ? "" : value.Value.ToString();
            }
        }

        private string _finishedOnDateDisplay = null!;
        public string FinishedOnDateDisplay
        {
            get => _finishedOnDateDisplay;
            set => SetProperty(ref _finishedOnDateDisplay, value);
        }

        /// <summary>
        /// Time elapsed when downloading torrent.
        /// This can be provider specific, for example elapsed time can be counted from the moment torrent connects to a seeder, or from a moment it was added.
        /// </summary>
        private TimeSpan _timeElapsed;
        public TimeSpan TimeElapsed
        {
            get => _timeElapsed;
            set => SetProperty(ref _timeElapsed, value);
        }

        /// <summary>
        /// Unix timestamp of when torrent was added
        /// </summary>
        private DateTime _addedOnDate;
        public DateTime AddedOnDate
        {
            get => _addedOnDate;
            set {
                if (_addedOnDate == value) return;
                _addedOnDate = value;
                AddedOnDateDisplay = value.ToString();
            }
        }

        private string _addedOnDateDisplay = null!;
        public string AddedOnDateDisplay
        {
            get => _addedOnDateDisplay;
            set => SetProperty(ref _addedOnDateDisplay, value);
        }

        /// <summary>
        /// Primary tracker URI
        /// </summary>
        private string? _trackerSingle;
        public string? TrackerSingle
        {
            get => _trackerSingle;
            set => SetProperty(ref _trackerSingle, value);
        }

        private IImage? _trackerIcon;
        public IImage? TrackerIcon
        {
            get => _trackerIcon;
            set => SetProperty(ref _trackerIcon, value);
        }

        private string? _trackerDisplayName;
        public string? TrackerDisplayName
        {
            get => _trackerDisplayName;
            set => SetProperty(ref _trackerDisplayName, value);
        }

        /// <summary>
        /// Status message
        /// </summary>
        private string _statusMsg = null!;
        public string StatusMsg
        {
            get => _statusMsg;
            set => SetProperty(ref _statusMsg, value);
        }

        /// <summary>
        /// Torrent comment
        /// </summary>
        public required string Comment { get; set; }

        /// <summary>
        /// Remote path of torrent data
        /// </summary>
        private string _remotePath = null!;
        public string RemotePath
        {
            get => _remotePath;
            set => SetProperty(ref _remotePath, value);
        }

        /// <summary>
        /// Is torrent a magnet link dummy waiting to be resolved?
        /// </summary>
        private bool _magnetDummy;
        public bool MagnetDummy
        {
            get => _magnetDummy;
            set => SetProperty(ref _magnetDummy, value);
        }

        private Shared.Abstractions.Torrent? PendingChanges;
        private (IImage? Icon, string? DisplayName)? ResolvedTracker;

        public Torrent(byte[] Hash, Plugin.RTSharpDataProvider Owner)
        {
            this.Hash = Hash;
            this.DataOwner = Owner;

            _sizeDisplay = Converters.ZeroBytes;
            _downloadedDisplay = Converters.ZeroBytes;
            _completedSizeDisplay = Converters.ZeroBytes;
            _uploadedDisplay = Converters.ZeroBytes;
            _ratioDisplay = "0.000";
            _dlSpeedDisplay = Converters.ZeroBytesPerSec;
            _upSpeedDisplay = Converters.ZeroBytesPerSec;
            _etaDisplay = Converters.ToAgoString(TimeSpan.Zero);
            _peersDisplay = new ConnectedTotalPair(0, 0).ToString();
            _seedersDisplay = new ConnectedTotalPair(0, 0).ToString();
            _createdOnDateDisplay = "";
            _remainingSizeDisplay = Converters.ZeroBytes;
            _finishedOnDateDisplay = "";
            _addedOnDateDisplay = "";
            _labelsDisplay = "";
            _name = "";
            _state = "";
            _priority = "";
            _statusMsg = "";
            _remotePath = "";
        }

        public ValueTask PendingEdit(Shared.Abstractions.Torrent In, bool updateTracker = true)
        {
            PendingChanges = In;

            async ValueTask resolveTracker()
            {
                using var scope = Core.ServiceProvider.CreateScope();
                var trackerDb = scope.ServiceProvider.GetRequiredService<TrackerDb>();
                var imageCache = scope.ServiceProvider.GetRequiredService<ImageCache>();

                var domain = UriUtils.GetDomainForTracker(In.TrackerSingle!);
                var trackerInfo = await trackerDb.GetTrackerInfo(domain);

                IImage? icon = null;
                string displayName;
                if (trackerInfo != null) {
                    if (trackerInfo.ImageHash != null)
                        icon = await imageCache.GetCachedImage(trackerInfo.ImageHash);
                    displayName = trackerInfo.Name ?? domain;
                } else
                    displayName = domain;

                PendingTrackerEdit(icon, displayName);
            }

            if (updateTracker && _trackerSingle != In.TrackerSingle && In.TrackerSingle != null)
                return resolveTracker();

            return ValueTask.CompletedTask;
        }

        public void PendingTrackerEdit(IImage? Icon, string? DisplayName)
        {
            ResolvedTracker = (Icon, DisplayName);
        }

        public void CommitUIChanges()
        {
            var trackerChanges = ResolvedTracker;
            ResolvedTracker = null;
            var changes = PendingChanges;
            PendingChanges = null;

            if (trackerChanges != null) {
                if (trackerChanges.Value.Icon != null)
                    TrackerIcon = trackerChanges.Value.Icon;
                TrackerDisplayName = trackerChanges.Value.DisplayName;
            }

            if (changes == null)
                return;

            if (_name != changes.Name) {
                Name = changes.Name;
            }
            if (_internalState != changes.State) {
                State = EnumExt.ToString(changes.State);
                _internalState = changes.State;
            }
            if (_size != changes.Size) {
                Size = changes.Size;
            }
            if (_wantedSize != changes.WantedSize) {
                WantedSize = changes.WantedSize;
            }
            if (PieceSize != changes.PieceSize) {
                PieceSize = changes.PieceSize;
            }
            if (_wasted != changes.Wasted) {
                Wasted = changes.Wasted;
            }
            if (_done != changes.Done) {
                Done = changes.Done;
            }
            if (_downloaded != changes.Downloaded) {
                Downloaded = changes.Downloaded;
            }
            if (_completedSize != changes.CompletedSize) {
                CompletedSize = changes.CompletedSize;
            }
            if (_uploaded != changes.Uploaded) {
                Uploaded = changes.Uploaded;
            }
            var ratio = (changes.Downloaded == 0 && changes.Uploaded == 0) ? 0f : (float)changes.Uploaded / changes.Downloaded;
            if (_ratio != ratio) {
                Ratio = ratio;
            }
            if (_dlSpeed != changes.DLSpeed) {
                DLSpeed = changes.DLSpeed;
            }
            if (_upSpeed != changes.UPSpeed) {
                UPSpeed = changes.UPSpeed;
            }
            if (_eta != changes.ETA) {
                ETA = changes.ETA;
            }
            if (changes.Labels.Count != _labels.Length || !changes.Labels.SetEquals(_labels)) {
                Labels = [.. changes.Labels];
            }
            var peers = new ConnectedTotalPair(changes.Peers.Connected, changes.Peers.Total);
            if (_peers != peers) {
                Peers = peers;
            }
            var seeders = new ConnectedTotalPair(changes.Seeders.Connected, changes.Seeders.Total);
            if (_seeders != seeders) {
                Seeders = seeders;
            }
            if (InternalPriority != changes.Priority) {
                Priority = EnumExt.ToString(changes.Priority);
                InternalPriority = changes.Priority;
            }
            if (_createdOnDate != changes.CreatedOnDate) {
                CreatedOnDate = changes.CreatedOnDate;
            }
            if (_remainingSize != changes.RemainingSize) {
                RemainingSize = changes.RemainingSize;
            }
            if (_finishedOnDate != changes.FinishedOnDate) {
                FinishedOnDate = changes.FinishedOnDate;
            }
            if (_timeElapsed != changes.TimeElapsed) {
                TimeElapsed = changes.TimeElapsed;
            }
            if (_addedOnDate != changes.AddedOnDate) {
                AddedOnDate = changes.AddedOnDate;
            }
            if (_trackerSingle != changes.TrackerSingle) {
                TrackerSingle = changes.TrackerSingle;
            }
            if (_statusMsg != changes.StatusMessage) {
                StatusMsg = changes.StatusMessage;
            }
            if (Comment != changes.Comment) {
                Comment = changes.Comment;
            }
            if (_remotePath != changes.RemotePath) {
                RemotePath = changes.RemotePath;
            }
            if (_magnetDummy != changes.MagnetDummy) {
                MagnetDummy = changes.MagnetDummy;
            }
        }

        public Shared.Abstractions.Torrent ToPluginModel()
        {
            return new Shared.Abstractions.Torrent(Hash) {
                Name = _name,
                DataOwner = DataOwner.Instance,
                State = _internalState,
                Size = _size,
                WantedSize = _size, // TODO: ??????????
                PieceSize = PieceSize,
                Wasted = _wasted,
                Done = (float)_downloaded / _size * 100, // Maybe get from server?
                Downloaded = _downloaded,
                CompletedSize = _completedSize,
                Uploaded = _uploaded,
                DLSpeed = _dlSpeed,
                UPSpeed = _upSpeed,
                ETA = _eta,
                Labels = [.. _labels],
                Peers = (_peers.Connected, _peers.Total),
                Seeders = (_seeders.Connected, _seeders.Total),
                Priority = InternalPriority,
                CreatedOnDate = _createdOnDate,
                RemainingSize = _size - _downloaded,
                FinishedOnDate = _finishedOnDate,
                TimeElapsed = _timeElapsed,
                AddedOnDate = _addedOnDate,
                TrackerSingle = _trackerSingle!,
                StatusMessage = _statusMsg,
                Comment = Comment,
                RemotePath = _remotePath,
                MagnetDummy = _magnetDummy,
                IsPrivate = null,
            };
        }
    }
}
