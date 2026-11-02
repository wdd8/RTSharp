using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Scripts;
using RTSharp.Scripts.CrossSeedSearch;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using System.Collections.ObjectModel;
using System.Reactive.Disposables;

namespace RTSharp.CrossSeed.Plugin.ViewModels;

public record SearchSource(string Name, Torrent[] Torrents);

public partial class ResultRow(SearchSource Search, string Indexer, string Title, string Outcome, CrossSeedMatch? Match) : ObservableObject
{
    public Torrent[] Torrents { get; } = Search.Torrents;

    public string Source { get; } = Search.Name;

    public string Indexer { get; } = Indexer;

    public string Title { get; } = Title;

    public CrossSeedMatch? Match { get; } = Match;

    [ObservableProperty]
    public partial string Outcome { get; set; } = Outcome;

    [ObservableProperty]
    public partial bool CanAdd { get; set; } = Match != null;
}

public partial class MainWindowViewModel(IPluginHost<CrossSeedPluginConfig> Host, IReadOnlyList<Torrent> Torrents) : ObservableViewModel
{
    private readonly CancellationTokenSource Cancellation = new();

    public ObservableCollection<ResultRow> Results { get; } = [];

    public Window Owner { get; set; } = null!; // view set

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Starting...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; set; } = true;

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Cancellation.Cancel();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Add(ResultRow Row)
    {
        var source = Row.Torrents[0].DataOwner;

        var target = await Host.SelectDuplicationTarget(Owner, Row.Torrents[0], x =>
            x.Capabilities.AddTorrent && x.Capabilities.FastResumeOnAdd && x.Capabilities.StartTorrent);

        if (target == null)
            return;

        var (provider, remotePath) = target.Value;

        // On the same server the files are linked from the source, so they stay next to it
        await AddMatch(Row, provider, provider.Host.Config.Plugin.ServerId == source.Host.Config.Plugin.ServerId ? Row.Torrents[0].RemotePath : remotePath);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AddDefault(ResultRow Row) => AddMatch(Row, Row.Torrents[0].DataOwner, Row.Torrents[0].RemotePath);

    public override void OnContextPopulated()
    {
        // Closing the window cancels the run
        AddDisposable(Disposable.Create(Cancellation.Cancel));

        _ = Run();
    }

    private async Task AddMatch(ResultRow Row, IDataProvider Target, string BaseDirectory)
    {
        var match = Row.Match!;

        Row.CanAdd = false;
        Row.Outcome = "Adding...";

        try {
            var hash = await Host.DuplicateTorrentAs(Row.Torrents, Target, BaseDirectory, match.Torrent, Host.Config.LinkOptions);

            // The daemons fall back to a normal add when fast resume can't apply, and it's added stopped either way
            var added = await Target.GetTorrent(hash);
            if (!added.IsCompleted) {
                Row.Outcome = "Added, but fast resume failed";
                return;
            }

            if (Target.Capabilities.SetLabels)
                await Target.SetLabels([ (hash, [ .. Row.Torrents.SelectMany(x => x.Labels).Distinct(), "cross-seed" ]) ]);

            Row.Outcome = match.Linked && Target.Host.Config.Plugin.ServerId == Row.Torrents[0].DataOwner.Host.Config.Plugin.ServerId ? "Added, linked" : "Added";
        } catch (Exception ex) {
            Host.Logger.Error(ex, "Adding a cross-seed of {Torrent} failed", Row.Source);
            Row.Outcome = $"Failed to add: {ex.Message}";
            Row.CanAdd = true;
        }
    }

    private async Task Run()
    {
        var config = Host.Config;

        void report(SearchSource Search, string Indexer, string Title, string Outcome, CrossSeedMatch? Match = null) =>
            Dispatcher.UIThread.Post(() => Results.Add(new ResultRow(Search, Indexer, Title, Outcome, Match)));

        async Task<T?> runScript<T>(IDaemonService Daemon, string Script, ScriptMessage Input, string Name) where T : class, IScriptMessage<T>
        {
            var id = await Daemon.RunCustomScript(Script, "Cross-seed", [ Input ]);

            using var registration = Cancellation.Token.Register(() => _ = Daemon.QueueScriptCancellation(id));

            try {
                await using var channel = Daemon.OpenScriptChannel(id, Cancellation.Token);

                var result = channel.Read<T>(Cancellation.Token).AsTask();

                await Daemon.GetScriptProgress(id, progress => {
                    Dispatcher.UIThread.Post(() => ProgressText = $"{Name}: {progress.Text}");
                });

                return await result;
            } finally {
                await Daemon.RemoveScriptSession(id);
            }
        }

        /*
         * Single-file episodes of one season on one data provider can be searched as that season, if the user wants to.
         * Everything else gets its own search, which reports why a torrent can't be cross seeded.
         */
        async Task<SearchSource[]> findSeasons(IGrouping<IDaemonService, Torrent> Group)
        {
            string[] videoExtensions = [ ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm", ".flv", ".vob", ".ogm", ".divx" ];

            var candidates = Group.Where(x => {
                var capabilities = x.DataOwner.Capabilities;
                var cap = capabilities.GetFiles && capabilities.GetDotTorrent && capabilities.AddTorrent && capabilities.FastResumeOnAdd && capabilities.StartTorrent;

                return cap && !x.MagnetDummy && x.IsCompleted && videoExtensions.Contains(Path.GetExtension(x.Name), StringComparer.OrdinalIgnoreCase);
            }).ToArray();

            var inSeason = new HashSet<Torrent>();
            var seasons = new List<SearchSource>();

            if (candidates.Length > 1) {
                try {
                    var singleFile = new List<Torrent>();

                    foreach (var provider in candidates.GroupBy(x => x.DataOwner)) {
                        var files = await provider.Key.GetFiles([ .. provider ], Cancellation.Token);
                        singleFile.AddRange(provider.Where(x => files.TryGetValue(x.Hash, out var torrentFiles) && !torrentFiles.MultiFile && torrentFiles.Files.All(y => y.Priority != Shared.Abstractions.File.PRIORITY.DONT_DOWNLOAD)));
                    }

                    var episodes = new List<(Torrent Torrent, ReleaseNameParser.ReleaseName Release)>(singleFile.Count);

                    foreach (var torrent in singleFile) {
                        var release = await ReleaseNameParser.Parse(torrent.Name, Cancellation.Token);

                        if (release is { Type: "episode", Series: > 0, Episode: > 0, Title.Length: > 0 })
                            episodes.Add((torrent, release));
                    }

                    var groups = episodes
                        .GroupBy(x => (x.Torrent.DataOwner, Title: x.Release.Title.ToLowerInvariant(), x.Release.Year, x.Release.Series))
                        .Where(x => x.Count() > 1 && x.DistinctBy(y => y.Release.Episode).Count() == x.Count());

                    foreach (var group in groups) {
                        var name = $"{group.First().Release.Title} S{group.Key.Series:00}";

                        var answer = await Dispatcher.UIThread.InvokeAsync(() => MessageBoxManager.GetMessageBoxStandard(
                            "RT# - Cross-seed",
                            $"{group.Count()} episodes of {name} are selected.\n\nSearch for the season instead of each episode?",
                            ButtonEnum.YesNo,
                            Icon.Question
                        ).ShowWindowDialogAsync(Owner));

                        if (answer != ButtonResult.Yes)
                            continue;

                        var torrents = group.OrderBy(x => x.Release.Episode).Select(x => x.Torrent).ToArray();

                        seasons.Add(new SearchSource($"{name} ({torrents.Length} episodes)", torrents));
                        inSeason.UnionWith(torrents);
                    }
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    Host.Logger.Error(ex, "Looking for seasons in the selected torrents failed");
                }
            }

            return [ .. seasons, .. Group.Where(x => !inSeason.Contains(x)).Select(x => new SearchSource(x.Name, [ x ])) ];
        }

        async Task processSearch(IDaemonService Daemon, SearchSource Search)
        {
            var owner = Search.Torrents[0].DataOwner;
            var capabilities = owner.Capabilities;

            if (!capabilities.GetDotTorrent) {
                report(Search, "", "", "The client can't provide the .torrent");
                return;
            }

            if (!capabilities.AddTorrent || !capabilities.FastResumeOnAdd || !capabilities.StartTorrent) {
                report(Search, "", "", "The client can't add torrents with fast resume");
                return;
            }

            // WantedSize can't tell if all files are wanted, as qbittorrent leaves padding out of it but not out of Size
            if (Search.Torrents.Any(x => x.MagnetDummy || !x.IsCompleted)
                || (await owner.GetFiles(Search.Torrents, Cancellation.Token)).Any(x => x.Value.Files.Any(y => y.Priority == Shared.Abstractions.File.PRIORITY.DONT_DOWNLOAD))) {
                report(Search, "", "", "Needs to be complete with all files wanted");
                return;
            }

            var sources = new List<CrossSeedSource>(Search.Torrents.Length);
            foreach (var torrent in Search.Torrents)
                sources.Add(new CrossSeedSource((await owner.GetDotTorrents([ torrent ])).Single().Value, torrent.RemotePath));

            var result = await runScript<CrossSeedSearchResult>(Daemon, DaemonScripts.CrossSeedSearch,
                ScriptMessage.Of(new CrossSeedSearchInput(
                    Sources: [ ..sources ],
                    Indexers: [ ..config.Indexers ],
                    ExistingInfoHashes: [ .. Host.Torrents.Select(x => Convert.ToHexStringLower(x.Hash)) ]
                )), Search.Name);

            if (result == null) {
                report(Search, "", "", "No response from daemon");
                return;
            }

            foreach (var skip in result.Skipped)
                report(Search, skip.Indexer, skip.Title, skip.Reason);

            foreach (var match in result.Matches)
                report(Search, match.Indexer, match.Title, "Match", match);
        }

        async Task processDaemon(IDaemonService Daemon, SearchSource[] Searches)
        {
            var first = true;

            foreach (var search in Searches) {
                if (!first)
                    await Task.Delay(TimeSpan.FromSeconds(config.SearchDelaySeconds), Cancellation.Token);
                first = false;

                try {
                    await processSearch(Daemon, search);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    Host.Logger.Error(ex, "Cross-seed failed for {Torrent}", search.Name);
                    report(search, "", "", $"Failed: {ex.Message}");
                }
            }
        }

        if (config.Indexers.Count == 0) {
            ProgressText = "Not configured. Set this up in the plugin settings.";
            IsRunning = false;
            return;
        }

        try {
            /*
             * Torrents with the same path find the same matches, but in separate searches that can't see each other's
             * planned links. This doesn't cover a match linked into a new folder that another search also uses, which
             * only gets caught by fast verify when it's added.
             */
            var samePath = Torrents
                .GroupBy(x => (x.DataOwner.Host.AttachedDaemonService, x.RemotePath.TrimEnd('/'), x.Name))
                .Where(x => x.Count() > 1)
                .SelectMany(x => x)
                .ToHashSet();

            foreach (var torrent in samePath)
                report(new SearchSource(torrent.Name, [ torrent ]), "", "", "Another selected torrent has the same path");

            var searches = new List<(IDaemonService Daemon, SearchSource[] Searches)>();

            // One server at a time, so that its prompts don't overlap with the prompts of another
            foreach (var group in Torrents.Where(x => !samePath.Contains(x)).GroupBy(x => x.DataOwner.Host.AttachedDaemonService))
                searches.Add((group.Key, await findSeasons(group)));

            await Task.WhenAll(searches.Select(x => processDaemon(x.Daemon, x.Searches)));
            Dispatcher.UIThread.Post(() => ProgressText = "Done");
        } catch (OperationCanceledException) {
            Dispatcher.UIThread.Post(() => ProgressText = "Stopped");
        } finally {
            Dispatcher.UIThread.Post(() => IsRunning = false);
        }
    }
}
