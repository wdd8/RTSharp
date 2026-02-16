namespace RTSharp.Shared.Abstractions;

public record DataProviderPeerCapabilities(
    bool AddPeer,
    bool BanPeer,
    bool KickPeer,
    bool SnubPeer,
    bool UnsnubPeer
);
