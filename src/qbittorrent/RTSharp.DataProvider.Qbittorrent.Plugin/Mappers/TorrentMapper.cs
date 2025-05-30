namespace RTSharp.DataProvider.Qbittorrent.Plugin.Mappers
{
    public class TorrentMapper
    {
        public static Shared.Abstractions.TORRENT_STATE MapFromExternal(QBittorrent.Client.TorrentState In)
        {
            return In switch {
                QBittorrent.Client.TorrentState.Unknown => Shared.Abstractions.TORRENT_STATE.NONE,
                QBittorrent.Client.TorrentState.Error => Shared.Abstractions.TORRENT_STATE.ERRORED | Shared.Abstractions.TORRENT_STATE.DOWNLOADING, // Is downloading always the case?
                QBittorrent.Client.TorrentState.PausedUpload => Shared.Abstractions.TORRENT_STATE.PAUSED,
                QBittorrent.Client.TorrentState.PausedDownload => Shared.Abstractions.TORRENT_STATE.PAUSED,
                QBittorrent.Client.TorrentState.QueuedUpload => Shared.Abstractions.TORRENT_STATE.QUEUED | Shared.Abstractions.TORRENT_STATE.SEEDING,
                QBittorrent.Client.TorrentState.QueuedDownload => Shared.Abstractions.TORRENT_STATE.QUEUED | Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.Uploading => Shared.Abstractions.TORRENT_STATE.SEEDING,
                QBittorrent.Client.TorrentState.StalledUpload => Shared.Abstractions.TORRENT_STATE.SEEDING,
                QBittorrent.Client.TorrentState.CheckingUpload => Shared.Abstractions.TORRENT_STATE.HASHING,
                QBittorrent.Client.TorrentState.CheckingDownload => Shared.Abstractions.TORRENT_STATE.HASHING,
                QBittorrent.Client.TorrentState.Downloading => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.StalledDownload => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.FetchingMetadata => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.ForcedFetchingMetadata => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.ForcedUpload => Shared.Abstractions.TORRENT_STATE.SEEDING,
                QBittorrent.Client.TorrentState.ForcedDownload => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                QBittorrent.Client.TorrentState.MissingFiles => Shared.Abstractions.TORRENT_STATE.ERRORED,
                QBittorrent.Client.TorrentState.Allocating => Shared.Abstractions.TORRENT_STATE.ALLOCATING,
                QBittorrent.Client.TorrentState.QueuedForChecking => Shared.Abstractions.TORRENT_STATE.HASHING,
                QBittorrent.Client.TorrentState.CheckingResumeData => Shared.Abstractions.TORRENT_STATE.HASHING | Shared.Abstractions.TORRENT_STATE.ACTIVE,
                QBittorrent.Client.TorrentState.Moving => Shared.Abstractions.TORRENT_STATE.ALLOCATING | Shared.Abstractions.TORRENT_STATE.HASHING, // ???
            };
        }

        public static Shared.Abstractions.TORRENT_PRIORITY MapFromExternalPriority(int In)
        {
            return In switch {
                //0 => Shared.Abstractions.TORRENT_PRIORITY.OFF,
                0 => Shared.Abstractions.TORRENT_PRIORITY.NORMAL,
                6 => Shared.Abstractions.TORRENT_PRIORITY.HIGH,
                7 => Shared.Abstractions.TORRENT_PRIORITY.HIGH, // TODO
                _ => Shared.Abstractions.TORRENT_PRIORITY.NA
            };
        }

        public static Shared.Abstractions.Torrent MapFromExternal(QBittorrent.Client.TorrentInfo In)
        {
            return new Shared.Abstractions.Torrent(Convert.FromHexString(In.Hash)) {
                Name = In.Name,
                State = MapFromExternal(In.State),
                IsPrivate = null, // fetch later
                Size = (ulong)In.TotalSize!,
                WantedSize = (ulong)In.Size,
                PieceSize = null, // fetch later
                Wasted = null, // fetch later
                Done = (float)In.Progress * 100,
                Downloaded = (ulong)In.Downloaded!,
                Uploaded = (ulong)In.Uploaded!,
                DLSpeed = (ulong)In.DownloadSpeed,
                UPSpeed = (ulong)In.UploadSpeed,
                ETA = In.EstimatedTime == null || In.EstimatedTime.Value == TimeSpan.FromDays(100) ? TimeSpan.MaxValue : In.EstimatedTime.Value,
                Labels = In.Tags.ToHashSet(),
                Peers = ((uint)In.ConnectedLeechers, (uint)In.TotalLeechers),
                Seeders = ((uint)In.ConnectedSeeds, (uint)In.TotalSeeds),
                Priority = MapFromExternalPriority(In.Priority),
                CreatedOnDate = null, // fetch later?? TODO:
                RemainingSize = (ulong)In.IncompletedSize!,
                FinishedOnDate = In.CompletionOn,
                TimeElapsed = In.ActiveTime == null ? TimeSpan.Zero : In.ActiveTime.Value,
                AddedOnDate = In.AddedOn == null ? DateTime.MinValue : In.AddedOn.Value,
                TrackerSingle = String.IsNullOrEmpty(In.CurrentTracker) ? null : In.CurrentTracker,
                StatusMessage = "", // TODO:?
                Comment = "", // TODO:?
                RemotePath = In.SavePath,
                MagnetDummy = In.MagnetUri != null
            };
        }

        public static void ApplyFromExternal(Shared.Abstractions.Torrent Stored, QBittorrent.Client.TorrentPartialInfo External)
        {
            // IsPrivate = ?
            // PieceSize = ?
            // Wasted = ?
            // CreatedOnDate = ?
            // StatusMessage = ?
            // Comment = ?
            if (External.Name != null) Stored.Name = External.Name;
            if (External.State != null) Stored.State = MapFromExternal(External.State.Value);
            if (External.TotalSize != null) Stored.Size = (ulong)External.TotalSize.Value;
            if (External.Size != null) Stored.WantedSize = (ulong)External.Size.Value;
            if (External.Progress != null) Stored.Done = (float)External.Progress.Value * 100;
            if (External.Downloaded != null) Stored.Downloaded = (ulong)External.Downloaded.Value;
            if (External.Uploaded != null) Stored.Uploaded = (ulong)External.Uploaded.Value;
            if (External.DownloadSpeed != null) Stored.DLSpeed = (ulong)External.DownloadSpeed.Value;
            if (External.UploadSpeed != null) Stored.UPSpeed = (ulong)External.UploadSpeed.Value;
            if (External.EstimatedTime != null) Stored.ETA = External.EstimatedTime.Value == TimeSpan.FromDays(100) ? TimeSpan.MaxValue : External.EstimatedTime.Value;
            if (External.Tags != null) Stored.Labels = External.Tags.ToHashSet();
            if (External.ConnectedLeechers != null) Stored.Peers = ((uint)External.ConnectedLeechers, Stored.Peers.Total);
            if (External.TotalLeechers != null) Stored.Peers = (Stored.Peers.Connected, (uint)External.TotalLeechers);
            if (External.ConnectedSeeds != null) Stored.Seeders = ((uint)External.ConnectedSeeds, Stored.Seeders.Total);
            if (External.TotalSeeds != null) Stored.Seeders = (Stored.Seeders.Connected, (uint)External.TotalSeeds);
            if (External.Priority != null) Stored.Priority = MapFromExternalPriority(External.Priority.Value);
            if (External.IncompletedSize != null) Stored.RemainingSize = (ulong)External.IncompletedSize.Value;
            if (External.CompletionOn != null) Stored.FinishedOnDate = External.CompletionOn.Value;
            if (External.ActiveTime != null) Stored.TimeElapsed = External.ActiveTime.Value;
            if (External.AddedOn != null) Stored.AddedOnDate = External.AddedOn.Value;
            if (External.CurrentTracker != null) Stored.TrackerSingle = String.IsNullOrEmpty(External.CurrentTracker) ? null : External.CurrentTracker;
            if (External.SavePath != null) Stored.RemotePath = External.SavePath;
            if (External.MagnetUri != null) Stored.MagnetDummy = External.MagnetUri != null;

            if (Stored.DLSpeed > 0 || Stored.UPSpeed > 0)
                Stored.State |= Shared.Abstractions.TORRENT_STATE.ACTIVE;
            else
                Stored.State &= ~Shared.Abstractions.TORRENT_STATE.ACTIVE;
        }

        public static void ApplyFromExternal(QBittorrent.Client.TorrentInfo Stored, QBittorrent.Client.TorrentPartialInfo External)
        {
            if (External.Name != null) Stored.Name = External.Name;
            if (External.MagnetUri != null) Stored.MagnetUri = External.MagnetUri;
            if (External.Size != null) Stored.Size = External.Size.Value;
            if (External.Progress != null) Stored.Progress = External.Progress.Value;
            if (External.DownloadSpeed != null) Stored.DownloadSpeed = (int)External.DownloadSpeed.Value;
            if (External.UploadSpeed != null) Stored.UploadSpeed = (int)External.UploadSpeed.Value;
            if (External.Priority != null) Stored.Priority = External.Priority.Value;
            if (External.ConnectedSeeds != null) Stored.ConnectedSeeds = External.ConnectedSeeds.Value;
            if (External.TotalSeeds != null) Stored.TotalSeeds = External.TotalSeeds.Value;
            if (External.ConnectedLeechers != null) Stored.ConnectedLeechers = External.ConnectedLeechers.Value;
            if (External.TotalLeechers != null) Stored.TotalLeechers = External.TotalLeechers.Value;
            if (External.Ratio != null) Stored.Ratio = External.Ratio.Value;
            if (External.EstimatedTime != null) Stored.EstimatedTime = External.EstimatedTime;
            if (External.State != null) Stored.State = External.State.Value;
            if (External.SequentialDownload != null) Stored.SequentialDownload = External.SequentialDownload.Value;
            if (External.FirstLastPiecePrioritized != null) Stored.FirstLastPiecePrioritized = External.FirstLastPiecePrioritized.Value;
            if (External.Category != null) Stored.Category = External.Category;
            if (External.Tags != null) Stored.Tags = External.Tags;
            if (External.SuperSeeding != null) Stored.SuperSeeding = External.SuperSeeding.Value;
            if (External.ForceStart != null) Stored.ForceStart = External.ForceStart.Value;
            if (External.SavePath != null) Stored.SavePath = External.SavePath;
            if (External.AddedOn != null) Stored.AddedOn = External.AddedOn;
            if (External.CompletionOn != null) Stored.CompletionOn = External.CompletionOn;
            if (External.CurrentTracker != null) Stored.CurrentTracker = External.CurrentTracker;
            if (External.DownloadLimit != null) Stored.DownloadLimit = External.DownloadLimit;
            if (External.UploadLimit != null) Stored.UploadLimit = External.UploadLimit;
            if (External.Downloaded != null) Stored.Downloaded = External.Downloaded;
            if (External.Uploaded != null) Stored.Uploaded = External.Uploaded;
            if (External.DownloadedInSession != null) Stored.DownloadedInSession = External.DownloadedInSession;
            if (External.UploadedInSession != null) Stored.UploadedInSession = External.UploadedInSession;
            if (External.IncompletedSize != null) Stored.IncompletedSize = External.IncompletedSize;
            if (External.CompletedSize != null) Stored.CompletedSize = External.CompletedSize;
            if (External.RatioLimit != null) Stored.RatioLimit = External.RatioLimit.Value;
            //if (External.SeedingTimeLimit != null) Stored.SeedingTimeLimit = External.SeedingTimeLimit; // TODO: ?
            if (External.LastSeenComplete != null) Stored.LastSeenComplete = External.LastSeenComplete;
            if (External.LastActivityTime != null) Stored.LastActivityTime = External.LastActivityTime;
            if (External.ActiveTime != null) Stored.ActiveTime = External.ActiveTime;
            if (External.AutomaticTorrentManagement != null) Stored.AutomaticTorrentManagement = External.AutomaticTorrentManagement.Value;
            if (External.TotalSize != null) Stored.TotalSize = External.TotalSize;
            if (External.AdditionalData != null) Stored.AdditionalData = External.AdditionalData;
        }

        public static Shared.Abstractions.File.PRIORITY MapFromExternal(QBittorrent.Client.TorrentContentPriority In)
        {
            return In switch {
                QBittorrent.Client.TorrentContentPriority.Skip => Shared.Abstractions.File.PRIORITY.DONT_DOWNLOAD,
                QBittorrent.Client.TorrentContentPriority.Minimal => Shared.Abstractions.File.PRIORITY.NORMAL, // TODO: missing
                QBittorrent.Client.TorrentContentPriority.VeryLow => Shared.Abstractions.File.PRIORITY.NORMAL, // TODO: missing
                QBittorrent.Client.TorrentContentPriority.Low => Shared.Abstractions.File.PRIORITY.NORMAL, // TODO: missing
                QBittorrent.Client.TorrentContentPriority.Normal => Shared.Abstractions.File.PRIORITY.NORMAL,
                QBittorrent.Client.TorrentContentPriority.High => Shared.Abstractions.File.PRIORITY.HIGH,
                QBittorrent.Client.TorrentContentPriority.VeryHigh => Shared.Abstractions.File.PRIORITY.HIGH, // TODO: missing
                QBittorrent.Client.TorrentContentPriority.Maximal => Shared.Abstractions.File.PRIORITY.HIGH, // TODO: missing
                _ => Shared.Abstractions.File.PRIORITY.NA
            };
        }

        public static Shared.Abstractions.File MapFromExternal(QBittorrent.Client.TorrentContent In)
        {
            return new Shared.Abstractions.File {
                Path = In.Name,
                Size = (ulong)In.Size,
                DownloadedPieces = (ulong)(In.PieceRange.EndIndex - In.PieceRange.StartIndex),
                Downloaded = (ulong)(In.Progress * In.Size), // Pretty bad but should work
                Priority = MapFromExternal(In.Priority),
                DownloadStrategy = Shared.Abstractions.File.DOWNLOAD_STRATEGY.NORMAL // TODO: should be available from qbit?
            };
        }

        public static Shared.Abstractions.Peer MapFromExternal(string PeerId, QBittorrent.Client.PeerPartialInfo In, ulong PeerDLSpeed)
        {
            Shared.Abstractions.Peer.PEER_FLAGS mapFlag(char Flag)
            {
                return Flag switch {
                    'I' => Shared.Abstractions.Peer.PEER_FLAGS.I_INCOMING,
                    'E' => Shared.Abstractions.Peer.PEER_FLAGS.E_ENCRYPTED,
                    '?' => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED, // ???
                    'u' => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED, // ???
                    'S' => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED, // ???
                    _ => 0
                    // TODO: others that do not map...
                };
            }

            var flagArr = In.Flags.Where(x => !Char.IsWhiteSpace(x)).Select(mapFlag);
            Shared.Abstractions.Peer.PEER_FLAGS flags = 0;

            if (flagArr.Any()) {
                flags = flagArr.Aggregate((a, b) => a | b);
            }

            flags |= (In.Relevance == 0 ? Shared.Abstractions.Peer.PEER_FLAGS.U_UNWANTED : 0);

            return new Shared.Abstractions.Peer {
                PeerId = PeerId,
                IPPort = new System.Net.IPEndPoint(In.Address, In.Port ?? 0),
                Client = In.Client,
                Flags = flags,
                Done = (float)(In.Progress * 100 ?? 0),
                Downloaded = (ulong)(In.Downloaded ?? 0),
                Uploaded = (ulong)(In.Uploaded ?? 0),
                DLSpeed = (ulong)(In.DownloadSpeed ?? 0),
                UPSpeed = (ulong)(In.UploadSpeed ?? 0),
                PeerDLSpeed = PeerDLSpeed
            };
        }

        public static Shared.Abstractions.Tracker.TRACKER_STATUS MapFromExternal(QBittorrent.Client.TorrentTrackerStatus In)
        {
            return In switch {
                QBittorrent.Client.TorrentTrackerStatus.Disabled => Shared.Abstractions.Tracker.TRACKER_STATUS.DISABLED,
                QBittorrent.Client.TorrentTrackerStatus.NotContacted => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                QBittorrent.Client.TorrentTrackerStatus.Working => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED | Shared.Abstractions.Tracker.TRACKER_STATUS.ACTIVE,
                QBittorrent.Client.TorrentTrackerStatus.Updating => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED,
                QBittorrent.Client.TorrentTrackerStatus.NotWorking => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED | Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_ACTIVE,
            };
        }

        public static Shared.Abstractions.Tracker MapFromExternal(QBittorrent.Client.TorrentTracker In)
        {
            return new Shared.Abstractions.Tracker {
                ID = In.Url.OriginalString,
                Uri = In.Url.ToString(),
                Status = MapFromExternal(In.TrackerStatus.Value),
                Seeders = (uint)(In.Seeds ?? 0),
                Peers = (uint)(In.Peers ?? 0),
                // Where do leeches come in?
                Downloaded = 0, // TODO: feature flag?
                LastUpdated = DateTime.UnixEpoch,
                Interval = TimeSpan.Zero, // TODO: feature flag?
                StatusMsg = In.Message
            };
        }
    }
}
