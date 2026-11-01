using Nito.AsyncEx;
using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.Services.TorrentPoll;

public sealed class PollingSubscription : IDisposable
{
    public TimeSpan Interval { get; }
    private readonly AsyncAutoResetEvent NewSnapshot = new(false);
    private readonly Lock Lock = new();
    private readonly TorrentPollingService Instance;
    private HistoryEntry? Pending;
    private HistoryEntry? Consumed;
    private DateTime LastPublished;
    private bool Disposed;

    internal PollingSubscription(TimeSpan Interval, TorrentPollingService Instance)
    {
        this.Interval = Interval;
        this.Instance = Instance;
    }

    internal void Publish(HistoryEntry Entry)
    {
        lock (Lock) {
            if (Disposed || (LastPublished != default && Entry.Date - LastPublished < Interval))
                return;
            LastPublished = Entry.Date;
            Pending = Entry;
            NewSnapshot.Set();
        }
    }

    public async Task<DeltaTorrentsListResponse?> GetChanges(bool AlwaysFullUpdate, CancellationToken CancellationToken)
    {
        HistoryEntry? previous;
        HistoryEntry current;
        while (true) {
            lock (Lock) {
                if (Disposed)
                    return null;
            }
            await NewSnapshot.WaitAsync(CancellationToken);
            lock (Lock) {
                if (Disposed)
                    return null;
                if (Pending == null)
                    continue;
                current = Pending;
                Pending = null;
                previous = AlwaysFullUpdate ? null : Consumed;
                Consumed = current;
                break;
            }
        }

        return Instance.BuildChanges(previous, current);
    }

    public string? GetTorrentName(byte[] Hash) => Instance.GetLatestHistoryEntry(Hash)?.Torrent.Name;

    public void Dispose()
    {
        lock (Lock) {
            if (Disposed)
                return;
            Disposed = true;
            Pending = Consumed = null;
            NewSnapshot.Set();
        }
        Instance.Unsubscribe(this);
    }
}

internal static class TorrentDeltaMessages
{
    internal static CompleteDeltaTorrentResponse Complete(Torrent Torrent) => new() {
        Hash = Torrent.Hash,
        Uploaded = Torrent.Uploaded,
        UPSpeed = Torrent.UPSpeed,
        SeedersTotal = Torrent.SeedersTotal,
        PeersTotal = Torrent.PeersTotal,
        PeersConnected = Torrent.PeersConnected
    };

    internal static IncompleteDeltaTorrentResponse Incomplete(Torrent Torrent) => new() {
        Hash = Torrent.Hash,
        Downloaded = Torrent.Downloaded,
        CompletedSize = Torrent.CompletedSize,
        Uploaded = Torrent.Uploaded,
        DLSpeed = Torrent.DLSpeed,
        HashingSpeed = Torrent.HashingSpeed,
        UPSpeed = Torrent.UPSpeed,
        ETA = Torrent.ETA?.Clone(),
        SeedersConnected = Torrent.SeedersConnected,
        SeedersTotal = Torrent.SeedersTotal,
        PeersConnected = Torrent.PeersConnected,
        PeersTotal = Torrent.PeersTotal,
        Wasted = Torrent.Wasted
    };
}
