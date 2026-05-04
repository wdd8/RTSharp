using Avalonia.Threading;

using DynamicData;

using Nito.AsyncEx;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GlobalTorrentKey = RTSharp.Models.GlobalTorrentKey;

namespace RTSharp.Core.TorrentPolling
{
    public static class TorrentPolling
    {
        private static Task TorrentChangesTask;
        private static Task TorrentUpdateTask;

        private static Channel<(RTSharpDataProvider DataProvider, ListingChanges<Torrent, Torrent, byte[]> Changes)> ListingChanges = Channel.CreateUnbounded<(RTSharpDataProvider DataProvider, ListingChanges<Torrent, Torrent, byte[]> Changes)>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true
        });

        public static TorrentStore Torrents { get; } = new();
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
            TorrentUpdateTask = Task.Factory.StartNew(TorrentModelUpdates, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            TorrentChangesTask = Task.Factory.StartNew(GetTorrentChanges, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static async Task TorrentModelUpdates()
        {
            try {
                await foreach (var (dp, listingChanges) in ListingChanges.Reader.ReadAllAsync())
                {
                    var changes = listingChanges.Changes;
                    var fullUpdate = listingChanges.FullUpdate.ToInfoHashDictionary(x => x.Hash);
                    var removed = listingChanges.Removed.ToHashSet(HashEqualityComparer.Instance);
                    var toRemove = new List<int>();

                    Torrents.Edit((torrents) => {
                        {
                            // Since it is possible to remove torrent and add it right after in same change, we must handle removals first
                            if (removed.Count > 0) {
                                torrents.RemoveKeys(removed.Select(x => new GlobalTorrentKey(x, dp.PluginInstance.InstanceId)));
                                foreach (var hash in removed) {
                                    TorrentsLookup.Remove((hash, dp.PluginInstance.InstanceId), out var _);
                                }
                            }
                        }

                        {
                            // Full update
                            var vmTorrents = Models.TorrentUpdateExt.NewOrUpdateFromPluginModelMulti(TorrentsLookup, dp, fullUpdate).GetAwaiter().GetResult();

                            foreach (var torrent in vmTorrents) {
                                if (TorrentsLookup.TryAdd((torrent.Hash, torrent.DataOwner.PluginInstance.InstanceId), torrent)) {
                                    torrents.Add(torrent);
                                } else {
                                    torrents.Refresh(torrent);
                                }
                            }
                        }

                        {
                            // Refresh labels
                            bool labelsChanged = false;

                            void add(string label)
                            {
                                ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(AllLabelReferences, label, out var exists);

                                if (exists)
                                    val++;
                                else {
                                    val = 1;
                                    labelsChanged = true;
                                }
                            }

                            void subtract(string label)
                            {
                                ref var val = ref CollectionsMarshal.GetValueRefOrNullRef(AllLabelReferences, label);
                                if (!Unsafe.IsNullRef(ref val)) {
                                    if (val == 1) {
                                        AllLabelReferences.Remove(label);
                                        labelsChanged = true;
                                    } else
                                        val--;
                                }
                            }

                            foreach (var change in listingChanges.FullUpdate) {
                                if (TorrentsLookup.TryGetValue((change.Hash, dp.PluginInstance.InstanceId), out var torrent)) {
                                    foreach (var label in torrent.Labels)
                                        subtract(label);
                                    foreach (var label in change.Labels)
                                        add(label);
                                }
                            }
                            foreach (var change in changes) {
                                if (TorrentsLookup.TryGetValue((change.Hash, dp.PluginInstance.InstanceId), out var torrent)) {
                                    foreach (var label in torrent.Labels)
                                        subtract(label);
                                    foreach (var label in change.Labels)
                                        add(label);
                                }
                            }

                            if (labelsChanged) {
                                string[] keys = [.. AllLabelReferences.Keys];
                                Dispatcher.UIThread.Post(() => {
                                    AllLabelReferencesSubject.OnNext(keys);
                                });
                            }
                        }

                        {
                            // Changed torrents
                            foreach (var change in changes) {
                                if (TorrentsLookup.TryGetValue((change.Hash, dp.PluginInstance.InstanceId), out var torrent)) {
                                    torrent.UpdateFromPluginModel(change).GetAwaiter().GetResult(); // async??
                                    torrents.Refresh(torrent);
                                }
                            }
                        }
                    });
                }
            } catch (Exception ex) {
                Log.Logger.Fatal(ex, "GetTorrentChanges task has died.");
                Debugger.Break();
            }
        }

        private static async Task ReadTorrentChanges(RTSharpDataProvider DataProvider, ChannelReader<ListingChanges<Torrent, Torrent, byte[]>> In)
        {
            try
            {
                await foreach (var listingChanges in In.ReadAllAsync())
                {
                    if (listingChanges.Changes.Any() || listingChanges.FullUpdate.Any() || listingChanges.Removed.Any())
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
            Plugins.DataProviders.Connect().Subscribe(x => {
                dataProvidersChanged.Set();
            });

            try
            {
                while (true)
                {
                    var dataProviders = Plugins.DataProviders.Items.ToList();

                    if (dataProviders.Count == 0)
                    {
                        await dataProvidersChanged.WaitAsync();
                        continue;
                    }

                    // Prevent rapid retrying in case changes task is crashing
                    if (dataProviders.Where(x => x.TorrentChangesTaskStartedAt != DateTime.MinValue).Any(x => DateTime.UtcNow - x.TorrentChangesTaskStartedAt <= TimeSpan.FromSeconds(5))) {
                        await Task.Delay(TimeSpan.FromSeconds(5) - dataProviders.Where(x => x.TorrentChangesTaskStartedAt != DateTime.MinValue).Min(x => DateTime.UtcNow - x.TorrentChangesTaskStartedAt));
                    }

                    foreach (var dp in dataProviders.Where(x => x.CurrentTorrentChangesTask == null))
                    {
                        ChannelReader<ListingChanges<Shared.Abstractions.Torrent, Shared.Abstractions.Torrent, byte[]>> channel;

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

                    var tasks = dataProviders.Select(x => x.CurrentTorrentChangesTask ?? Task.Delay(1000)); // Wait for changes task or 1 second before retrying

                    // Wait for any changes in tasks or data providers
                    var dataProvidersChangedTask = dataProvidersChanged.WaitAsync();

                    var task = await Task.WhenAny(tasks.Concat([dataProvidersChangedTask]).ToArray()!);

                    if (task != dataProvidersChangedTask)
                    {
                        var dps = dataProviders.Where(x => x.CurrentTorrentChangesTask != dataProvidersChangedTask);

                        // Reset on retry
                        foreach (var dp in dps) {
                            dp.CurrentTorrentChangesTask = null;
                            dp.CurrentTorrentChangesTaskCts.Cancel();
                        }
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
