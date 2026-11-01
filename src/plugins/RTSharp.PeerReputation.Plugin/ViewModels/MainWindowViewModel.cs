using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using RTSharp.Scripts.PeerReputation;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;

namespace RTSharp.PeerReputation.Plugin.ViewModels;

public partial class MainWindowViewModel : ObservableViewModel
{
    private readonly IReadOnlyList<IDaemonService> Daemons;
    private readonly Guid ScriptOwnerId;
    private readonly CancellationTokenSource Lifetime = new();

    public ObservableCollection<PeerAsnViewModel> PeerAsns { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private partial bool IsLoading { get; set;}

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowEmptyState => IsEmpty && !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private partial PeerAsnViewModel? SelectedPeerAsn { get; set; }

    public bool HasSelection => SelectedPeerAsn != null;
    public bool IsEmpty => PeerAsns.Count == 0;

    public MainWindowViewModel(IReadOnlyList<IDaemonService> daemons, Guid scriptOwnerId)
    {
        this.Daemons = daemons;
        this.ScriptOwnerId = scriptOwnerId;
        PeerAsns = [];
        PeerAsns.CollectionChanged += OnPeerAsnsChanged;
        AddDisposable(System.Reactive.Disposables.Disposable.Create(
            () => PeerAsns.CollectionChanged -= OnPeerAsnsChanged));
        AddDisposable(System.Reactive.Disposables.Disposable.Create(() => {
            Lifetime.Cancel();
            Lifetime.Dispose();
        }));
    }

    public override void OnContextPopulated() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading || Lifetime.IsCancellationRequested)
            return;

        IsLoading = true;
        ErrorMessage = null;
        try {
            var results = await Task.WhenAll(Daemons.Select(async daemon => {
                try {
                    return (Daemon: daemon, Result: await GetAsnList(daemon, Lifetime.Token), Error: (string?)null);
                } catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) {
                    return (Daemon: daemon, Result: (PeerAsnListResult?)null, Error: (string?)null);
                } catch (Exception ex) {
                    return (Daemon: daemon, Result: (PeerAsnListResult?)null, Error: $"{daemon.Id}: {ex.Message}");
                }
            }));

            if (Lifetime.IsCancellationRequested)
                return;

            var selected = SelectedPeerAsn is { } current ? (current.Daemon, current.PeerAsn) : ((string, uint)?)null;
            PeerAsns.Clear();
            foreach (var (daemon, result, _) in results) {
                if (result is null)
                    continue;
                foreach (var info in result.PeerAsns) {
                    var row = new PeerAsnViewModel {
                        Daemon = daemon.Id,
                        PeerAsn = info.PeerAsn,
                        AverageDownloadSpeed = info.AverageDownloadSpeed,
                        AverageUploadSpeed = info.AverageUploadSpeed,
                        PeerOccurrenceCount = info.PeerOccurrenceCount
                    };
                    row.UpdateTrackerDistribution(info.TrackerDistribution.Select(x =>
                        new KeyValuePair<string, ulong>(x.Tracker, x.PeerCount)));
                    PeerAsns.Add(row);
                }
            }

            SelectedPeerAsn = selected is { } key ?
                PeerAsns.FirstOrDefault(x => x.Daemon == key.Item1 && x.PeerAsn == key.Item2) :
                null;
            ErrorMessage = string.Join(Environment.NewLine, results.Where(x => x.Error != null).Select(x => x.Error));
        } finally {
            if (!Lifetime.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private async Task<PeerAsnListResult> GetAsnList(IDaemonService daemon, CancellationToken cancellationToken)
    {
        var installed = (await daemon.ListInstalledScripts()).FirstOrDefault(x => x.Id == ScriptOwnerId.ToString() && x.OwnerId == ScriptOwnerId);
        if (installed is not { State: TASK_STATE.RUNNING, SessionId: { } sessionId })
            throw new InvalidOperationException("Peer Reputation script is not running");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var channel = daemon.OpenScriptChannel(sessionId, timeout.Token);
        await channel.Send(new PeerReputationCommandRequest((int)PeerReputationCommand.GetAsnList));
        return await channel.Read<PeerAsnListResult>(timeout.Token) ?? throw new InvalidOperationException("Peer Reputation script closed without returning the ASN list");
    }

    private void OnPeerAsnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        if (SelectedPeerAsn is not null && !PeerAsns.Contains(SelectedPeerAsn))
            SelectedPeerAsn = null;
    }
}

public partial class PeerAsnViewModel : ObservableObject
{
    public required string Daemon { get; init; }
    public required uint PeerAsn { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AverageDownloadSpeedDisplay))]
    private partial ulong AaverageDownloadSpeed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AverageUploadSpeedDisplay))]
    private partial ulong AverageUploadSpeed { get; set; }

    public string AverageDownloadSpeedDisplay => Converters.GetSIDataSize(AverageDownloadSpeed) + "/s";
    public string AverageUploadSpeedDisplay => Converters.GetSIDataSize(AverageUploadSpeed) + "/s";

    [ObservableProperty]
    public partial ulong PeerOccurrenceCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackers))]
    private IReadOnlyList<TrackerDistributionViewModel> trackerDistribution = [];

    public bool HasTrackers => TrackerDistribution.Count > 0;

    public void UpdateTrackerDistribution(IEnumerable<KeyValuePair<string, ulong>> peerCounts)
    {
        var counts = peerCounts.GroupBy(x => x.Key)
            .Select(group => (Tracker: group.Key, Count: group.Sum(x => (decimal)x.Value)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count).ThenBy(x => x.Tracker).ToArray();
        var total = counts.Sum(x => x.Count);
        TrackerDistribution = counts.Select(x => new TrackerDistributionViewModel(x.Tracker, x.Count, x.Count / total * 100m)).ToArray();
    }
}

public sealed record TrackerDistributionViewModel(string Tracker, decimal PeerCount, decimal Share);
