using System.Collections.Concurrent;
using System.Net;
using Dapper;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Daemon.Services.TorrentPoll;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;
using Xunit;
using Main = RTSharp.Scripts.PeerReputation.Main;
using Torrent = RTSharp.Daemon.Protocols.DataProvider.Torrent;

namespace RTSharp.Daemon.Tests;

public sealed class PeerReputationTests : IAsyncDisposable
{
    private static readonly TimeSpan TestPollInterval = TimeSpan.FromMilliseconds(20);
    private readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "RTSharp.PeerReputationTests", Guid.NewGuid().ToString("N"));
    private readonly DaemonDatabase Database;
    private readonly ServiceProvider Services;
    private readonly CancellationTokenSource Lifetime = new();
    private readonly TestScript Script;
    private Task? Running;
    private readonly List<IDisposable> Leases = [];

    public PeerReputationTests()
    {
        Database = new DaemonDatabase(Path.Combine(DirectoryPath, "daemon.db"));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["DataProviders:rtorrent:first:Url"] = "unused",
            ["DataProviders:qbittorrent:second:Url"] = "unused"
        }).Build());
        services.AddSingleton<RegisteredDataProviders>();
        services.AddSingleton(sp => new EventsService(NullLogger<EventsService>.Instance, sp.GetRequiredService<RegisteredDataProviders>()));
        foreach (var (key, type) in new[] { ("first", DataProviderType.rtorrent), ("second", DataProviderType.qbittorrent) }) {
            services.AddKeyedSingleton<TorrentPollingService>(key, (sp, _) => new Poller(sp.GetRequiredService<EventsService>()));
            services.AddKeyedSingleton<RegisteredDataProvider>(key, (sp, _) => new RegisteredDataProvider(sp, type, key));
        }
        Services = services.BuildServiceProvider();
        Script = new TestScript(Database, Services.GetRequiredService<RegisteredDataProviders>());
    }

    private sealed class Poller(EventsService Events) : TorrentPollingService(NullLogger<Poller>.Instance, Events)
    {
        public volatile Torrent[] Torrents = [];
        protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current) =>
            new() { FullUpdate = { Current.List.Values.Select(x => x.Clone()) } };
        protected override Task<HistoryEntry> PollAsync(CancellationToken CancellationToken) => Task.FromResult(
            new HistoryEntry(DateTime.UtcNow, Torrents.ToInfoHashDictionary(x => x.Hash.ToByteArray(), x => x.Clone())));
    }

    private sealed class Session : IScriptSession
    {
        public ScriptProgressState Progress { get; } = new();
        public void ProgressChanged(ScriptProgressState? progress = null, bool chainChanged = false) { }
        public void Send<T>(T Message) where T : IScriptMessage<T> { }
        public async ValueTask<T> Rx<T>(CancellationToken CancellationToken) where T : IScriptMessage<T>
        {
            var pending = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            return await pending.Task.WaitAsync(CancellationToken);
        }
    }

    private sealed class TestScript(DaemonDatabase Database, RegisteredDataProviders Providers)
        : Main(NullLogger<Main>.Instance, Database, Providers)
    {
        public readonly ConcurrentDictionary<string, int> Samples = new();
        public readonly ConcurrentDictionary<string, string[]> PeerLists = new();
        public int FailLookups;
        public int Lookups;
        protected override TimeSpan PollInterval => TestPollInterval;
        public void Initialize(DaemonDatabase Database) => EnsureSchema(Database);
        public void Save(string[] Trackers, Dictionary<uint, long> Counts) => SaveDistribution(Trackers, Counts);
        protected override Task<TorrentsPeersReply> GetPeers(RegisteredDataProvider Provider, Torrents Request)
        {
            var peers = PeerLists.GetValueOrDefault(Provider.InstanceKey, ["8.8.8.8"]);
            Samples.AddOrUpdate(Provider.InstanceKey, 1, (_, value) => value + 1);
            return Task.FromResult(new TorrentsPeersReply { Reply = {
                new TorrentsPeersReply.Types.TorrentsPeers { InfoHash = Request.Hashes.Single(), Peers = { peers.Select(Peer) } }
            } });
        }
        protected override Task<Dictionary<IPAddress, uint>> ResolveAsns(HashSet<IPAddress> Peers, CancellationToken Token)
        {
            Interlocked.Increment(ref Lookups);
            if (Interlocked.Decrement(ref FailLookups) >= 0)
                throw new IOException("Transient lookup failure");
            return Task.FromResult(Peers.ToDictionary(x => x, _ => 15169u));
        }
    }

    private static TorrentsPeersReply.Types.Peer Peer(string Address) => new() { IPAddress = ByteString.CopyFrom(IPAddress.Parse(Address).GetAddressBytes()) };
    private Poller GetPoller(string Key) => (Poller)Services.GetRequiredKeyedService<TorrentPollingService>(Key);
    private static Torrent Torrent(TorrentState State, byte Id = 0) => new() {
        Hash = ByteString.CopyFrom(Enumerable.Repeat(Id, 20).ToArray()), State = State,
        PrimaryTracker = new TorrentTracker { Uri = "https://tracker.example/private-passkey/announce" }
    };
    private static readonly byte[] DefaultHash = new byte[20];
    private long Count()
    {
        using var connection = Database.OpenConnection();
        return connection.QuerySingle<long>("SELECT COALESCE(SUM(PeerCount), 0) FROM PeerReputationTrackerDistribution");
    }
    private static async Task Until(Func<bool> Condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!Condition()) {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        timeout.Token.ThrowIfCancellationRequested();
    }
    private async Task WaitForTorrentState(string Key, TorrentState State)
    {
        var poller = GetPoller(Key);
        using var subscription = poller.Subscribe(TimeSpan.FromMilliseconds(10));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true) {
            var changes = await subscription.GetChanges(true, timeout.Token);
            if (changes?.FullUpdate.Any(x => x.Hash.Span.SequenceEqual(DefaultHash) && x.State == State) == true)
                return;
        }
    }
    private async Task AssertNoAdditionalSamplesAcrossPolls(string Key, int ExpectedSamples)
    {
        var poller = GetPoller(Key);
        using var subscription = poller.Subscribe(TimeSpan.FromMilliseconds(10));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        for (var i = 0; i < 6; i++) {
            await subscription.GetChanges(true, timeout.Token);
            Assert.Equal(ExpectedSamples, Script.Samples.GetValueOrDefault(Key));
        }
    }
    private async Task WaitForEmptySnapshot(string Key)
    {
        var poller = GetPoller(Key);
        using var subscription = poller.Subscribe(TimeSpan.FromMilliseconds(10));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true) {
            var changes = await subscription.GetChanges(true, timeout.Token);
            if (changes?.FullUpdate.Count == 0)
                return;
        }
    }
    private async Task Start(bool ExistingDownload = false, bool ExistingSeed = false)
    {
        foreach (var key in new[] { "first", "second" }) {
            var poller = GetPoller(key);
            Leases.Add(poller.Subscribe(TestPollInterval));
            if (ExistingDownload)
                poller.Torrents = [Torrent(TorrentState.Downloading | TorrentState.Active)];
            if (ExistingSeed)
                poller.Torrents = [Torrent(TorrentState.Seeding | TorrentState.Complete | TorrentState.Active)];
            await poller.StartAsync(Lifetime.Token);
        }
        if (ExistingDownload || ExistingSeed)
            await Until(() => GetPoller("first").GetLatestHistoryEntry(DefaultHash) != null &&
                GetPoller("second").GetLatestHistoryEntry(DefaultHash) != null);
        Running = Script.Execute(new Session(), Lifetime.Token);
    }

    [Fact]
    public void DeduplicatesAddressesAndTrackerHostsAndExcludesLocalPeers()
    {
        var observation = new Main.TorrentObservation();
        observation.AddPeers(new[] { "8.8.8.8", "::ffff:8.8.8.8", "2001:4860:4860::8888", "192.168.1.1", "127.0.0.1", "::1", "fe80::1", "fc00::1" }.Select(Peer));
        observation.AddPeers([Peer("8.8.8.8"), new()]);
        observation.AddTrackers(["https://TRACKER.example/secret", "udp://tracker.example:80/announce", "DHT", "https://second.example/announce"]);
        Assert.Equal(2, observation.Peers.Count);
        Assert.Equal(new[] { "second.example", "tracker.example" }, observation.Trackers.Order().ToArray());
    }

    [Fact]
    public void AccumulatesCountsAndRollsBackFailures()
    {
        Script.Initialize(Database);
        Script.Save(["tracker.example"], new() { [uint.MaxValue] = 2 });
        using var connection = Database.OpenConnection();
        Script.Save(["tracker.example"], new() { [uint.MaxValue] = 3 });
        Assert.Equal(5, Count());
        Assert.Throws<SqliteException>(() => Script.Save(["tracker.example"], new() { [15169] = 1, [12345] = -1 }));
        Assert.Equal(5, Count());
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM PeerReputationAsns"));
    }

    [Fact]
    public async Task MonitorsEveryDownloadingTorrentInOneProviderSnapshot()
    {
        await Start();
        GetPoller("first").Torrents = [
            Torrent(TorrentState.Downloading | TorrentState.Active, 1),
            Torrent(TorrentState.Downloading | TorrentState.Active, 2)
        ];
        await Until(() => Count() == 2);
        GetPoller("first").Torrents = [
            Torrent(TorrentState.Seeding | TorrentState.Complete, 1),
            Torrent(TorrentState.Seeding | TorrentState.Complete, 2)
        ];
        await Until(() => Count() == 2);
    }

    [Fact]
    public async Task MonitorsExistingDownloadsAcrossPauseResumeAndSeparatesProvidersWithEqualHashes()
    {
        await Start(ExistingDownload: true);
        await Until(() => Count() == 2);
        Script.PeerLists["first"] = [];
        GetPoller("first").Torrents = [Torrent(TorrentState.Paused)];
        await WaitForTorrentState("first", TorrentState.Paused);
        var pausedSamples = Script.Samples["first"];
        await AssertNoAdditionalSamplesAcrossPolls("first", pausedSamples);
        Assert.Equal(2, Count());
        Script.PeerLists["first"] = ["1.1.1.1"];
        Script.FailLookups = 1;
        GetPoller("first").Torrents = [Torrent(TorrentState.Downloading | TorrentState.Active)];
        await Until(() => Script.Samples["first"] > pausedSamples);
        await Until(() => Script.Lookups >= 3);
        GetPoller("first").Torrents = [Torrent(TorrentState.Complete | TorrentState.Stopped)];
        GetPoller("first").Torrents = []; // Queued peer batches survive removal.
        await Until(() => Count() == 3);
        Assert.True(Script.Lookups >= 4);
        GetPoller("second").Torrents = [Torrent(TorrentState.Seeding | TorrentState.Complete | TorrentState.Active)];
        var secondSamples = Script.Samples["second"];
        await Until(() => Script.Samples["second"] > secondSamples);
        Assert.Equal(3, Count());
    }

    [Fact]
    public async Task IgnoresInactiveSeedsAndRetainsAlreadyObservedPeersAfterRemoval()
    {
        await Start();
        GetPoller("first").Torrents = [Torrent(TorrentState.Seeding | TorrentState.Complete)];
        GetPoller("second").Torrents = [Torrent(TorrentState.Downloading | TorrentState.Active)];
        await Until(() => Script.Samples.ContainsKey("second"));
        GetPoller("second").Torrents = [];
        await WaitForEmptySnapshot("second");
        await AssertNoAdditionalSamplesAcrossPolls("first", 0);
        Assert.False(Script.Samples.ContainsKey("first"));
        Assert.Equal(1, Count());
        Assert.Equal(1, Script.Lookups);
    }

    [Fact]
    public async Task MonitorsExistingActiveSeedsAndCountsEachPeerOnce()
    {
        await Start(ExistingSeed: true);
        await Until(() => Count() == 2);
        Script.PeerLists["first"] = ["8.8.8.8", "1.1.1.1"];
        await Until(() => Count() == 3);
        GetPoller("first").Torrents = [Torrent(TorrentState.Paused | TorrentState.Complete)];
        await WaitForTorrentState("first", TorrentState.Paused | TorrentState.Complete);
        var pausedSamples = Script.Samples["first"];
        await AssertNoAdditionalSamplesAcrossPolls("first", pausedSamples);
        GetPoller("first").Torrents = [Torrent(TorrentState.Seeding | TorrentState.Complete | TorrentState.Active)];
        await Until(() => Script.Samples["first"] > pausedSamples);
        Assert.Equal(3, Count());
        var seedSamples = Script.Samples["first"];
        GetPoller("first").Torrents = [Torrent(TorrentState.Active)];
        await Until(() => Script.Samples["first"] > seedSamples);
        Assert.Equal(3, Count());
    }

    public async ValueTask DisposeAsync()
    {
        await Lifetime.CancelAsync();
        if (Running != null) {
            try { await Running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
        foreach (var lease in Leases)
            lease.Dispose();
        foreach (var key in new[] { "first", "second" })
            await GetPoller(key).StopAsync(CancellationToken.None);
        await Services.DisposeAsync();
        Lifetime.Dispose();
        using var connection = Database.OpenConnection();
        connection.Close();
        SqliteConnection.ClearPool(connection);
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
