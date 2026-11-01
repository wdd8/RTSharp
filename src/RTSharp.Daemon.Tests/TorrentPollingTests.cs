using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Daemon.Services.TorrentPoll;
using RTSharp.Shared.Utils;
using Xunit;
using QbittorrentPolling = RTSharp.Daemon.Services.qbittorrent.TorrentPolling;
using RtorrentPolling = RTSharp.Daemon.Services.rtorrent.TorrentPoll.TorrentPolling;
using TransmissionPolling = RTSharp.Daemon.Services.transmission.TorrentPolling;

namespace RTSharp.Daemon.Tests;

public class TorrentPollingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(30);

    private sealed class TestPoller(EventsService Events) : TorrentPollingService(NullLogger<TestPoller>.Instance, Events)
    {
        public bool EmitCompleteDeltas = true;
        public readonly Channel<Torrent[]> Results = Channel.CreateUnbounded<Torrent[]>();
        public readonly Channel<int> Started = Channel.CreateUnbounded<int>();
        public int Count;
        public bool FailNext;

        protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current)
        {
            var delta = new DeltaTorrentsListResponse();
            foreach (var (hash, torrent) in Current.List) {
                if (Previous == null || !Previous.List.TryGetValue(hash, out var old) || torrent.State != old.State) {
                    delta.FullUpdate.Add(torrent.Clone());
                } else if (torrent.IsCompleted && torrent.PeersConnected != old.PeersConnected && EmitCompleteDeltas) {
                    delta.Complete.Add(new CompleteDeltaTorrentResponse { Hash = torrent.Hash, PeersConnected = torrent.PeersConnected });
                }
            }
            if (Previous != null) {
                foreach (var (hash, torrent) in Previous.List) {
                    if (!Current.List.ContainsKey(hash))
                        delta.Removed.Add(torrent.Hash);
                }
            }
            return delta;
        }

        protected override async Task<HistoryEntry> PollAsync(CancellationToken CancellationToken)
        {
            var count = Interlocked.Increment(ref Count);
            Started.Writer.TryWrite(count);
            if (FailNext) {
                FailNext = false;
                throw new IOException("Temporary provider failure");
            }
            var torrents = await Results.Reader.ReadAsync(CancellationToken);
            return new HistoryEntry(DateTime.UtcNow, torrents.ToInfoHashDictionary(x => x.Hash.ToByteArray(), x => x.Clone()));
        }

        public Task<int> NextPoll() => Started.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        public void Reply(params Torrent[] Torrents) => Results.Writer.TryWrite(Torrents);
    }

    private static EventsService CreateEvents()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new EventsService(NullLogger<EventsService>.Instance, new RegisteredDataProviders(services, new ConfigurationBuilder().Build()));
    }

    private static Torrent Torrent(byte Id, TorrentState State = TorrentState.Downloading) => new() {
        Hash = ByteString.CopyFrom(Enumerable.Repeat(Id, 20).ToArray()),
        Name = $"Torrent {Id}",
        State = State
    };

    private static async Task<DeltaTorrentsListResponse> Read(PollingSubscription Subscription)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        return (await Subscription.GetChanges(false, cancellation.Token))!;
    }

    private static HistoryEntry Snapshot(Torrent Torrent, TorrentsTrackersReply.Types.TorrentsTrackers? Trackers = null)
    {
        var torrents = new[] { Torrent }.ToInfoHashDictionary(x => x.Hash.ToByteArray(), x => x);
        var trackers = new InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>();
        if (Trackers != null)
            trackers[Torrent.Hash.ToByteArray()] = Trackers;
        return new HistoryEntry(DateTime.UtcNow, torrents, trackers);
    }

    [Fact]
    public void ProvidersOwnFullCompleteAndIncompleteComparisons()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var events = CreateEvents();
        using var qbittorrent = new QbittorrentPolling("test", services, NullLogger<QbittorrentPolling>.Instance, events);
        using var transmission = new TransmissionPolling("test", services, NullLogger<TransmissionPolling>.Instance, events);
        using var rtorrent = new RtorrentPolling(services.GetRequiredService<IServiceScopeFactory>(), "test", null!, NullLogger<RtorrentPolling>.Instance, events);

        var completed = Torrent(1, TorrentState.Seeding);
        var peerChange = completed.Clone();
        peerChange.PeersConnected = 2;
        var before = Snapshot(completed);
        var after = Snapshot(peerChange);
        Assert.Empty(qbittorrent.BuildChanges(before, after).Complete);
        Assert.Single(transmission.BuildChanges(before, after).Complete);
        Assert.Single(rtorrent.BuildChanges(before, after).Complete);

        var active = peerChange.Clone();
        active.State |= TorrentState.Active;
        Assert.Single(qbittorrent.BuildChanges(after, Snapshot(active)).FullUpdate);
        var activePeerChange = active.Clone();
        activePeerChange.PeersConnected = 3;
        Assert.Single(qbittorrent.BuildChanges(Snapshot(active), Snapshot(activePeerChange)).Complete);

        var incomplete = Torrent(2);
        incomplete.WantedSize = 100;
        var progress = incomplete.Clone();
        progress.Downloaded = 10;
        foreach (var provider in new TorrentPollingService[] { qbittorrent, transmission, rtorrent }) {
            Assert.Single(provider.BuildChanges(Snapshot(incomplete), Snapshot(progress)).Incomplete);
            var renamed = progress.Clone();
            renamed.Name = "Renamed";
            Assert.Single(provider.BuildChanges(Snapshot(progress), Snapshot(renamed)).FullUpdate);
        }

        var oldTrackers = new TorrentsTrackersReply.Types.TorrentsTrackers { InfoHash = completed.Hash };
        oldTrackers.Trackers.Add(new TorrentTracker { Uri = "https://tracker.example", StatusMessage = "old" });
        var newTrackers = oldTrackers.Clone();
        newTrackers.Trackers[0].StatusMessage = "new";
        Assert.Single(rtorrent.BuildChanges(Snapshot(completed, oldTrackers), Snapshot(completed, newTrackers)).FullUpdate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedDeltaUsesProviderPolicy(bool EmitCompleteDeltas)
    {
        using var poller = new TestPoller(CreateEvents()) { EmitCompleteDeltas = EmitCompleteDeltas };
        using var subscription = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            poller.Reply(Torrent(1, TorrentState.Seeding));
            Assert.Single((await Read(subscription)).FullUpdate);

            await poller.NextPoll();
            var changed = Torrent(1, TorrentState.Seeding);
            changed.PeersConnected = 2;
            poller.Reply(changed);
            var delta = await Read(subscription);
            Assert.Equal(EmitCompleteDeltas ? 1 : 0, delta.Complete.Count);
            Assert.Empty(delta.FullUpdate);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SharesPollsStartsLazilyAndStopsAfterLastSubscriber()
    {
        using var poller = new TestPoller(CreateEvents());
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await Task.Delay(70, TestContext.Current.CancellationToken);
            Assert.Equal(0, poller.Count);
            using var first = poller.Subscribe(TimeSpan.FromSeconds(1));
            using var second = poller.Subscribe(TimeSpan.FromSeconds(1.1));
            Assert.Equal(1, await poller.NextPoll());
            poller.Reply(Torrent(1));
            Assert.Single((await Read(first)).FullUpdate);
            Assert.Single((await Read(second)).FullUpdate);
            using var late = poller.Subscribe(TimeSpan.FromSeconds(1));
            Assert.Single((await Read(late)).FullUpdate);
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Equal(1, poller.Count);
            first.Dispose();
            second.Dispose();
            late.Dispose();
            await Task.Delay(1100, TestContext.Current.CancellationToken);
            Assert.Equal(1, poller.Count);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AddingAndRemovingFastSubscriberReschedulesTheSameLoop()
    {
        using var poller = new TestPoller(CreateEvents());
        using var slow = poller.Subscribe(TimeSpan.FromSeconds(2));
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            poller.Reply(Torrent(1));
            await Read(slow);
            using var fast = poller.Subscribe(Fast);
            await Read(fast); // Cached initial snapshot.
            Assert.Equal(2, await poller.NextPoll());
            fast.Dispose();
            poller.Reply(Torrent(1));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Equal(2, poller.Count);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SlowReaderGetsChangesSinceItsLastConsumedSnapshot()
    {
        using var poller = new TestPoller(CreateEvents());
        using var fast = poller.Subscribe(Fast);
        using var slow = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            poller.Reply(Torrent(1));
            await Read(fast);
            await Read(slow);
            await poller.NextPoll();
            poller.Reply(Torrent(2));
            await Read(fast);
            await poller.NextPoll();
            var changed = Torrent(2);
            changed.Downloaded = 123;
            poller.Reply(changed);
            await Read(fast);
            var changes = await Read(slow);
            Assert.Equal(Torrent(1).Hash, Assert.Single(changes.Removed));
            Assert.Equal((ulong)123, Assert.Single(changes.FullUpdate).Downloaded);
            Assert.Empty(changes.Incomplete);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task FirstReadRemainsFullEvenWhenConsumerMissesSeveralPolls()
    {
        using var poller = new TestPoller(CreateEvents());
        using var first = poller.Subscribe(Fast);
        using var delayed = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            poller.Reply(Torrent(1));
            await Read(first);
            await poller.NextPoll();
            var updated = Torrent(1);
            updated.Downloaded = 100;
            poller.Reply(updated, Torrent(2));
            await Read(first);
            var changes = await Read(delayed);
            Assert.Equal(2, changes.FullUpdate.Count);
            Assert.Empty(changes.Incomplete);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task EmitsStateTransitionsOnceWithoutReadingSubscriptions()
    {
        var events = CreateEvents();
        var observed = new ConcurrentQueue<(TorrentState Old, TorrentState New)>();
        using var handler = events.SubscribeToTorrentChangedStateEvent((torrent, state) => observed.Enqueue((torrent.State, state)));
        using var poller = new TestPoller(events);
        using var first = poller.Subscribe(Fast);
        using var second = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            poller.Reply(Torrent(1));
            await poller.NextPoll();
            poller.Reply(Torrent(1));
            await poller.NextPoll();
            poller.Reply(Torrent(1, TorrentState.Seeding));
            await poller.NextPoll();
            Assert.Equal(new[] {
                (TorrentState.None, TorrentState.Downloading),
                (TorrentState.Downloading, TorrentState.Seeding)
            }, observed.ToArray());
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task FailedPollRetriesWithoutLosingSubscriptions()
    {
        using var poller = new TestPoller(CreateEvents()) { FailNext = true };
        using var subscription = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Equal(1, poller.Count);
            Assert.Equal(2, await poller.NextPoll());
            poller.Reply(Torrent(1));
            Assert.Single((await Read(subscription)).FullUpdate);
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task EventOverloadKeepsAllRegisteredProvidersPollingAndDisposesTogether()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["DataProviders:rtorrent:rt:Url"] = "unused",
            ["DataProviders:qbittorrent:qb:Url"] = "unused",
            ["DataProviders:transmission:tr:Url"] = "unused"
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<RegisteredDataProviders>();
        services.AddSingleton<EventsService>(sp => new EventsService(NullLogger<EventsService>.Instance, sp.GetRequiredService<RegisteredDataProviders>()));
        foreach (var (key, type) in new[] { ("rt", DataProviderType.rtorrent), ("qb", DataProviderType.qbittorrent), ("tr", DataProviderType.transmission) }) {
            services.AddKeyedSingleton<TorrentPollingService>(key, (sp, _) => new TestPoller(sp.GetRequiredService<EventsService>()));
            services.AddKeyedSingleton<RegisteredDataProvider>(key, (sp, _) => new RegisteredDataProvider(sp, type, key));
        }
        using var provider = services.BuildServiceProvider();
        var pollers = provider.GetRequiredService<RegisteredDataProviders>().GetDataProviders().Select(x => (TestPoller)x.TorrentPolling).ToArray();
        foreach (var poller in pollers)
            await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            using var subscription = provider.GetRequiredService<EventsService>().SubscribeToTorrentChangedStateEvent((_, _) => { }, Fast);
            foreach (var poller in pollers) {
                await poller.NextPoll();
                poller.Reply(Torrent(1));
                await poller.NextPoll();
            }
            subscription.Dispose();
            foreach (var poller in pollers)
                poller.Reply(Torrent(1));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.All(pollers, x => Assert.Equal(2, x.Count));
        } finally {
            foreach (var poller in pollers)
                await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DisposingOneSubscriptionUnblocksItsReaderAndLeavesOthersRunning()
    {
        using var poller = new TestPoller(CreateEvents());
        using var first = poller.Subscribe(Fast);
        using var second = poller.Subscribe(Fast);
        await poller.StartAsync(TestContext.Current.CancellationToken);
        try {
            await poller.NextPoll();
            var pending = first.GetChanges(false, TestContext.Current.CancellationToken);
            first.Dispose();
            Assert.Null(await pending.WaitAsync(Timeout, TestContext.Current.CancellationToken));
            Assert.Null(await first.GetChanges(false, TestContext.Current.CancellationToken));
            poller.Reply(Torrent(1));
            Assert.Single((await Read(second)).FullUpdate);
            Assert.Equal(2, await poller.NextPoll());
        } finally {
            await poller.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsInvalidIntervals(int Milliseconds)
    {
        using var poller = new TestPoller(CreateEvents());
        Assert.Throws<ArgumentOutOfRangeException>(() => poller.Subscribe(TimeSpan.FromMilliseconds(Milliseconds)));
    }
}
