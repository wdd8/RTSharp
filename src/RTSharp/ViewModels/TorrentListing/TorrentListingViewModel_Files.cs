using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;
using System.Reactive.Linq;
using Serilog;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using RTSharp.Core;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using System.Diagnostics;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
    {
        private sealed record FilesUpdate(Models.Torrent FetchedFor, (Models.File Root, Models.File[] Leaves)? FullUpdate, IList<FileDelta>? Deltas);

        private Channel<FilesUpdate>? FilesChanges;

        private Channel<(Models.Torrent, IList<PieceState>)>? PiecesChanges;

#if DEBUG
        private bool GetFilesChangesRunning = false;
        private bool GetPiecesChangesRunning = false;
#endif

        private async Task FilesTasks(Models.Torrent Torrent, CancellationToken SelectionChange)
        {
            FilesChanges = Channel.CreateUnbounded<FilesUpdate>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true
            });

            PiecesChanges = Channel.CreateBounded<(Models.Torrent, IList<PieceState>)>(new BoundedChannelOptions(1) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            await Task.WhenAll(
                await Task.Factory.StartNew(FilesModelUpdates, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()),
                await Task.Factory.StartNew(PiecesModelUpdates, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()),
                await Task.Factory.StartNew(() => GetFilesChanges(Torrent, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default),
                await Task.Factory.StartNew(() => GetPiecesChanges(Torrent, SelectionChange), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
            );
        }

        private async Task PiecesModelUpdates()
        {
            Models.Torrent? lastFetchedFor = null;

            if (PiecesChanges == null)
                throw new NullReferenceException(nameof(PiecesChanges));

            await foreach (var (fetchedFor, pieces) in PiecesChanges.Reader.ReadAllAsync()) {
                GeneralInfoViewModel.Pieces = pieces;

                lastFetchedFor = fetchedFor;
            }
        }

        private async Task FilesModelUpdates()
        {
            if (FilesChanges == null)
                throw new NullReferenceException(nameof(FilesChanges));

            Models.Torrent? lastFetchedFor = null;
            Models.File[]? leaves = null;
            FilesViewModel.ClearRoot();

            await foreach (var update in FilesChanges.Reader.ReadAllAsync()) {
                if (lastFetchedFor == null || !update.FetchedFor.Hash.SequenceEqual(lastFetchedFor.Hash) || update.FetchedFor.DataOwner != lastFetchedFor.DataOwner) {
                    FilesViewModel.ClearRoot();
                    leaves = null;
                }

                if (update.FullUpdate != null) {
                    FilesViewModel.SetRoot(update.FullUpdate.Value.Root);
                    leaves = update.FullUpdate.Value.Leaves;
                } else if (update.Deltas != null && leaves != null && update.Deltas.Count > 0) {
                    foreach (var delta in update.Deltas) {
                        if (delta.Index < 0 || delta.Index >= leaves.Length) {
                            // TODO: force full update?
                            continue;
                        }
                        leaves[delta.Index]?.ApplyDelta(delta);
                    }

                    if (FilesViewModel.File != null)
                        Models.File.RecomputeFolders(FilesViewModel.File);
                }

                lastFetchedFor = update.FetchedFor;
            }
        }

        private async Task GetFilesChanges(Models.Torrent current, CancellationToken selectionChange)
        {
            if (FilesChanges == null)
                throw new NullReferenceException(nameof(FilesChanges));

#if DEBUG
            Debug.Assert(!GetFilesChangesRunning);
            GetFilesChangesRunning = true;
#endif

            try {
                using var scope = Core.ServiceProvider.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<Config>();
                var fileCache = scope.ServiceProvider.GetRequiredService<TorrentFileCache>();
                var propertiesCache = scope.ServiceProvider.GetRequiredService<TorrentPropertiesCache>();

                bool canUseCached = config.Caching.Value.FilesCachingEnabled && !current.InternalState.HasFlag(TORRENT_STATE.ACTIVE) && (current.Done == 0 || current.Done == 100);
                if (canUseCached) {
                    using var cachedView = CancellationTokenSource.CreateLinkedTokenSource(selectionChange);
                    void onTorrentChanged(object? sender, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName != nameof(Models.Torrent.InternalState) && e.PropertyName != nameof(Models.Torrent.Done))
                            return;

                        if (current.InternalState.HasFlag(TORRENT_STATE.ACTIVE) || (current.Done != 0 && current.Done != 100)) {
                            canUseCached = false;
                            cachedView.Cancel();
                        }
                    }

                    current.PropertyChanged += onTorrentChanged;

                    var cachedFiles = await fileCache.GetCachedFileEntries(current.Hash);
                    var cachedProps = await propertiesCache.GetCachedTorrentProperties(current.Hash);

                    if (cachedFiles.Any() && cachedProps != null) {
                        FilesViewModel.MultiFile = cachedProps.IsMultiFile;
                        var built = Models.File.FromCache(current, cachedProps.IsMultiFile, cachedFiles, current.Done == 100);
                        FilesChanges.Writer.TryWrite(new FilesUpdate(current, built, null));

                        try {
                            await Task.Delay(-1, cachedView.Token);
                        } catch {
                        } finally {
                            current.PropertyChanged -= onTorrentChanged;
                        }

                        Log.Logger.Debug("Torrent state changed, switching to live updates");
                    }
                }

                if (current.DataOwner.Instance.Capabilities.GetFileChanges)
                    await StreamFileChanges(current, config, canUseCached, fileCache, propertiesCache, selectionChange);
                else
                    await PollFileChanges(current, config, canUseCached, fileCache, propertiesCache, selectionChange);
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetFilesChanges task has died.");
                throw;
            } finally {
                FilesChanges.Writer.Complete();
                GetFilesChangesRunning = false;
            }
        }

        private async Task StreamFileChanges(Models.Torrent current, Config config, bool canUseCached, TorrentFileCache fileCache, TorrentPropertiesCache propertiesCache, CancellationToken selectionChange)
        {
            ChannelReader<TorrentFilesUpdate> reader;
            try {
                reader = await current.DataOwner.Instance.GetFileChanges(current.ToPluginModel(), config.Behavior.Value.FilesPollingInterval, selectionChange);
            } catch {
                return;
            }

            var firstFull = true;
            try {
                await foreach (var update in reader.ReadAllAsync(selectionChange)) {
                    if (update.FullUpdate != null) {
                        var fullUpdate = update.FullUpdate.Value;

                        FilesViewModel.MultiFile = fullUpdate.MultiFile;

                        var domain = Models.File.FromPluginModel(current.Name, fullUpdate);
                        FilesChanges!.Writer.TryWrite(new FilesUpdate(current, domain, null));

                        if (firstFull && canUseCached) {
                            firstFull = false;
                            _ = Task.WhenAll(
                                fileCache.AddCachedFileEntries(current.Hash, fullUpdate.Files),
                                propertiesCache.AddCachedTorrentProperties(current.Hash, fullUpdate.MultiFile)
                            );
                        }
                    } else {
                        FilesChanges!.Writer.TryWrite(new FilesUpdate(current, null, update.Changes));
                    }
                }
            } catch (Exception ex) {
                Log.Logger.Verbose(ex, "StreamFileChanges task has died.");
                return;
            }
        }

        private async Task PollFileChanges(Models.Torrent current, Config config, bool canUseCached, TorrentFileCache fileCache, TorrentPropertiesCache propertiesCache, CancellationToken selectionChange)
        {
            while (!selectionChange.IsCancellationRequested) {
                var delayTask = Task.Delay(config.Behavior.Value.FilesPollingInterval, selectionChange);

                TorrentFiles files;
                try {
                    files = (await current.DataOwner.Instance.GetFiles(new List<Torrent> { current.ToPluginModel() }, selectionChange)).FirstOrDefault().Value;

                    if (files == default)
                        goto end;

                    if (files.Files.Any()) {
                        FilesViewModel.MultiFile = files.MultiFile;
                        FilesChanges!.Writer.TryWrite(new FilesUpdate(current, Models.File.FromPluginModel(current.Name, files), null));
                    }
                } catch {
                    return;
                }

                if (canUseCached) {
                    _ = Task.WhenAll(
                        fileCache.AddCachedFileEntries(current.Hash, files.Files),
                        propertiesCache.AddCachedTorrentProperties(current.Hash, files.MultiFile)
                    );
                }

end:
                try {
                    await delayTask;
                } catch { }
            }
        }

        private async Task GetPiecesChanges(Models.Torrent current, CancellationToken selectionChange)
        {
            if (PiecesChanges == null)
                throw new NullReferenceException(nameof(PiecesChanges));

#if DEBUG
            Debug.Assert(!GetPiecesChangesRunning);
            GetPiecesChangesRunning = true;
#endif

            try {
                while (!selectionChange.IsCancellationRequested) {
                    if (!current.DataOwner.Instance.Capabilities.GetPieces) {
                        PiecesChanges.Writer.TryWrite((current, []));
                        await Task.Delay(-1, selectionChange);
                    }

                    using var scope = Core.ServiceProvider.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<Config>();

                    var delayTask = Task.Delay(config.Behavior.Value.FilesPollingInterval, selectionChange);

                    IList<PieceState> pieces;

                    if (current.Done == 100) {
                        pieces = [ PieceState.Downloaded ];
                        PiecesChanges.Writer.TryWrite((current, pieces)); // TODO: listen to Done% change instead?
                    } else if (current.Done == 0) {
                        pieces = [ PieceState.NotDownloaded ];
                        PiecesChanges.Writer.TryWrite((current, pieces)); // TODO: listen to Done% change instead?
                    } else {
                        try {
                            pieces = (await current!.DataOwner.Instance.GetPieces(new List<Torrent> { current.ToPluginModel() }, selectionChange)).FirstOrDefault().Value;

                            if (pieces == null)
                                goto end;

                            PiecesChanges.Writer.TryWrite((current, pieces));
                        } catch {
                            return;
                        }
                    }

end:
                    try {
                        await delayTask;
                    } catch { }
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetPiecesChanges task has died.");
                throw;
            } finally {
                PiecesChanges.Writer.Complete();
                GetPiecesChangesRunning = false;
            }
        }
    }
}
