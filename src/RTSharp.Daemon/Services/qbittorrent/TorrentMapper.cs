using Google.Protobuf.WellKnownTypes;

using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.Services.qbittorrent
{
    public class TorrentMapper
    {
        public static Protocols.DataProvider.TorrentState MapFromExternal(QBittorrent.Client.TorrentState In)
        {
            return In switch {
                QBittorrent.Client.TorrentState.Unknown => Protocols.DataProvider.TorrentState.None,
                QBittorrent.Client.TorrentState.Error => Protocols.DataProvider.TorrentState.Errored | Protocols.DataProvider.TorrentState.Downloading, // Is downloading always the case?
                QBittorrent.Client.TorrentState.PausedUpload => Protocols.DataProvider.TorrentState.Paused,
                QBittorrent.Client.TorrentState.PausedDownload => Protocols.DataProvider.TorrentState.Paused,
                QBittorrent.Client.TorrentState.QueuedUpload => Protocols.DataProvider.TorrentState.Queued | Protocols.DataProvider.TorrentState.Seeding,
                QBittorrent.Client.TorrentState.QueuedDownload => Protocols.DataProvider.TorrentState.Queued | Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.Uploading => Protocols.DataProvider.TorrentState.Seeding,
                QBittorrent.Client.TorrentState.StalledUpload => Protocols.DataProvider.TorrentState.Seeding,
                QBittorrent.Client.TorrentState.CheckingUpload => Protocols.DataProvider.TorrentState.Hashing,
                QBittorrent.Client.TorrentState.CheckingDownload => Protocols.DataProvider.TorrentState.Hashing,
                QBittorrent.Client.TorrentState.Downloading => Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.StalledDownload => Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.FetchingMetadata => Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.ForcedFetchingMetadata => Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.ForcedUpload => Protocols.DataProvider.TorrentState.Seeding,
                QBittorrent.Client.TorrentState.ForcedDownload => Protocols.DataProvider.TorrentState.Downloading,
                QBittorrent.Client.TorrentState.MissingFiles => Protocols.DataProvider.TorrentState.Errored,
                QBittorrent.Client.TorrentState.Allocating => Protocols.DataProvider.TorrentState.Allocating,
                QBittorrent.Client.TorrentState.QueuedForChecking => Protocols.DataProvider.TorrentState.Hashing,
                QBittorrent.Client.TorrentState.CheckingResumeData => Protocols.DataProvider.TorrentState.Hashing | Protocols.DataProvider.TorrentState.Active,
                QBittorrent.Client.TorrentState.Moving => Protocols.DataProvider.TorrentState.Allocating | Protocols.DataProvider.TorrentState.Hashing, // ???
            };
        }

        public static Protocols.DataProvider.TorrentPriority MapFromExternalPriority(int In)
        {
            return In switch {
                //0 => Protocols.DataProvider.TorrentPriority.Off,
                0 => Protocols.DataProvider.TorrentPriority.Normal,
                6 => Protocols.DataProvider.TorrentPriority.High,
                7 => Protocols.DataProvider.TorrentPriority.High, // TODO: 
                _ => Protocols.DataProvider.TorrentPriority.Na
            };
        }

        public static void ApplyFromExternal(Protocols.DataProvider.Torrent Stored, QBittorrent.Client.TorrentPartialInfo External)
        {
            // IsPrivate = ?
            // PieceSize = ?
            // Wasted = ?
            // CreatedOnDate = ?
            // StatusMessage = ?
            // Comment = ?
            if (External.Name != null)
                Stored.Name = External.Name;
            if (External.State != null)
                Stored.State = MapFromExternal(External.State.Value);
            if (External.TotalSize != null)
                Stored.Size = (ulong)External.TotalSize.Value;
            if (External.Size != null)
                Stored.WantedSize = (ulong)External.Size.Value;
            if (External.Downloaded != null)
                Stored.Downloaded = (ulong)External.Downloaded.Value;
            if (External.Uploaded != null)
                Stored.Uploaded = (ulong)External.Uploaded.Value;
            if (External.DownloadSpeed != null)
                Stored.DLSpeed = (ulong)External.DownloadSpeed.Value;
            if (External.UploadSpeed != null)
                Stored.UPSpeed = (ulong)External.UploadSpeed.Value;
            if (External.Tags != null) {
                Stored.Labels.Clear();
                Stored.Labels.AddRange(External.Tags);
            }
            if (External.CompletedSize != null)
                Stored.CompletedSize = (ulong)External.CompletedSize.Value;
            if (External.ConnectedLeechers != null)
                Stored.PeersConnected = (uint)External.ConnectedLeechers;
            if (External.TotalLeechers != null)
                Stored.PeersTotal = (uint)External.TotalLeechers;
            if (External.ConnectedSeeds != null)
                Stored.SeedersConnected = (uint)External.ConnectedSeeds;
            if (External.TotalSeeds != null)
                Stored.SeedersTotal = (uint)External.TotalSeeds;
            if (External.Priority != null)
                Stored.Priority = MapFromExternalPriority(External.Priority.Value);
            if (External.CompletionOn != null)
                Stored.FinishedOn = Timestamp.FromDateTime(External.CompletionOn.Value.ToUniversalTime());
            if (External.AddedOn != null)
                Stored.AddedOn = Timestamp.FromDateTime(External.AddedOn.Value.ToUniversalTime());
            if (!String.IsNullOrEmpty(External.CurrentTracker)) {
                Stored.PrimaryTracker = new Protocols.DataProvider.TorrentTracker
                {
                    ID = External.CurrentTracker,
                    Uri = External.CurrentTracker
                };
            }
            if (External.SavePath != null)
                Stored.RemotePath = External.SavePath;
            if (External.MagnetUri != null)
                Stored.MagnetDummy = External.MagnetUri != null;

            if (Stored.DLSpeed > 1024 || Stored.UPSpeed > 1024)
                Stored.State |= Protocols.DataProvider.TorrentState.Active;
            else
                Stored.State &= ~Protocols.DataProvider.TorrentState.Active;
        }

        public static void ApplyFromExternal(QBittorrent.Client.TorrentInfo Stored, QBittorrent.Client.TorrentPartialInfo External)
        {
            if (External.Name != null)
                Stored.Name = External.Name;
            if (External.MagnetUri != null)
                Stored.MagnetUri = External.MagnetUri;
            if (External.Size != null)
                Stored.Size = External.Size.Value;
            if (External.Progress != null)
                Stored.Progress = External.Progress.Value;
            if (External.DownloadSpeed != null)
                Stored.DownloadSpeed = (int)External.DownloadSpeed.Value;
            if (External.UploadSpeed != null)
                Stored.UploadSpeed = (int)External.UploadSpeed.Value;
            if (External.Priority != null)
                Stored.Priority = External.Priority.Value;
            if (External.ConnectedSeeds != null)
                Stored.ConnectedSeeds = External.ConnectedSeeds.Value;
            if (External.TotalSeeds != null)
                Stored.TotalSeeds = External.TotalSeeds.Value;
            if (External.ConnectedLeechers != null)
                Stored.ConnectedLeechers = External.ConnectedLeechers.Value;
            if (External.TotalLeechers != null)
                Stored.TotalLeechers = External.TotalLeechers.Value;
            if (External.Ratio != null)
                Stored.Ratio = External.Ratio.Value;
            if (External.EstimatedTime != null)
                Stored.EstimatedTime = External.EstimatedTime;
            if (External.State != null)
                Stored.State = External.State.Value;
            if (External.SequentialDownload != null)
                Stored.SequentialDownload = External.SequentialDownload.Value;
            if (External.FirstLastPiecePrioritized != null)
                Stored.FirstLastPiecePrioritized = External.FirstLastPiecePrioritized.Value;
            if (External.Category != null)
                Stored.Category = External.Category;
            if (External.Tags != null)
                Stored.Tags = External.Tags;
            if (External.SuperSeeding != null)
                Stored.SuperSeeding = External.SuperSeeding.Value;
            if (External.ForceStart != null)
                Stored.ForceStart = External.ForceStart.Value;
            if (External.SavePath != null)
                Stored.SavePath = External.SavePath;
            if (External.AddedOn != null)
                Stored.AddedOn = External.AddedOn;
            if (External.CompletionOn != null)
                Stored.CompletionOn = External.CompletionOn;
            if (External.CurrentTracker != null)
                Stored.CurrentTracker = External.CurrentTracker;
            if (External.DownloadLimit != null)
                Stored.DownloadLimit = External.DownloadLimit;
            if (External.UploadLimit != null)
                Stored.UploadLimit = External.UploadLimit;
            if (External.Downloaded != null)
                Stored.Downloaded = External.Downloaded;
            if (External.Uploaded != null)
                Stored.Uploaded = External.Uploaded;
            if (External.DownloadedInSession != null)
                Stored.DownloadedInSession = External.DownloadedInSession;
            if (External.UploadedInSession != null)
                Stored.UploadedInSession = External.UploadedInSession;
            if (External.IncompletedSize != null)
                Stored.IncompletedSize = External.IncompletedSize;
            if (External.CompletedSize != null)
                Stored.CompletedSize = External.CompletedSize;
            if (External.RatioLimit != null)
                Stored.RatioLimit = External.RatioLimit.Value;
            //if (External.SeedingTimeLimit != null) Stored.SeedingTimeLimit = External.SeedingTimeLimit; // TODO: ?
            if (External.LastSeenComplete != null)
                Stored.LastSeenComplete = External.LastSeenComplete;
            if (External.LastActivityTime != null)
                Stored.LastActivityTime = External.LastActivityTime;
            if (External.ActiveTime != null)
                Stored.ActiveTime = External.ActiveTime;
            if (External.AutomaticTorrentManagement != null)
                Stored.AutomaticTorrentManagement = External.AutomaticTorrentManagement.Value;
            if (External.TotalSize != null)
                Stored.TotalSize = External.TotalSize;
            if (External.AdditionalData != null)
                Stored.AdditionalData = External.AdditionalData;
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

            if (flagArr.Any())
                flags = flagArr.Aggregate((a, b) => a | b);

            flags |= In.Relevance == 0 ? Shared.Abstractions.Peer.PEER_FLAGS.U_UNWANTED : 0;

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
