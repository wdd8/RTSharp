using System.Net;

using Transmission.Net.Api.Entity;

namespace RTSharp.DataProvider.Transmission.Plugin.Mappers
{
    public class TorrentMapper
    {
        public static Shared.Abstractions.TORRENT_STATE MapFromExternal(global::Transmission.Net.Core.Enums.TorrentStatus In)
        {
            return In switch {
                global::Transmission.Net.Core.Enums.TorrentStatus.Stopped => Shared.Abstractions.TORRENT_STATE.STOPPED,
                global::Transmission.Net.Core.Enums.TorrentStatus.VerifyQueue => Shared.Abstractions.TORRENT_STATE.HASHING | Shared.Abstractions.TORRENT_STATE.QUEUED,
                global::Transmission.Net.Core.Enums.TorrentStatus.Verifying => Shared.Abstractions.TORRENT_STATE.HASHING,
                global::Transmission.Net.Core.Enums.TorrentStatus.DownloadQueue => Shared.Abstractions.TORRENT_STATE.DOWNLOADING | Shared.Abstractions.TORRENT_STATE.QUEUED,
                global::Transmission.Net.Core.Enums.TorrentStatus.Downloading => Shared.Abstractions.TORRENT_STATE.DOWNLOADING,
                global::Transmission.Net.Core.Enums.TorrentStatus.QueueSeed => Shared.Abstractions.TORRENT_STATE.SEEDING | Shared.Abstractions.TORRENT_STATE.HASHING,
                global::Transmission.Net.Core.Enums.TorrentStatus.Seeding => Shared.Abstractions.TORRENT_STATE.SEEDING,
                _ => Shared.Abstractions.TORRENT_STATE.NONE
            };
        }

        public static Shared.Abstractions.TORRENT_PRIORITY MapFromExternal(global::Transmission.Net.Core.Enums.Priority In)
        {
            return In switch {
                global::Transmission.Net.Core.Enums.Priority.Low => Shared.Abstractions.TORRENT_PRIORITY.LOW,
                global::Transmission.Net.Core.Enums.Priority.Normal => Shared.Abstractions.TORRENT_PRIORITY.NORMAL,
                global::Transmission.Net.Core.Enums.Priority.High => Shared.Abstractions.TORRENT_PRIORITY.HIGH,
                _ => Shared.Abstractions.TORRENT_PRIORITY.NA
            };
        }

        public static Shared.Abstractions.Torrent MapFromExternal(TorrentView In)
        {
            var peersTotal = In.TrackerStats.Max(x => x.LeecherCount);
            var seedsTotal = In.TrackerStats.Max(x => x.SeederCount);
            return new Shared.Abstractions.Torrent(Convert.FromHexString(In.HashString!)) {
                Name = In.Name!,
                State = MapFromExternal(In.Status!.Value),
                IsPrivate = In.IsPrivate,
                Size = (ulong)In.TotalSize!,
                WantedSize = (ulong)In.SizeWhenDone!,
                PieceSize = (ulong)In.PieceSize!,
                Wasted = (ulong)In.CorruptEver!,
                Done = (float)In.PercentDone! * 100,
                Downloaded = (ulong)In.DownloadedEver!,
                Uploaded = (ulong)In.UploadedEver!,
                DLSpeed = (ulong)In.RateDownload!,
                UPSpeed = (ulong)In.RateUpload!,
                ETA = In.Eta == null || In.Eta == -1 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(In.Eta.Value),
                Labels = In.Labels!.ToHashSet(),
                Peers = ((uint)In.PeersGettingFromUs!, (uint)(peersTotal == -1 ? 0 : peersTotal)),
                Seeders = ((uint)In.PeersSendingToUs!, (uint)(seedsTotal == -1 ? 0 : seedsTotal)), // TODO: ???
                Priority = MapFromExternal(In.BandwidthPriority!.Value),
                CreatedOnDate = In.DateCreated!.Value,
                RemainingSize = (ulong)(In.TotalSize - In.SizeWhenDone),
                FinishedOnDate = In.DoneDate,
                TimeElapsed = MapFromExternal(In.Status!.Value).HasFlag(Shared.Abstractions.TORRENT_STATE.SEEDING) ? TimeSpan.FromSeconds(In.SecondsSeeding!.Value) : TimeSpan.FromSeconds(In.SecondsDownloading!.Value),
                AddedOnDate = In.AddedDate == null ? DateTime.MinValue : In.AddedDate.Value,
                TrackerSingle = In.Trackers?.Length == 0 ? null : new Uri(In.Trackers!.OrderBy(x => x.Tier).First().Announce),
                StatusMessage = In.ErrorString,
                Comment = In.Comment ?? "", // TODO:?
                RemotePath = In.DownloadDir!,
                MagnetDummy = In.MagnetLink != null
            };
        }

        public static Shared.Abstractions.File MapFromExternal(global::Transmission.Net.Core.Entity.Torrent.ITorrentFile In, int PieceSize)
        {
            return new Shared.Abstractions.File {
                Path = In.Name!,
                Size = (ulong)In.Length!.Value,
                Downloaded = (ulong)In.BytesCompleted!.Value,
                DownloadedPieces = (ulong)(In.BytesCompleted!.Value / PieceSize),
                DownloadStrategy = Shared.Abstractions.File.DOWNLOAD_STRATEGY.NA,
                Priority = Shared.Abstractions.File.PRIORITY.NA
            };
        }

        public static Shared.Abstractions.Tracker.TRACKER_STATUS MapFromExternal(global::Transmission.Net.Core.Enums.TrackerState In)
        {
            return In switch {
                global::Transmission.Net.Core.Enums.TrackerState.Inactive => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_ACTIVE,
                global::Transmission.Net.Core.Enums.TrackerState.Waiting => Shared.Abstractions.Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                global::Transmission.Net.Core.Enums.TrackerState.Queued => Shared.Abstractions.Tracker.TRACKER_STATUS.ENABLED,
                global::Transmission.Net.Core.Enums.TrackerState.Active => Shared.Abstractions.Tracker.TRACKER_STATUS.ACTIVE,
                _ => 0
            };
        }

        public static Shared.Abstractions.Tracker MapFromExternal(global::Transmission.Net.Core.Entity.ITorrentTrackerStats In)
        {
            return new Shared.Abstractions.Tracker {
                ID = In.Id!,
                Uri = new Uri(In.Announce!),
                Status = In.AnnounceState == null ? MapFromExternal(In.ScrapeState!.Value) : MapFromExternal(In.AnnounceState!.Value),
                Seeders = In.SeederCount == -1 ? 0 : (uint)In.SeederCount!,
                Peers = In.LeecherCount == -1 ? 0 : (uint)In.LeecherCount!, // TODO: is this right?
                Downloaded = (uint)In.LastAnnouncePeerCount!,
                LastUpdated = In.LastAnnounceTime,
                Interval = In.NextAnnounceTime == null || In.LastAnnounceTime == null ? TimeSpan.Zero : In.NextAnnounceTime.Value - In.LastAnnounceTime.Value,
                StatusMsg = In.LastAnnounceResult ?? In.LastScrapeResult ?? ""
            };
        }

        public static Shared.Abstractions.Peer MapFromExternal(global::Transmission.Net.Core.Entity.Torrent.ITorrentPeers In, ulong PeerDLSpeed)
        {
            Shared.Abstractions.Peer.PEER_FLAGS flag(char In)
            {
                // ???????????????????????????????????????????????????????????????????
                switch (In) {
                    case 'O':
                        // Optimistic unchoke
                        return Shared.Abstractions.Peer.PEER_FLAGS.S_SNUBBED;
                    case 'D':
                        // Downloading from this peer
                        break;
                    case 'd':
                        // We would download from this peer if they would let us
                        return Shared.Abstractions.Peer.PEER_FLAGS.P_PREFERRED;
                    case 'U':
                        // Uploading to peer
                        break;
                    case 'u':
                        // We would upload to this peer if they asked
                        return Shared.Abstractions.Peer.PEER_FLAGS.P_PREFERRED;
                    case 'K':
                        // Peer has unchoked us, but we're not interested
                        break;
                    case '?':
                        // We unchoked this peer, but they're not interested
                        break;
                    case 'E':
                        // Encrypted connection
                        return Shared.Abstractions.Peer.PEER_FLAGS.E_ENCRYPTED;
                    case 'X':
                        // Peer was found through Peer Exchange (PEX)
                        break;
                    case 'H':
                        // Peer was found through DHT
                        break;
                    case 'I':
                        // Peer is an incoming connection
                        return Shared.Abstractions.Peer.PEER_FLAGS.I_INCOMING;
                    case 'T':
                        // Peer is connected over µTP
                        break;
                }
                return 0;
            }

            return new Shared.Abstractions.Peer {
                PeerId = In.Address!,
                IPPort = new IPEndPoint(IPAddress.Parse(In.Address!), In.Port!.Value!),
                Client = In.ClientName!,
                DLSpeed = (ulong)In.RateToClient!,
                Done = (float)In.Progress! * 100,
                Flags = In.FlagStr!.Select(flag).Aggregate((a, b) => a | b),
                UPSpeed = (ulong)In.RateToPeer!,
                Downloaded = 0,
                Uploaded = 0,
                PeerDLSpeed = PeerDLSpeed
            };
        }

        /*public static Shared.Abstractions.Tracker MapFromExternal(global::Transmission.Net.Core.Entity.ITorrentTracker In)
        {
            return new Shared.Abstractions.Tracker {
                ID = In.Id,
                Uri = In.Announce,
                Downloaded = 0,
                Interval = TimeSpan.Zero,
                LastUpdated
            }
        }*/
    }
}
