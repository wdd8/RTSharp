using Grpc.Core;

using System.Diagnostics;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Utils;

namespace RTSharp.Daemon.Services.TorrentPoll;

public sealed class HistoryEntry
{
    public DateTime Date { get; }
    public InfoHashDictionary<Torrent> List { get; }
    public InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers { get; }

    public HistoryEntry(DateTime date, InfoHashDictionary<Torrent> list, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>? trackers = null)
    {
        Date = date;
        List = list;
        Trackers = trackers ?? [];
    }
}

public abstract class TorrentPollingService(ILogger Logger, EventsService Events) : BackgroundService
{
    private readonly Lock Lock = new();
    private readonly List<PollingSubscription> Subscribers = [];
    private TaskCompletionSource Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile HistoryEntry? Latest;
    private long LastPollDurationTicks;

    public TimeSpan LastPollDuration => TimeSpan.FromTicks(Interlocked.Read(ref LastPollDurationTicks));

    protected internal abstract DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current);

    public PollingSubscription Subscribe(TimeSpan Interval)
    {
        if (Interval < TimeSpan.FromMilliseconds(10) || Interval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(Interval), "Polling interval must be between 10ms and 49 days.");
        lock (Lock) {
            var subscription = new PollingSubscription(Interval, this);
            if (Subscribers.Count > 0 && Latest != null)
                subscription.Publish(Latest);
            Subscribers.Add(subscription);
            SignalChanged();
            return subscription;
        }
    }

    internal void Unsubscribe(PollingSubscription Subscription)
    {
        lock (Lock) {
            Subscribers.Remove(Subscription);
            SignalChanged();
        }
    }

    private void SignalChanged()
    {
        Changed.TrySetResult();
        Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    protected abstract Task<HistoryEntry> PollAsync(CancellationToken CancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPoll = DateTime.MinValue;
        var retryAfter = DateTime.MinValue;
        try {
            while (!stoppingToken.IsCancellationRequested) {
                Task changed;
                TimeSpan delay;
                lock (Lock) {
                    changed = Changed.Task;
                    if (Subscribers.Count == 0) {
                        lastPoll = DateTime.MinValue;
                        delay = Timeout.InfiniteTimeSpan;
                    } else {
                        var due = lastPoll + Subscribers.Min(x => x.Interval);
                        if (due < retryAfter)
                            due = retryAfter;
                        delay = due - DateTime.UtcNow;
                    }
                }

                if (delay == Timeout.InfiniteTimeSpan || delay > TimeSpan.Zero) {
                    try {
                        await changed.WaitAsync(delay, stoppingToken);
                    } catch (TimeoutException) { }
                    continue;
                }

                lastPoll = DateTime.UtcNow;
                HistoryEntry entry;
                var stopwatch = Stopwatch.StartNew();
                try {
                    entry = await PollAsync(stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    if (ex is DataProviderFailure failure)
                        Logger.LogConnectionFailure(failure);
                    else
                        Logger.LogError(ex, "Torrent poll failed");
                    retryAfter = DateTime.UtcNow.AddSeconds(1);
                    continue;
                }
                stopwatch.Stop();
                Interlocked.Exchange(ref LastPollDurationTicks, stopwatch.Elapsed.Ticks);

                lastPoll = DateTime.UtcNow;
                var previous = Latest;
                Latest = entry;
                foreach (var (hash, torrent) in entry.List) {
                    var old = previous != null && previous.List.TryGetValue(hash, out var value) ? value : null;
                    if (torrent.State != (old?.State ?? TorrentState.None)) {
                        var before = (old ?? torrent).Clone();
                        before.State = old?.State ?? TorrentState.None;
                        Events.EmitTorrentChangedStateEvent(this, before, torrent.State);
                    }
                }

                lock (Lock) {
                    foreach (var subscription in Subscribers)
                        subscription.Publish(entry);
                }
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task Stream(GetTorrentListUpdatesRequest Request, IServerStreamWriter<DeltaTorrentsListResponse> Response, CancellationToken CancellationToken)
    {
        var interval = Request.Interval.ToTimeSpan();
        try {
            if (interval < TimeSpan.FromMilliseconds(10) || interval.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(interval), "Polling interval must be between 10ms and 49 days.");
        } catch (ArgumentOutOfRangeException ex) {
            throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, ex.Message));
        }
        using var subscription = Subscribe(interval);
        await Response.WriteAsync(new(), CancellationToken);
        while (!CancellationToken.IsCancellationRequested) {
            var changes = await subscription.GetChanges(false, CancellationToken);
            if (changes == null)
                return;
            if (changes.FullUpdate.Count + changes.Complete.Count + changes.Incomplete.Count + changes.Removed.Count > 0)
                await Response.WriteAsync(changes, CancellationToken);
        }
    }

    public (Torrent Torrent, TorrentsTrackersReply.Types.TorrentsTrackers Trackers)? GetLatestHistoryEntry(byte[] Hash)
    {
        var latest = Latest;
        if (latest != null && latest.List.TryGetValue(Hash, out var torrent)) {
            latest.Trackers.TryGetValue(Hash, out var trackers);
            return (torrent.Clone(), trackers?.Clone() ?? new() { InfoHash = torrent.Hash });
        }
        return null;
    }

    public TorrentTracker[] GetTorrentTrackers(byte[] Hash) => GetLatestHistoryEntry(Hash)?.Trackers.Trackers.Select(x => x.Clone()).ToArray() ?? [];
}
