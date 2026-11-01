#pragma usings
#pragma contract PeerReputationMessages

using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;
using Torrent = RTSharp.Daemon.Protocols.DataProvider.Torrent;

namespace RTSharp.Scripts.PeerReputation;

public class Main(ILogger<Main> Logger, DaemonDatabase Database, RegisteredDataProviders Providers) : IScript
{
    protected static void EnsureSchema(DaemonDatabase database)
    {
        database.Migrate("PeerReputation",
            (connection, transaction) => connection.Execute("""
                CREATE TABLE PeerReputationAsns (
                    PeerAsn INTEGER NOT NULL PRIMARY KEY CHECK (PeerAsn BETWEEN 0 AND 4294967295),
                    AverageDownloadSpeed INTEGER NOT NULL DEFAULT 0 CHECK (AverageDownloadSpeed >= 0),
                    AverageUploadSpeed INTEGER NOT NULL DEFAULT 0 CHECK (AverageUploadSpeed >= 0),
                    SpeedSampleCount INTEGER NOT NULL DEFAULT 0 CHECK (SpeedSampleCount >= 0)
                );

                CREATE TABLE PeerReputationTrackerDistribution (
                    PeerAsn INTEGER NOT NULL REFERENCES PeerReputationAsns(PeerAsn) ON DELETE CASCADE,
                    Tracker TEXT NOT NULL,
                    PeerCount INTEGER NOT NULL CHECK (PeerCount >= 0),
                    PRIMARY KEY (PeerAsn, Tracker)
                );
                """, transaction: transaction));
    }

    protected virtual TimeSpan PollInterval => TimeSpan.FromSeconds(2);
    private readonly Lock PersistenceLock = new();
    private readonly HashSet<Task> PendingPersistence = [];

    public async Task Execute(IScriptSession Session, CancellationToken CancellationToken)
    {
        EnsureSchema(Database);

        Session.Progress.State = TASK_STATE.RUNNING;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var scans = Providers.GetDataProviders().Select(provider => ScanActiveTorrents(provider, lifetime.Token)).ToArray();
        var commands = CommandHandler(Session, lifetime.Token);
        try {
            await await Task.WhenAny(scans.Append(commands));
        } finally {
            await lifetime.CancelAsync();
            try {
                await Task.WhenAll(scans);
            } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) {
            } finally {
                try {
                    await commands;
                } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) {
                } finally {
                    Task[] pending;
                    lock (PersistenceLock)
                        pending = PendingPersistence.ToArray();
                    await Task.WhenAll(pending);
                }
            }
        }
    }

    private async Task ScanActiveTorrents(RegisteredDataProvider Provider, CancellationToken Token)
    {
        using var subscription = Provider.TorrentPolling.Subscribe(PollInterval);
        var observations = new Dictionary<ByteString, TorrentObservation>();
        while (!Token.IsCancellationRequested) {
            var changes = await subscription.GetChanges(true, Token);
            if (changes == null)
                return;

            var present = new HashSet<ByteString>();
            var samples = new List<(Torrent Torrent, Task<bool> Result)>();
            foreach (var torrent in changes.FullUpdate) {
                present.Add(torrent.Hash);
                var complete = (torrent.State & TorrentState.Complete) != 0;
                var downloading = !complete && (torrent.State & TorrentState.Downloading) != 0;
                var active = (torrent.State & TorrentState.Active) != 0;
                if (!observations.TryGetValue(torrent.Hash, out var observation)) {
                    if (!downloading && !active)
                        continue;
                    observations.Add(torrent.Hash, observation = new TorrentObservation());
                }

                observation.AddTrackers(Provider.TorrentPolling.GetTorrentTrackers(torrent.Hash.ToByteArray()).Select(x => x.Uri));
                if (torrent.PrimaryTracker is { } primary)
                    observation.AddTrackers([primary.Uri]);
                if (downloading || active || (complete && !observation.CompletionSampled))
                    samples.Add((torrent, SamplePeers(Provider, torrent.Hash, observation, Token)));
            }

            await Task.WhenAll(samples.Select(x => x.Result));
            foreach (var (torrent, result) in samples) {
                if (await result) {
                    var observation = observations[torrent.Hash];
                    if ((torrent.State & TorrentState.Complete) != 0)
                        observation.CompletionSampled = true;
                    var newPeers = observation.Peers.Except(observation.QueuedPeers).ToArray();
                    if (newPeers.Length == 0)
                        continue;
                    observation.QueuedPeers.UnionWith(newPeers);
                    var batch = new TorrentObservation();
                    batch.Peers.UnionWith(newPeers);
                    foreach (var peer in newPeers)
                        if (observation.PeerSpeeds.TryGetValue(peer, out var speed))
                            batch.PeerSpeeds.Add(peer, speed);
                    batch.Trackers.UnionWith(observation.Trackers);
                    QueuePersistence(Provider, torrent.Hash, batch, Token);
                }
            }
            foreach (var hash in observations.Keys.Where(x => !present.Contains(x)).ToArray())
                observations.Remove(hash);
        }
    }

    private async Task<bool> SamplePeers(RegisteredDataProvider Provider, ByteString Hash, TorrentObservation Observation, CancellationToken Token)
    {
        try {
            var peers = await GetPeers(Provider, new Torrents {
                Hashes = {
                    Hash
                }
            }).WaitAsync(TimeSpan.FromSeconds(30), Token);
            Observation.AddPeers(peers.Reply.Where(x => x.InfoHash == Hash).SelectMany(x => x.Peers));
            return true;
        } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            Logger.LogWarning(ex, "Retrying peer sampling for {Provider}/{Hash}", Provider.InstanceKey, Hash);
            return false;
        }
    }

    private void QueuePersistence(RegisteredDataProvider Provider, ByteString Hash, TorrentObservation Observation, CancellationToken Token)
    {
        lock (PersistenceLock) {
            Task? task = null;
            task = Task.Run(async () => {
                try {
                    await PersistPeerBatch(Provider, Hash, Observation, Token);
                } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
                } catch (Exception ex) {
                    Logger.LogError(ex, "Peer monitoring failed for {Provider}/{Hash}", Provider.InstanceKey, Hash);
                } finally {
                    lock (PersistenceLock)
                        PendingPersistence.Remove(task!);
                }
            });
            PendingPersistence.Add(task);
        }
    }

    private async Task PersistPeerBatch(RegisteredDataProvider Provider, ByteString Hash, TorrentObservation Observation, CancellationToken Token)
    {
        Dictionary<IPAddress, uint>? asns = null;
        while (true) {
            Token.ThrowIfCancellationRequested();
            try {
                // Keep the sampled peer batch while retrying lookup or storage, even if the torrent is removed.
                asns ??= await ResolveAsns(Observation.Peers, Token);
                Token.ThrowIfCancellationRequested();
                var counts = asns.Values.GroupBy(x => x).ToDictionary(x => x.Key, x => x.LongCount());
                var speeds = Observation.PeerSpeeds
                    .Where(x => asns.ContainsKey(x.Key))
                    .GroupBy(x => asns[x.Key])
                    .ToDictionary(x => x.Key, x => new AsnSpeedSample(
                        ToDatabaseSpeed(x.Average(peer => (decimal)peer.Value.DownloadSpeed)),
                        ToDatabaseSpeed(x.Average(peer => (decimal)peer.Value.UploadSpeed)),
                        x.LongCount()
                    ));
                SaveDistribution(Observation.Trackers, counts, speeds);
                return;
            } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                Logger.LogWarning(ex, "Retrying peer monitoring for {Provider}/{Hash}", Provider.InstanceKey, Hash);
            }
            await Task.Delay(PollInterval, Token);
        }
    }

    protected virtual Task<TorrentsPeersReply> GetPeers(RegisteredDataProvider Provider, Torrents Request) => Provider.Type switch {
        DataProviderType.rtorrent => Provider.Resolve<RTSharp.Daemon.Services.rtorrent.Grpc>().GetTorrentsPeers(Request),
        DataProviderType.qbittorrent => Provider.Resolve<RTSharp.Daemon.Services.qbittorrent.Grpc>().GetTorrentsPeers(Request),
        DataProviderType.transmission => Provider.Resolve<RTSharp.Daemon.Services.transmission.Grpc>().GetTorrentsPeers(Request),
        _ => throw new NotSupportedException($"Unsupported provider {Provider.Type}")
    };

    protected virtual async Task<Dictionary<IPAddress, uint>> ResolveAsns(HashSet<IPAddress> Peers, CancellationToken Token)
    {
        var results = await TeamCymruWhoisClient.Shared.QueryMany(Peers, Token);
        return results.ToDictionary(x => x.Key, x => x.Value.Asn);
    }

    protected void SaveDistribution(IEnumerable<string> Trackers, IReadOnlyDictionary<uint, long> Counts)
        => SaveDistribution(Trackers, Counts, new Dictionary<uint, AsnSpeedSample>());

    private void SaveDistribution(IEnumerable<string> Trackers, IReadOnlyDictionary<uint, long> Counts, IReadOnlyDictionary<uint, AsnSpeedSample> Speeds)
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var asn in Counts.Keys.Concat(Speeds.Keys).Distinct())
            connection.Execute("INSERT INTO PeerReputationAsns (PeerAsn) VALUES (@PeerAsn) ON CONFLICT (PeerAsn) DO NOTHING",
                new {
                    PeerAsn = (long)asn
                },
                transaction
            );
        foreach (var (asn, count) in Counts) {
            foreach (var tracker in Trackers) {
                connection.Execute("""
                    INSERT INTO PeerReputationTrackerDistribution (PeerAsn, Tracker, PeerCount)
                    VALUES (@PeerAsn, @Tracker, @PeerCount)
                    ON CONFLICT (PeerAsn, Tracker) DO UPDATE SET PeerCount = PeerCount + excluded.PeerCount
                    """,
                    new {
                        PeerAsn = (long)asn,
                        Tracker = tracker,
                        PeerCount = count
                    },
                    transaction
                );
            }
        }
        foreach (var (asn, speed) in Speeds) {
            connection.Execute("""
                UPDATE PeerReputationAsns
                SET AverageDownloadSpeed =
                        (AverageDownloadSpeed * SpeedSampleCount + @DownloadSpeed * @SampleCount)
                        / (SpeedSampleCount + @SampleCount),
                    AverageUploadSpeed =
                        (AverageUploadSpeed * SpeedSampleCount + @UploadSpeed * @SampleCount)
                        / (SpeedSampleCount + @SampleCount),
                    SpeedSampleCount = SpeedSampleCount + @SampleCount
                WHERE PeerAsn = @PeerAsn
                """, new {
                    PeerAsn = (long)asn,
                    speed.DownloadSpeed,
                    speed.UploadSpeed,
                    speed.SampleCount
                }, transaction);
        }
        transaction.Commit();
    }

    private static long ToDatabaseSpeed(decimal Speed) => (long)Math.Min(Speed, long.MaxValue);

    private sealed record AsnSpeedSample(long DownloadSpeed, long UploadSpeed, long SampleCount);

    public readonly record struct PeerSpeed(ulong DownloadSpeed, ulong UploadSpeed);

    public sealed class TorrentObservation
    {
        public HashSet<IPAddress> Peers { get; } = [];
        public Dictionary<IPAddress, PeerSpeed> PeerSpeeds { get; } = [];
        public HashSet<IPAddress> QueuedPeers { get; } = [];
        public HashSet<string> Trackers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool CompletionSampled { get; set; }

        public void AddTrackers(IEnumerable<string> Uris)
        {
            foreach (var value in Uris) {
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "udp" && !string.IsNullOrEmpty(uri.Host))
                    Trackers.Add(uri.IdnHost.ToLowerInvariant());
            }
        }

        public void AddPeers(IEnumerable<TorrentsPeersReply.Types.Peer> Items)
        {
            foreach (var peer in Items) {
                if (peer.IPAddress.Length is not (4 or 16))
                    continue;
                var addr = new IPAddress(peer.IPAddress.Span);
                if (addr.IsIPv4MappedToIPv6)
                    addr = addr.MapToIPv4();
                if (IsPublic(addr) && Peers.Add(addr))
                    PeerSpeeds.Add(addr, new PeerSpeed(peer.DLSpeed, peer.UPSpeed));
            }
        }

        private static bool IsPublic(IPAddress Address)
        {
            if (IPAddress.IsLoopback(Address))
                return false;
            if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                return !Address.IsIPv6LinkLocal && !Address.IsIPv6SiteLocal && !Address.IsIPv6UniqueLocal && !Address.IsIPv6Multicast && !Address.Equals(IPAddress.IPv6Any);
            var bytes = Address.GetAddressBytes();
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
                !(bytes[0] == 169 && bytes[1] == 254) &&
                !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                !(bytes[0] == 192 && bytes[1] == 168) &&
                !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        }
    }

    private async Task CommandHandler(IScriptSession Session, CancellationToken CancellationToken)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var cmd = await Session.Rx<PeerReputationCommandRequest>(CancellationToken);
            switch ((PeerReputationCommand)cmd.Command) {
                case PeerReputationCommand.GetAsnList:
                    using (var connection = Database.OpenConnection()) {
                        var trackerRows = connection.Query<TrackerDistributionRow>("""
                            SELECT PeerAsn, Tracker, PeerCount
                            FROM PeerReputationTrackerDistribution
                            ORDER BY PeerAsn, Tracker
                            """).ToLookup(x => x.PeerAsn);

                        var asns = connection.Query<PeerAsnRow>("""
                            SELECT PeerAsn, AverageDownloadSpeed, AverageUploadSpeed,
                                   SpeedSampleCount AS PeerOccurrenceCount
                            FROM PeerReputationAsns
                            ORDER BY PeerAsn
                            """)
                            .Select(x => new PeerAsnInfo(
                                x.PeerAsn,
                                x.AverageDownloadSpeed,
                                x.AverageUploadSpeed,
                                x.PeerOccurrenceCount,
                                [.. trackerRows[x.PeerAsn].Select(tracker => new TrackerDistributionInfo(
                                    tracker.Tracker, tracker.PeerCount
                                ))]
                            )
                        );

                        Session.Send(new PeerAsnListResult([.. asns]));
                    }
                    break;
                default:
                    Logger.LogWarning($"Unknown command: {cmd.Command}");
                    break;
            }
        }
    }

    public sealed class PeerAsnRow
    {
        public uint PeerAsn { get; set; }
        public ulong AverageDownloadSpeed { get; set; }
        public ulong AverageUploadSpeed { get; set; }
        public ulong PeerOccurrenceCount { get; set; }
    }

    public sealed class TrackerDistributionRow
    {
        public uint PeerAsn { get; set; }
        public string Tracker { get; set; } = "";
        public ulong PeerCount { get; set; }
    }
}
