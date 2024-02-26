using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
    {
        private Channel<(Models.Torrent, IList<Models.File>)> FilesChanges;

        private Channel<(Models.Torrent, IList<PieceState>)> PiecesChanges;

        private async Task FilesTasks(Models.Torrent Torrent, CancellationToken SelectionChange)
        {
            FilesChanges = Channel.CreateUnbounded<(Models.Torrent, IList<Models.File>)>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            PiecesChanges = Channel.CreateUnbounded<(Models.Torrent, IList<PieceState>)>(new UnboundedChannelOptions() {
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

            await foreach (var (fetchedFor, pieces) in PiecesChanges.Reader.ReadAllAsync()) {
                GeneralInfoViewModel.Pieces = pieces;

                lastFetchedFor = fetchedFor;
            }
        }

        private async Task FilesModelUpdates()
        {
            Models.Torrent? lastFetchedFor = null;
            FilesViewModel.Files.Clear();

            await foreach (var (fetchedFor, files) in FilesChanges.Reader.ReadAllAsync()) {
                if (lastFetchedFor == null || !fetchedFor.Hash.SequenceEqual(lastFetchedFor!.Hash) || fetchedFor.Owner != lastFetchedFor.Owner)
                    FilesViewModel.Files.Clear();

                if (FilesViewModel.Files.Count == 0) {
                    foreach (var file in files) {
                        FilesViewModel.Files.Add(file);
                    }
                } else {
                    void update(ObservableCollection<Models.File> Target, IList<Models.File> Source)
                    {
                        foreach (var dst in Target) {
                            foreach (var src in Source) {
                                if (dst.Path == src.Path) {
                                    dst.Update(src);

                                    update(dst.Children, src.Children);
                                }
                            }
                        }
                    }

                    update(FilesViewModel.Files, files);
                }

                lastFetchedFor = fetchedFor;
            }
        }

        private async Task GetFilesChanges(Models.Torrent current, CancellationToken selectionChange)
        {
            try {
                while (!selectionChange.IsCancellationRequested) {
                    using var scope = Core.ServiceProvider.CreateScope();
                    var config = scope.ServiceProvider.GetRequiredService<Config>();
                    var fileCache = scope.ServiceProvider.GetRequiredService<TorrentFileCache>();
                    var propertiesCache = scope.ServiceProvider.GetRequiredService<TorrentPropertiesCache>();

                    var delayTask = Task.Delay(config.Behavior.Value.FilesPollingInterval, selectionChange);

                    bool canUseCached = config.Caching.Value.FilesCachingEnabled && !current.InternalState.HasFlag(TORRENT_STATE.ACTIVE) && (current.Done == 0 || current.Done == 100);
                    if (canUseCached) {
                        var cachedFiles = await fileCache.GetCachedFileEntries(current.Hash);
                        var cachedProps = await propertiesCache.GetCachedTorrentProperties(current.Hash);

                        if (cachedFiles.Any() && cachedProps != null) {
                            FilesChanges.Writer.TryWrite((current, Models.File.FromCache(current, cachedProps.IsMultiFile, cachedFiles, current.Done == 100)));
                            try {
                                await Task.Delay(-1, selectionChange); // TODO: what about state change?
                            } catch { }
                            return;
                        }
                    }
                    (bool MultiFile, IList<File> Files) files;
                    try {
                        files = (await current!.Owner.Instance.GetFiles(new List<Torrent> { current.ToPluginModel() }, selectionChange)).First().Value;

                        FilesChanges.Writer.TryWrite((current, Models.File.FromPluginModel(current.Name, files.MultiFile, files.Files)));
                    } catch {
                        return;
                    }

                    Task? cacheTask = null;
                    if (canUseCached) {
                        cacheTask = Task.WhenAll(
                            fileCache.AddCachedFileEntries(current.Hash, files.Files),
                            propertiesCache.AddCachedTorrentProperties(current.Hash, files.MultiFile)
                        );
                    }

                    try {
                        await delayTask;
                    } catch { }
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetFilesChanges task has died.");
                throw;
            } finally {
                FilesChanges.Writer.Complete();
            }
        }

        private async Task GetPiecesChanges(Models.Torrent current, CancellationToken selectionChange)
        {
            try {
                while (!selectionChange.IsCancellationRequested) {
                    if (!current.Owner.Instance.Capabilities.GetPieces) {
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
                            pieces = (await current!.Owner.Instance.GetPieces(new List<Torrent> { current.ToPluginModel() }, selectionChange)).First().Value;

                            PiecesChanges.Writer.TryWrite((current, pieces));
                        } catch {
                            return;
                        }
                    }

                    try {
                        await delayTask;
                    } catch { }
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetPiecesChanges task has died.");
                throw;
            } finally {
                PiecesChanges.Writer.Complete();
            }
        }
    }
}
