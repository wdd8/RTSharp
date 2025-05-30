using RTSharp.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;
using Serilog;
using Nito.AsyncEx;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using RTSharp.Core.Util;
using RTSharp.Shared.Utils;

namespace RTSharp.Core.TorrentPolling
{
    public static class TorrentPolling
    {
        private static Task TorrentChangesTask;
        private static Task TorrentUpdateTask;

        private static Channel<(DataProvider DataProvider, ListingChanges<Torrent, byte[]> Changes)> ListingChanges = Channel.CreateUnbounded<(DataProvider DataProvider, ListingChanges<Torrent, byte[]> Changes)>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        public static ObservableCollectionEx<Models.Torrent> Torrents { get; } = new();
        public static ConcurrentInfoHashOwnerDictionary<Models.Torrent> TorrentsLookup { get; } = new();

        public static Dictionary<string, int> AllLabelReferences { get; } = new();
        public static IObservable<string[]> AllLabelReferencesObservable { get; private set; }
        private static Subject<string[]> AllLabelReferencesSubject;

        static TorrentPolling()
        {
            AllLabelReferencesSubject = new Subject<string[]>();
            AllLabelReferencesObservable = AllLabelReferencesSubject.AsObservable();
        }

        public static void Start()
        {
            TorrentUpdateTask = Task.Factory.StartNew(TorrentModelUpdates, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.FromCurrentSynchronizationContext());
            TorrentChangesTask = Task.Factory.StartNew(GetTorrentChanges, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static async Task TorrentModelUpdates()
        {
            await foreach (var (dp, listingChanges) in ListingChanges.Reader.ReadAllAsync())
            {
                var changes = listingChanges.Changes.ToList();
                var removed = listingChanges.Removed.ToHashSet(new HashEqualityComparer());

                var toRemove = new List<Models.Torrent>();

                // Since it is possible to remove torrent and add it right after in same change, we must handle removals first
                foreach (var torrent in Torrents)
                {
                    if (removed.Contains(torrent.Hash) && torrent.Owner == dp)
                    {
                        toRemove.Add(torrent);
                    }
                }

                foreach (var remove in toRemove)
                {
                    Torrents.Remove(remove);
                    TorrentsLookup.Remove((remove.Hash, remove.Owner.PluginInstance.InstanceId), out var _);
                }

                bool labelsChanged = false;

                void add(string label)
                {
                    if (AllLabelReferences.ContainsKey(label))
                        AllLabelReferences[label]++;
                    else {
                        AllLabelReferences[label] = 1;
                        labelsChanged = true;
                    }
                }

                void subtract(string label)
                {
                    if (AllLabelReferences.TryGetValue(label, out var val)) {
                        if (val == 1) {
                            AllLabelReferences.Remove(label);
                            labelsChanged = true;
                        } else
                            AllLabelReferences[label]--;
                    } else
                        Log.Logger.Warning($"Tried to subtract ref count from label {label}, but it didn't exist");
                }

                foreach (var torrent in Torrents)
                {
                    Torrent? changedTorrent = null;

                    if (torrent.Owner != dp)
                        continue;

                    foreach (var i in changes)
                    {
                        if (i.Hash.SequenceEqual(torrent.Hash))
                        {
                            foreach (var label in torrent.Labels)
                                subtract(label);
                            await torrent.UpdateFromPluginModel(i);
                            foreach (var label in torrent.Labels)
                                add(label);
                            changedTorrent = i;
                            break;
                        }
                    }
                    if (changedTorrent != null)
                    {
                        changes.Remove(changedTorrent);
                    }
                }
                Torrents.NotifyInnerItemsChanged();

                // For new torrents
                var newVMTorrents = await Task.WhenAll(changes.Select(async x => {
                    var newVMTorrent = new Models.Torrent(x.Hash, dp);
                    await newVMTorrent.UpdateFromPluginModel(x);
                    return newVMTorrent;
                }));
                if (newVMTorrents.Count() > 0) {
                    Torrents.AddRange(newVMTorrents);
                    foreach (var torrent in newVMTorrents)
                        TorrentsLookup.TryAdd((torrent.Hash, torrent.Owner.PluginInstance.InstanceId), torrent);

                    foreach (var x in newVMTorrents)
                    {
                        foreach (var label in x.Labels)
                            add(label);
                    }
                }

                if (labelsChanged)
                    AllLabelReferencesSubject.OnNext(AllLabelReferences.Keys.ToArray());
            }
        }

        private static async Task ReadTorrentChanges(DataProvider DataProvider, ChannelReader<ListingChanges<Torrent, byte[]>> In)
        {
            try
            {
                await foreach (var listingChanges in In.ReadAllAsync())
                {
                    if (listingChanges.Changes.Any() || listingChanges.Removed.Any())
                        await ListingChanges.Writer.WriteAsync((DataProvider, listingChanges));
                }
            }
            catch
            {
                // Log? Don't really care, it will gonna restart itself anyway
            }
        }

        private static async Task GetTorrentChanges()
        {
            AsyncAutoResetEvent dataProvidersChanged = new();
            Plugins.DataProviders.CollectionChanged += (sender, e) => {
                dataProvidersChanged.Set();
            };

            try
            {
                while (true)
                {
                    var dataProviders = Plugins.DataProviders.ToList();

                    if (!dataProviders.Any())
                    {
                        await dataProvidersChanged.WaitAsync();
                        continue;
                    }

                    if (dataProviders.All(x => DateTime.UtcNow - x.TorrentChangesTaskStartedAt <= TimeSpan.FromSeconds(5)))
                        await Task.Delay(dataProviders.Min(x => DateTime.UtcNow - x.TorrentChangesTaskStartedAt));

                    foreach (var dp in dataProviders.Where(x => x.CurrentTorrentChangesTask == null))
                    {
                        ChannelReader<ListingChanges<Torrent, byte[]>> channel;

                        dp.CurrentTorrentChangesTaskCts?.Cancel();
                        dp.CurrentTorrentChangesTaskCts = new();

                        try
                        {
                            channel = await dp.Instance.GetTorrentChanges(dp.CurrentTorrentChangesTaskCts.Token);
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error(ex, $"{dp.PluginInstance.PluginInstanceConfig.Name} GetTorrentChanges threw an error");
                            dp.TorrentChangesTaskStartedAt = DateTime.MinValue;
                            continue;
                        }

                        dp.CurrentTorrentChangesTask = ReadTorrentChanges(dp, channel);
                        dp.TorrentChangesTaskStartedAt = DateTime.UtcNow;
                    }

                    var tasks = dataProviders.Select(x => x.CurrentTorrentChangesTask).Where(x => x != null);

                    // Wait for any changes in tasks or data providers
                    var dataProvidersChangedTask = dataProvidersChanged.WaitAsync();

                    var task = await Task.WhenAny(tasks.Concat(new[] { dataProvidersChangedTask }).ToArray()!);

                    if (task != dataProvidersChangedTask)
                    {
                        var dp = dataProviders.Single(x => x.CurrentTorrentChangesTask == task);

                        // Changes task was running but died, full reset
                        dp.CurrentTorrentChangesTask = null;
                        dp.CurrentTorrentChangesTaskCts.Cancel();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Fatal(ex, "GetTorrentChanges task has died.");
                throw;
            }
        }
    }
}
