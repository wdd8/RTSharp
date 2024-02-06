using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using RTSharp.DataProvider.Rtorrent.Protocols;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Mappers
{
    public static class TorrentMapper
    {
        public static TORRENT_STATE MapFromProto(Protocols.TorrentState In)
        {
            return FlagsMapper.Map(In, x => x switch {
                Protocols.TorrentState.Downloading => TORRENT_STATE.DOWNLOADING,
                Protocols.TorrentState.Seeding => TORRENT_STATE.SEEDING,
                Protocols.TorrentState.Hashing => TORRENT_STATE.HASHING,
                Protocols.TorrentState.Paused => TORRENT_STATE.PAUSED,
                Protocols.TorrentState.Stopped => TORRENT_STATE.STOPPED,
                Protocols.TorrentState.Complete => TORRENT_STATE.COMPLETE,
                Protocols.TorrentState.Active => TORRENT_STATE.ACTIVE,
                Protocols.TorrentState.Inactive => TORRENT_STATE.INACTIVE,
                Protocols.TorrentState.Errored => TORRENT_STATE.ERRORED,
                _ => TORRENT_STATE.NONE
            });
        }

        public static TORRENT_PRIORITY MapFromProto(Protocols.TorrentPriority In)
        {
            return In switch {
                Protocols.TorrentPriority.Off => TORRENT_PRIORITY.OFF,
                Protocols.TorrentPriority.Low => TORRENT_PRIORITY.LOW,
                Protocols.TorrentPriority.Normal => TORRENT_PRIORITY.NORMAL,
                Protocols.TorrentPriority.High => TORRENT_PRIORITY.HIGH,
                _ => TORRENT_PRIORITY.NA
            };
        }

        private static Shared.Abstractions.Tracker.TRACKER_STATUS MapFromProto(Protocols.TorrentTrackerStatus In)
        {
            return FlagsMapper.Map(In, x => x switch {
                TorrentTrackerStatus.Active => Shared.Abstractions.Tracker.TRACKER_STATUS.ACTIVE,
                TorrentTrackerStatus.NotActive => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_ACTIVE,
                TorrentTrackerStatus.NotContactedYet => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                TorrentTrackerStatus.Disabled => Shared.Abstractions.Tracker.TRACKER_STATUS.DISABLED,
                TorrentTrackerStatus.Enabled => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED,
                _ => Shared.Abstractions.Tracker.TRACKER_STATUS.DISABLED
            });
        }

        public static void UpdateTracker(Shared.Abstractions.Tracker Bottom, TorrentTracker Top)
        {
            Bottom.ID = Top.Uri;
            Bottom.Uri = new Uri(Top.Uri);
            Bottom.Status = MapFromProto(Top.Status);
            Bottom.Seeders = Top.Seeders;
            Bottom.Peers = Top.Peers;
            Bottom.Downloaded = Top.Downloaded;
            Bottom.LastUpdated = Top.LastUpdated.ToDateTime();
            Bottom.Interval = Top.ScrapeInterval.ToTimeSpan();
        }

        public static Shared.Abstractions.Tracker MapFromProto(TorrentTracker In)
        {
            var ret = new Shared.Abstractions.Tracker();
            UpdateTracker(ret, In);
            return ret;
        }

        private static string SelectTracker(IEnumerable<Protocols.TorrentTracker> In)
        {
            return In.FirstOrDefault()?.Uri;
        }

        public static Shared.Abstractions.Torrent MapFromProto(Protocols.Torrent In)
        {
            return new Shared.Abstractions.Torrent(In.Hash.ToByteArray()) {
                Name = In.Name,
                State = MapFromProto(In.State),
                IsPrivate = In.IsPrivate,
                Size = In.Size,
                WantedSize = In.Size, // ??????????
                ChunkSize = In.ChunkSize,
                Wasted = In.Wasted,
                Done = (float)In.Downloaded / In.Size * 100,
                Downloaded = In.Downloaded,
                Uploaded = In.Uploaded,
                DLSpeed = In.DLSpeed,
                UPSpeed = In.UPSpeed,
                ETA = In.ETA == null ? TimeSpan.MaxValue : In.ETA.ToTimeSpan(),
                Labels = In.Labels.ToHashSet(),
                Peers = (In.PeersConnected, In.PeersTotal),
                Seeders = (In.SeedersConnected, In.SeedersTotal),
                Priority = MapFromProto(In.Priority),
                CreatedOnDate = In.CreatedOn.ToDateTime(),
                RemainingSize = In.Size - In.Downloaded,
                FinishedOnDate = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? null : In.FinishedOn.ToDateTime(),
                TimeElapsed = In.FinishedOn.ToDateTime() == DateTime.UnixEpoch ? (DateTime.UtcNow - In.AddedOn.ToDateTime()) : (In.FinishedOn.ToDateTime() - In.AddedOn.ToDateTime()),
                AddedOnDate = In.AddedOn.ToDateTime(),
                TrackerSingle = SelectTracker(In.Trackers) == null ? null : new Uri(SelectTracker(In.Trackers)),
                StatusMessage = In.StatusMessage,
                Comment = In.Comment,
                RemotePath = In.RemotePath,
                MagnetDummy = Regex.IsMatch(In.Name, "magnet:\\?xt=urn:[a-z0-9]+:[a-z0-9]{32,40}&dn=.+&tr=.+")
            };
        }

        public static Shared.Abstractions.File.DOWNLOAD_STRATEGY MapFromProto(Protocols.TorrentsFilesReply.Types.FileDownloadStrategy In)
        {
            return In switch {
                TorrentsFilesReply.Types.FileDownloadStrategy.Normal => Shared.Abstractions.File.DOWNLOAD_STRATEGY.NORMAL,
                TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeFirst => Shared.Abstractions.File.DOWNLOAD_STRATEGY.PRIORITIZE_FIRST,
                TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeLast => Shared.Abstractions.File.DOWNLOAD_STRATEGY.PRIORITIZE_LAST,
                _ => Shared.Abstractions.File.DOWNLOAD_STRATEGY.NA
            };
        }

        public static Shared.Abstractions.File.PRIORITY MapFromProto(Protocols.TorrentsFilesReply.Types.FilePriority In)
        {
            return In switch {
                TorrentsFilesReply.Types.FilePriority.DontDownload => Shared.Abstractions.File.PRIORITY.DONT_DOWNLOAD,
                TorrentsFilesReply.Types.FilePriority.Normal => Shared.Abstractions.File.PRIORITY.NORMAL,
                TorrentsFilesReply.Types.FilePriority.High => Shared.Abstractions.File.PRIORITY.HIGH,
                _ => Shared.Abstractions.File.PRIORITY.NA
            };
        }

        public static Shared.Abstractions.File MapFromProto(Protocols.TorrentsFilesReply.Types.File In)
        {
            return new Shared.Abstractions.File() {
                Path = In.Path,
                Size = In.Size,
                DownloadedChunks = In.CompletedChunks,
                DownloadStrategy = MapFromProto(In.DownloadStrategy),
                Priority = MapFromProto(In.Priority)
            };
        }

        public static Shared.Abstractions.Peer.PEER_FLAGS MapFromProto(Protocols.TorrentsPeersReply.Types.PeerFlags In)
        {
            return FlagsMapper.Map(In, x => x switch {
                TorrentsPeersReply.Types.PeerFlags.IIncoming => Shared.Abstractions.Peer.PEER_FLAGS.I_INCOMING,
                TorrentsPeersReply.Types.PeerFlags.EEncrypted => Shared.Abstractions.Peer.PEER_FLAGS.E_ENCRYPTED,
                TorrentsPeersReply.Types.PeerFlags.SSnubbed => Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED,
                TorrentsPeersReply.Types.PeerFlags.OObfuscated => Shared.Abstractions.Peer.PEER_FLAGS.O_OBFUSCATED,
                TorrentsPeersReply.Types.PeerFlags.PPreferred => Shared.Abstractions.Peer.PEER_FLAGS.P_PREFERRED,
                TorrentsPeersReply.Types.PeerFlags.UUnwanted => Shared.Abstractions.Peer.PEER_FLAGS.U_UNWANTED,
                _ => throw new ArgumentOutOfRangeException(nameof(In), In, null)
            });
        }

        public static Shared.Abstractions.Peer MapFromProto(Protocols.TorrentsPeersReply.Types.Peer In)
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
                UPSpeed = In.UPSpeed
            };
        }
    }
}
