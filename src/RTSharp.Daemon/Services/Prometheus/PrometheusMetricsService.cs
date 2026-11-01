using Microsoft.Extensions.Options;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Services.TorrentPoll;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Utils;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RTSharp.Daemon.Services.Prometheus;

public class PrometheusMetricsService(
    RegisteredDataProviders RegisteredDataProviders,
    IOptionsMonitor<PrometheusOptions> Options,
    ILogger<PrometheusMetricsService> Logger) : BackgroundService
{
    private volatile string MetricsText = Render(Array.Empty<DataProviderMetrics>(), DateTimeOffset.UnixEpoch);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            var options = Options.CurrentValue;
            var interval = ResolveInterval(options.PollInterval);

            if (!options.Enabled) {
                await Task.Delay(interval, stoppingToken);
                continue;
            }

            var subscriptions = new List<(RegisteredDataProvider DataProvider, PollingSubscription Subscription)>();
            try {
                foreach (var dataProvider in RegisteredDataProviders.GetDataProviders())
                    subscriptions.Add((dataProvider, dataProvider.TorrentPolling.Subscribe(interval)));

                if (subscriptions.Count == 0) {
                    MetricsText = Render(Array.Empty<DataProviderMetrics>(), DateTimeOffset.UtcNow);
                    await Task.Delay(interval, stoppingToken);
                    continue;
                }

                using var iterationCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var optionsChanged = Options.OnChange((_, _) => iterationCancellation.Cancel());
                var latestMetrics = new DataProviderMetrics?[subscriptions.Count];
                var metricsGate = new Lock();
                var consumers = new Task[subscriptions.Count];
                for (var x = 0; x < subscriptions.Count; x++) {
                    var (dataProvider, subscription) = subscriptions[x];
                    consumers[x] = ConsumeMetrics(x, dataProvider, subscription, latestMetrics, metricsGate, iterationCancellation.Token);
                }

                try {
                    await Task.WhenAll(consumers);
                } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
                    // Restart subscriptions when Prometheus options are reloaded.
                }
            } finally {
                foreach (var (_, subscription) in subscriptions)
                    subscription.Dispose();
            }
        }
    }

    private async Task ConsumeMetrics(int idx, RegisteredDataProvider dataProvider, PollingSubscription subscription,
        DataProviderMetrics?[] latestMetrics, Lock metricsGate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var metrics = await GetMetrics(dataProvider, subscription, cancellationToken);
            lock (metricsGate) {
                latestMetrics[idx] = metrics;
                var currentMetrics = new List<DataProviderMetrics>(latestMetrics.Length);
                foreach (var current in latestMetrics) {
                    if (current.HasValue)
                        currentMetrics.Add(current.Value);
                }

                currentMetrics.Sort(static (x, y) => string.CompareOrdinal(x.DataProvider, y.DataProvider));
                MetricsText = Render([.. currentMetrics], DateTimeOffset.UtcNow);
            }
        }
    }

    private static TimeSpan ResolveInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            return TimeSpan.FromMinutes(10);
        if (interval < TimeSpan.FromMilliseconds(10))
            return TimeSpan.FromMilliseconds(10);

        var maxInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        return interval > maxInterval ? maxInterval : interval;
    }

    public string Render()
    {
        return MetricsText;
    }

    private static string Render(DataProviderMetrics[] dataProviderMetrics, DateTimeOffset updatedAt)
    {
        var sb = new StringBuilder(EstimateRenderCapacity(dataProviderMetrics));

        sb.AppendLine("# HELP rtsharp_tracker_uploaded_bytes Current sum of torrent uploaded bytes by data provider and tracker.");
        sb.AppendLine("# TYPE rtsharp_tracker_uploaded_bytes gauge");
        foreach (var dataProviderMetric in dataProviderMetrics) {
            foreach (var metric in dataProviderMetric.TrackerTraffic) {
                sb.Append("rtsharp_tracker_uploaded_bytes");
                AppendLabels(sb, dataProviderMetric.DataProvider, metric.Tracker);
                sb.Append(' ');
                sb.Append(CultureInfo.InvariantCulture, $"{metric.Uploaded}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("# HELP rtsharp_tracker_downloaded_bytes Current sum of torrent downloaded bytes by data provider and tracker.");
        sb.AppendLine("# TYPE rtsharp_tracker_downloaded_bytes gauge");
        foreach (var dataProviderMetric in dataProviderMetrics) {
            foreach (var metric in dataProviderMetric.TrackerTraffic) {
                sb.Append("rtsharp_tracker_downloaded_bytes");
                AppendLabels(sb, dataProviderMetric.DataProvider, metric.Tracker);
                sb.Append(' ');
                sb.Append(CultureInfo.InvariantCulture, $"{metric.Downloaded}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("# HELP rtsharp_data_provider_torrent_fetch_duration_seconds Time spent fetching torrents from each data provider during the last completed refresh.");
        sb.AppendLine("# TYPE rtsharp_data_provider_torrent_fetch_duration_seconds gauge");
        foreach (var metric in dataProviderMetrics) {
            sb.Append("rtsharp_data_provider_torrent_fetch_duration_seconds");
            AppendLabels(sb, metric.DataProvider);
            sb.Append(' ');
            sb.Append(CultureInfo.InvariantCulture, $"{metric.FetchDuration.TotalSeconds}");
            sb.AppendLine();
        }

        sb.AppendLine("# HELP rtsharp_tracker_metrics_last_refresh_timestamp_seconds Unix timestamp of the last completed tracker metrics refresh.");
        sb.AppendLine("# TYPE rtsharp_tracker_metrics_last_refresh_timestamp_seconds gauge");
        sb.Append("rtsharp_tracker_metrics_last_refresh_timestamp_seconds ");
        sb.Append(CultureInfo.InvariantCulture, $"{updatedAt.ToUnixTimeSeconds()}");
        sb.AppendLine();

        return sb.ToString();
    }

    private async Task<DataProviderMetrics> GetMetrics(RegisteredDataProvider dataProvider, PollingSubscription subscription, CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();

            var changes = await subscription.GetChanges(true, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var trackerDomains = new Dictionary<string, string>(StringComparer.Ordinal);
            var trafficByTracker = new Dictionary<string, TorrentStats>(StringComparer.Ordinal);

            if (changes is not null) {
                foreach (var torrent in changes.FullUpdate) {
                    var tracker = GetDomain(torrent.PrimaryTracker?.Uri, trackerDomains);
                    ref var traffic = ref CollectionsMarshal.GetValueRefOrAddDefault(trafficByTracker, tracker, out var exists);
                    if (exists) {
                        traffic.Uploaded += torrent.Uploaded;
                        traffic.Downloaded += torrent.Downloaded;
                    } else {
                        traffic = new TorrentStats(torrent.Uploaded, torrent.Downloaded);
                    }
                }
            }

            var metrics = new TrackerStats[trafficByTracker.Count];
            var index = 0;
            foreach (var (tracker, traffic) in trafficByTracker) {
                metrics[index++] = new TrackerStats(tracker, traffic.Uploaded, traffic.Downloaded);
            }

            Array.Sort(metrics, static (x, y) => String.CompareOrdinal(x.Tracker, y.Tracker));

            return new DataProviderMetrics(
                dataProvider.InstanceKey,
                metrics,
                dataProvider.TorrentPolling.LastPollDuration
            );
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (DataProviderFailure ex) {
            Logger.LogError("Failed to refresh metrics for data provider {DataProvider}", dataProvider.InstanceKey);
            ConnectionLog.LogConnectionFailure(Logger, ex);
            return new DataProviderMetrics(
                dataProvider.InstanceKey,
                Array.Empty<TrackerStats>(),
                dataProvider.TorrentPolling.LastPollDuration
            );
        } catch (Exception ex) {
            Logger.LogError(ex, "Failed to refresh metrics for data provider {DataProvider}", dataProvider.InstanceKey);
            return new DataProviderMetrics(
                dataProvider.InstanceKey,
                Array.Empty<TrackerStats>(),
                dataProvider.TorrentPolling.LastPollDuration
            );
        }
    }

    private static string GetDomain(string? uri, Dictionary<string, string> cache)
    {
        if (uri == null)
            return "unknown";

        if (cache.TryGetValue(uri, out var cachedDomain))
            return cachedDomain;

        var domain = UriUtils.GetDomainForTracker(uri);
        if (string.IsNullOrEmpty(domain))
            domain = "unknown";

        cache.Add(uri, domain);
        return domain;
    }

    private static void AppendLabels(StringBuilder sb, string dataProvider, string tracker)
    {
        sb.Append("{data_provider=\"");
        AppendEscapedLabelValue(sb, dataProvider);
        sb.Append("\",tracker=\"");
        AppendEscapedLabelValue(sb, tracker);
        sb.Append("\"}");
    }

    private static void AppendLabels(StringBuilder sb, string dataProvider)
    {
        sb.Append("{data_provider=\"");
        AppendEscapedLabelValue(sb, dataProvider);
        sb.Append("\"}");
    }

    private static void AppendEscapedLabelValue(StringBuilder sb, string value)
    {
        foreach (var c in value) {
            switch (c) {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '\n':
                    sb.Append(@"\n");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
    }

    private static int EstimateRenderCapacity(DataProviderMetrics[] dataProviderMetrics)
    {
        var trackerMetricCount = 0;
        foreach (var metric in dataProviderMetrics)
            trackerMetricCount += metric.TrackerTraffic.Length;

        return 512 + (trackerMetricCount * 240) + (dataProviderMetrics.Length * 160);
    }

    private struct TorrentStats(ulong uploaded, ulong downloaded)
    {
        public ulong Uploaded = uploaded;
        public ulong Downloaded = downloaded;
    }

    private readonly struct TrackerStats(string tracker, ulong uploaded, ulong downloaded)
    {
        public string Tracker { get; } = tracker;
        public ulong Uploaded { get; } = uploaded;
        public ulong Downloaded { get; } = downloaded;
    }

    private readonly struct DataProviderMetrics(string dataProvider, TrackerStats[] trackerTraffic, TimeSpan fetchDuration)
    {
        public string DataProvider { get; } = dataProvider;
        public TrackerStats[] TrackerTraffic { get; } = trackerTraffic;
        public TimeSpan FetchDuration { get; } = fetchDuration;
    }
}
