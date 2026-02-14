using Avalonia.Threading;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System;
using System.Linq;
using System.Net;

namespace RTSharp.Core.Services.Daemon
{
    public static class Mapper
    {
        public static Shared.Abstractions.TORRENT_STATE MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentState In)
        {
            return FlagsMapper.Map(In, x => x switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Downloading => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Seeding => Shared.Abstractions.TORRENT_STATE.SEEDING,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Hashing => Shared.Abstractions.TORRENT_STATE.HASHING,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Paused => Shared.Abstractions.TORRENT_STATE.PAUSED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Stopped => Shared.Abstractions.TORRENT_STATE.STOPPED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Complete => Shared.Abstractions.TORRENT_STATE.COMPLETE,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Active => Shared.Abstractions.TORRENT_STATE.ACTIVE,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Inactive => Shared.Abstractions.TORRENT_STATE.INACTIVE,
                RTSharp.Daemon.Protocols.DataProvider.TorrentState.Errored => Shared.Abstractions.TORRENT_STATE.ERRORED,
                _ => Shared.Abstractions.TORRENT_STATE.NONE
            });
        }

        public static Shared.Abstractions.TORRENT_PRIORITY MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentPriority In)
        {
            return In switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentPriority.Off => Shared.Abstractions.TORRENT_PRIORITY.OFF,
                RTSharp.Daemon.Protocols.DataProvider.TorrentPriority.Low => Shared.Abstractions.TORRENT_PRIORITY.LOW,
                RTSharp.Daemon.Protocols.DataProvider.TorrentPriority.Normal => Shared.Abstractions.TORRENT_PRIORITY.NORMAL,
                RTSharp.Daemon.Protocols.DataProvider.TorrentPriority.High => Shared.Abstractions.TORRENT_PRIORITY.HIGH,
                _ => Shared.Abstractions.TORRENT_PRIORITY.NA
            };
        }

        private static Shared.Abstractions.Tracker.TRACKER_STATUS MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus In)
        {
            return FlagsMapper.Map(In, x => x switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus.Active => Shared.Abstractions.Tracker.TRACKER_STATUS.ACTIVE,
                RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus.NotActive => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_ACTIVE,
                RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus.NotContactedYet => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus.Disabled => Shared.Abstractions.Tracker.TRACKER_STATUS.DISABLED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentTrackerStatus.Enabled => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED,
                _ => Shared.Abstractions.Tracker.TRACKER_STATUS.DISABLED
            });
        }

        public static void UpdateTracker(Shared.Abstractions.Tracker Bottom, RTSharp.Daemon.Protocols.DataProvider.TorrentTracker Top)
        {
            Bottom.ID = Top.Uri;
            Bottom.Uri = Top.Uri;
            Bottom.Status = MapFromProto(Top.Status);
            Bottom.Seeders = Top.Seeders;
            Bottom.Peers = Top.Peers;
            Bottom.Downloaded = Top.Downloaded;
            Bottom.LastUpdated = Top.LastUpdated.ToDateTime();
            Bottom.Interval = Top.ScrapeInterval.ToTimeSpan();
        }

        public static Shared.Abstractions.Tracker MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentTracker In)
        {
            var ret = new Shared.Abstractions.Tracker();
            UpdateTracker(ret, In);
            return ret;
        }

        public static Shared.Abstractions.Torrent MapFromProto(RTSharp.Daemon.Protocols.DataProvider.Torrent In, IDataProvider owner)
        {
            try {
                return new Shared.Abstractions.Torrent(In.Hash.ToByteArray()) {
                    Name = In.Name,
                    Owner = owner,
                    State = MapFromProto(In.State),
                    IsPrivate = In.IsPrivate,
                    Size = In.Size,
                    WantedSize = In.WantedSize,
                    PieceSize = In.ChunkSize,
                    Wasted = In.Wasted,
                    Done = (float)In.CompletedSize / In.WantedSize * 100,
                    Downloaded = In.Downloaded,
                    CompletedSize = In.CompletedSize,
                    Uploaded = In.Uploaded,
                    DLSpeed = In.DLSpeed,
                    UPSpeed = In.UPSpeed,
                    ETA = In.ETA == null ? TimeSpan.MaxValue : In.ETA.ToTimeSpan(),
                    Labels = In.Labels.Select(StringCache.Reuse).ToHashSet(),
                    Peers = (In.PeersConnected, In.PeersTotal),
                    Seeders = (In.SeedersConnected, In.SeedersTotal),
                    Priority = MapFromProto(In.Priority),
                    CreatedOnDate = In.CreatedOn?.ToDateTime(),
                    RemainingSize = In.WantedSize - In.CompletedSize,
                    FinishedOnDate = In.FinishedOn == null ? null : In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? null : In.FinishedOn.ToDateTime(),
                    TimeElapsed = (In.FinishedOn == null || In.FinishedOn.ToDateTime() == DateTime.UnixEpoch) ? DateTime.UtcNow - In.AddedOn.ToDateTime() : In.FinishedOn.ToDateTime() - In.AddedOn.ToDateTime(),
                    AddedOnDate = In.AddedOn.ToDateTime(),
                    TrackerSingle = In.PrimaryTracker == null ? null : In.PrimaryTracker.Uri,
                    StatusMessage = StringCache.Reuse(In.StatusMessage),
                    Comment = StringCache.Reuse(In.Comment),
                    RemotePath = StringCache.Reuse(In.RemotePath),
                    MagnetDummy = In.MagnetDummy
                };
            } catch {
                throw;
            }
        }

        public static Shared.Abstractions.Torrent MapFromProto(RTSharp.Daemon.Protocols.DataProvider.IncompleteDeltaTorrentResponse In, Models.Torrent Torrent)
        {
            return new Shared.Abstractions.Torrent(In.Hash.ToByteArray()) {
                Name = Torrent.Name,
                State = MapFromProto(In.State),
                IsPrivate = Torrent.IsPrivate,
                Size = Torrent.Size,
                WantedSize = Torrent.WantedSize,
                PieceSize = Torrent.PieceSize,
                Wasted = In.Wasted,
                Done = (float)In.CompletedSize / Torrent.WantedSize * 100,
                Downloaded = In.Downloaded,
                CompletedSize = In.CompletedSize,
                Uploaded = In.Uploaded,
                DLSpeed = In.DLSpeed,
                UPSpeed = In.UPSpeed,
                ETA = In.ETA == null ? TimeSpan.MaxValue : In.ETA.ToTimeSpan(),
                Labels = In.Labels.Select(StringCache.Reuse).ToHashSet(),
                Peers = (In.PeersConnected, In.PeersTotal),
                Seeders = (In.SeedersConnected, In.SeedersTotal),
                Priority = MapFromProto(In.Priority),
                CreatedOnDate = Torrent.CreatedOnDate,
                RemainingSize = Torrent.WantedSize - In.CompletedSize,
                FinishedOnDate = null,
                TimeElapsed = DateTime.UtcNow - Torrent.AddedOnDate,
                AddedOnDate = Torrent.AddedOnDate,
                TrackerSingle = In.PrimaryTracker != null ? In.PrimaryTracker.Uri : null,
                StatusMessage = StringCache.Reuse(In.StatusMessage),
                Comment = StringCache.Reuse(Torrent.Comment),
                RemotePath = StringCache.Reuse(In.RemotePath),
                MagnetDummy = In.MagnetDummy
            };
        }

        public static void ApplyFromProto(RTSharp.Daemon.Protocols.DataProvider.IncompleteDeltaTorrentResponse In, Models.Torrent Torrent)
        {
            Dispatcher.UIThread.Invoke(() => {
                var mappedState = MapFromProto(In.State);
                Torrent.State = EnumExt.ToString(mappedState);
                Torrent.InternalState = mappedState;
                Torrent.Wasted = In.Wasted;
                Torrent.Done = (float)In.CompletedSize / Torrent.WantedSize * 100;
                Torrent.Downloaded = In.Downloaded;
                Torrent.CompletedSize = In.CompletedSize;
                Torrent.Uploaded = In.Uploaded;
                Torrent.DLSpeed = In.DLSpeed;
                Torrent.UPSpeed = In.UPSpeed;
                Torrent.ETA = In.ETA == null ? TimeSpan.MaxValue : In.ETA.ToTimeSpan();
                Torrent.Labels = In.Labels.Select(StringCache.Reuse).ToArray();
                Torrent.Peers = new(In.PeersConnected, In.PeersTotal);
                Torrent.Seeders = new(In.SeedersConnected, In.SeedersTotal);
                Torrent.Priority = EnumExt.ToString(MapFromProto(In.Priority));
                Torrent.RemainingSize = Torrent.WantedSize - In.CompletedSize;
                Torrent.FinishedOnDate = null;
                Torrent.TimeElapsed = DateTime.UtcNow - Torrent.AddedOnDate;
                var oldTracker = Torrent.TrackerSingle;
                Torrent.TrackerSingle = In.PrimaryTracker != null ? In.PrimaryTracker.Uri : null;
                if (oldTracker != Torrent.TrackerSingle) {
                    Torrent.TrackerDisplayName = null;
                    Torrent.TrackerIcon = null;
                }
                Torrent.StatusMsg = StringCache.Reuse(In.StatusMessage);
                Torrent.RemotePath = StringCache.Reuse(In.RemotePath);
                Torrent.MagnetDummy = In.MagnetDummy;
            });
        }

        public static Shared.Abstractions.Torrent MapFromProto(RTSharp.Daemon.Protocols.DataProvider.CompleteDeltaTorrentResponse In, Models.Torrent Torrent)
        {
            return new Shared.Abstractions.Torrent(In.Hash.ToByteArray()) {
                Name = Torrent.Name,
                State = MapFromProto(In.State),
                IsPrivate = Torrent.IsPrivate,
                Size = Torrent.Size,
                WantedSize = In.WantedSize,
                PieceSize = Torrent.PieceSize,
                Wasted = Torrent.Wasted,
                Done = Torrent.Done,
                Downloaded = Torrent.Downloaded,
                CompletedSize = Torrent.CompletedSize,
                Uploaded = In.Uploaded,
                DLSpeed = Torrent.DLSpeed,
                UPSpeed = In.UPSpeed,
                ETA = Torrent.ETA,
                Labels = In.Labels.Select(StringCache.Reuse).ToHashSet(),
                Peers = (In.PeersConnected, In.PeersTotal),
                Seeders = (Torrent.Seeders.Connected, In.SeedersTotal),
                Priority = MapFromProto(In.Priority),
                CreatedOnDate = Torrent.CreatedOnDate,
                RemainingSize = Torrent.RemainingSize,
                FinishedOnDate = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? null : In.FinishedOn.ToDateTime(),
                TimeElapsed = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? TimeSpan.Zero : In.FinishedOn.ToDateTime() - Torrent.AddedOnDate,
                AddedOnDate = Torrent.AddedOnDate,
                TrackerSingle = In.PrimaryTracker != null ? In.PrimaryTracker.Uri : null,
                StatusMessage = StringCache.Reuse(In.StatusMessage),
                Comment = StringCache.Reuse(Torrent.Comment),
                RemotePath = StringCache.Reuse(In.RemotePath),
                MagnetDummy = Torrent.MagnetDummy
            };
        }

        public static void ApplyFromProto(RTSharp.Daemon.Protocols.DataProvider.CompleteDeltaTorrentResponse In, Models.Torrent Torrent)
        {
            Dispatcher.UIThread.Invoke(() => {
                var mappedState = MapFromProto(In.State);
                Torrent.State = EnumExt.ToString(mappedState);
                Torrent.InternalState = mappedState;
                Torrent.WantedSize = In.WantedSize;
                Torrent.Uploaded = In.Uploaded;
                Torrent.UPSpeed = In.UPSpeed;
                Torrent.Labels = In.Labels.Select(StringCache.Reuse).ToArray();
                Torrent.Peers = new(In.PeersConnected, In.PeersTotal);
                Torrent.Seeders = new(Torrent.Seeders.Connected, In.SeedersTotal);
                Torrent.Priority = EnumExt.ToString(MapFromProto(In.Priority));
                Torrent.FinishedOnDate = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? null : In.FinishedOn.ToDateTime();
                Torrent.TimeElapsed = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? TimeSpan.Zero : In.FinishedOn.ToDateTime() - Torrent.AddedOnDate;
                var oldTracker = Torrent.TrackerSingle;
                Torrent.TrackerSingle = In.PrimaryTracker != null ? In.PrimaryTracker.Uri : null;
                if (oldTracker != Torrent.TrackerSingle) {
                    Torrent.TrackerDisplayName = null;
                    Torrent.TrackerIcon = null;
                }
                Torrent.StatusMsg = StringCache.Reuse(In.StatusMessage);
                Torrent.RemotePath = StringCache.Reuse(In.RemotePath);
            });
        }

        public static Shared.Abstractions.File.DOWNLOAD_STRATEGY MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FileDownloadStrategy In)
        {
            return In switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FileDownloadStrategy.Normal => Shared.Abstractions.File.DOWNLOAD_STRATEGY.NORMAL,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeFirst => Shared.Abstractions.File.DOWNLOAD_STRATEGY.PRIORITIZE_FIRST,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeLast => Shared.Abstractions.File.DOWNLOAD_STRATEGY.PRIORITIZE_LAST,
                _ => Shared.Abstractions.File.DOWNLOAD_STRATEGY.NA
            };
        }

        public static Shared.Abstractions.File.PRIORITY MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FilePriority In)
        {
            return In switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FilePriority.DontDownload => Shared.Abstractions.File.PRIORITY.DONT_DOWNLOAD,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FilePriority.Normal => Shared.Abstractions.File.PRIORITY.NORMAL,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.FilePriority.High => Shared.Abstractions.File.PRIORITY.HIGH,
                _ => Shared.Abstractions.File.PRIORITY.NA
            };
        }

        public static Shared.Abstractions.File MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentsFilesReply.Types.File In)
        {
            return new Shared.Abstractions.File() {
                Path = In.Path,
                Size = In.Size,
                DownloadedPieces = In.DownloadedPieces,
                Downloaded = In.Downloaded,
                DownloadStrategy = MapFromProto(In.DownloadStrategy),
                Priority = MapFromProto(In.Priority)
            };
        }

        public static Shared.Abstractions.Peer.PEER_FLAGS MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags In)
        {
            return FlagsMapper.Map(In, x => x switch {
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.IIncoming => Shared.Abstractions.Peer.PEER_FLAGS.I_INCOMING,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.EEncrypted => Shared.Abstractions.Peer.PEER_FLAGS.E_ENCRYPTED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.SSnubbed => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.OObfuscated => Shared.Abstractions.Peer.PEER_FLAGS.O_OBFUSCATED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.PPreferred => Shared.Abstractions.Peer.PEER_FLAGS.P_PREFERRED,
                RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.PeerFlags.UUnwanted => Shared.Abstractions.Peer.PEER_FLAGS.U_UNWANTED,
                _ => throw new ArgumentOutOfRangeException(nameof(In), In, null)
            });
        }

        public static Shared.Abstractions.Peer MapFromProto(RTSharp.Daemon.Protocols.DataProvider.TorrentsPeersReply.Types.Peer In)
        {
            return new Shared.Abstractions.Peer() {
                PeerId = In.PeerID,
                IPPort = new IPEndPoint(new IPAddress(In.IPAddress.ToByteArray()), (ushort)In.Port),
                Client = In.Client,
                Flags = MapFromProto(In.Flags),
                Done = In.Done,
                Downloaded = In.Downloaded,
                Uploaded = In.Uploaded,
                DLSpeed = In.DLSpeed,
                UPSpeed = In.UPSpeed,
                PeerDLSpeed = In.PeerDLSpeed
            };
        }
    }
}
