namespace RTSharp.Shared.Abstractions
{
    public record DataProviderCapabilities(
        bool GetFiles,
        bool GetPeers,
        bool GetTrackers,
        bool StartTorrent,
        bool PauseTorrent,
        bool StopTorrent,
        bool ForceRecheckTorrent,
        bool ReannounceToAllTrackers,
        bool AddTorrent,
        bool? ForceStartTorrentOnAdd,
        bool MoveDownloadDirectory,
        bool RemoveTorrent,
        bool RemoveTorrentAndData,
        bool AddLabel,
        bool AddPeer,
        bool BanPeer,
        bool KickPeer,
        bool SnubPeer,
        bool UnsnubPeer,
        bool SetLabels
    );
}
