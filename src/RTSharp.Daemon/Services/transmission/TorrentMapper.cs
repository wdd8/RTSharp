using Google.Protobuf.WellKnownTypes;

using System.Net;

using Transmission.Net.Api.Entity;

namespace RTSharp.Daemon.Services.transmission;

public class TorrentMapper
{
    public static Protocols.DataProvider.TorrentState MapFromExternal(global::Transmission.Net.Core.Enums.TorrentStatus In)
    {
        return In switch {
            global::Transmission.Net.Core.Enums.TorrentStatus.Stopped => Protocols.DataProvider.TorrentState.Stopped,
            global::Transmission.Net.Core.Enums.TorrentStatus.VerifyQueue => Protocols.DataProvider.TorrentState.Hashing | Protocols.DataProvider.TorrentState.Queued,
            global::Transmission.Net.Core.Enums.TorrentStatus.Verifying => Protocols.DataProvider.TorrentState.Hashing,
            global::Transmission.Net.Core.Enums.TorrentStatus.DownloadQueue => Protocols.DataProvider.TorrentState.Downloading | Protocols.DataProvider.TorrentState.Queued,
            global::Transmission.Net.Core.Enums.TorrentStatus.Downloading => Protocols.DataProvider.TorrentState.Downloading,
            global::Transmission.Net.Core.Enums.TorrentStatus.QueueSeed => Protocols.DataProvider.TorrentState.Seeding | Protocols.DataProvider.TorrentState.Hashing,
            global::Transmission.Net.Core.Enums.TorrentStatus.Seeding => Protocols.DataProvider.TorrentState.Seeding,
            _ => Protocols.DataProvider.TorrentState.None
        };
    }

    public static Protocols.DataProvider.TorrentPriority MapFromExternal(global::Transmission.Net.Core.Enums.Priority In)
    {
        return In switch {
            global::Transmission.Net.Core.Enums.Priority.Low => Protocols.DataProvider.TorrentPriority.Low,
            global::Transmission.Net.Core.Enums.Priority.Normal => Protocols.DataProvider.TorrentPriority.Normal,
            global::Transmission.Net.Core.Enums.Priority.High => Protocols.DataProvider.TorrentPriority.High,
            _ => Protocols.DataProvider.TorrentPriority.Na
        };
    }

    public static void ApplyFromExternal(Protocols.DataProvider.Torrent Stored, TorrentView External)
    {
        var peersTotal = External.TrackerStats?.Max(x => x.LeecherCount == -1 ? 0 : x.LeecherCount);
        var seedsTotal = External.TrackerStats?.Max(x => x.SeederCount == -1 ? 0 : x.SeederCount);

        if (External.Name != null)
            Stored.Name = External.Name;
        if (External.Status != null)
            Stored.State = MapFromExternal(External.Status.Value);
        if (External.IsPrivate != null)
            Stored.IsPrivate = External.IsPrivate.Value;
        if (External.TotalSize != null)
            Stored.Size = (ulong)External.TotalSize.Value;
        if (External.SizeWhenDone != null)
            Stored.WantedSize = (ulong)External.SizeWhenDone.Value;
        if (External.PieceSize != null)
            Stored.ChunkSize = (uint)External.PieceSize.Value;
        if (External.CorruptEver != null)
            Stored.Wasted = (ulong)External.CorruptEver.Value;
        if (External.DownloadedEver != null)
            Stored.Downloaded = (ulong)External.DownloadedEver.Value;
        if ((External.Status == Transmission.Net.Core.Enums.TorrentStatus.Verifying || External.Status == Transmission.Net.Core.Enums.TorrentStatus.VerifyQueue) && External.RecheckProgress != null && External.SizeWhenDone != null) {
            // We cannot report back Done% so emulate it through size
            Stored.CompletedSize = (ulong)(External.SizeWhenDone * External.RecheckProgress);
        } else if (External.SizeWhenDone != null && External.LeftUntilDone != null)
            Stored.CompletedSize = (ulong)External.SizeWhenDone! - (ulong)External.LeftUntilDone!;
        if (External.UploadedEver != null)
            Stored.Uploaded = (ulong)External.UploadedEver.Value;
        if (External.RateDownload != null)
            Stored.DLSpeed = (ulong)External.RateDownload.Value;
        if (External.RateUpload != null)
            Stored.UPSpeed = (ulong)External.RateUpload.Value;
        if (External.Eta != null)
            Stored.ETA = External.Eta == null || External.Eta == -1 ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(External.Eta.Value));
        if (External.Labels != null) {
            Stored.Labels.Clear();
            Stored.Labels.AddRange(External.Labels);
        }
        if (peersTotal != null)
            Stored.PeersTotal = (uint)peersTotal.Value;
        if (External.PeersGettingFromUs != null)
            Stored.PeersConnected = (uint)External.PeersGettingFromUs.Value;
        if (seedsTotal != null)
            Stored.SeedersTotal = (uint)seedsTotal.Value;
        if (External.PeersSendingToUs != null)
            Stored.SeedersConnected = (uint)External.PeersSendingToUs.Value;
        if (External.BandwidthPriority != null)
            Stored.Priority = MapFromExternal(External.BandwidthPriority.Value);
        if (External.DateCreated != null)
            Stored.CreatedOn = Timestamp.FromDateTime(External.DateCreated.Value.ToUniversalTime());
        if (External.DoneDate != null)
            Stored.FinishedOn = Timestamp.FromDateTime(External.DoneDate.Value.ToUniversalTime());
        if (External.AddedDate != null)
            Stored.AddedOn = Timestamp.FromDateTime(External.AddedDate.Value.ToUniversalTime());
        if (External.Trackers?.Length != 0) {
            var tracker = External.Trackers!.OrderBy(x => x.Tier).First();
            Stored.PrimaryTracker = new Protocols.DataProvider.TorrentTracker {
                ID = tracker.Id.ToString(),
                Uri = tracker.Announce
            };
        }
        if (External.ErrorString != null)
            Stored.StatusMessage = External.ErrorString;
        if (External.Comment != null)
            Stored.Comment = External.Comment;
        if (External.DownloadDir != null)
            Stored.RemotePath = External.DownloadDir;
        Stored.MagnetDummy = External.MagnetLink != null;
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
            Uri = In.Announce!,
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
