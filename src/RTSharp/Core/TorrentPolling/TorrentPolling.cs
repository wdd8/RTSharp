﻿using DynamicData;

using Nito.AsyncEx;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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

        public static SourceList<Models.Torrent> Torrents { get; } = new();
        public static ConcurrentInfoHashOwnerDictionary<Models.Torrent> TorrentsLookup { get; } = new();
        public static event Action TorrentBatchChange;

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
                var changes = listingChanges.Changes.ToInfoHashDictionary(x => x.Hash);
                var removed = listingChanges.Removed.ToHashSet(new HashEqualityComparer());

                var toRemove = new List<Models.Torrent>();

                // Since it is possible to remove torrent and add it right after in same change, we must handle removals first
                foreach (var torrent in Torrents.Items)
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
                    } else
                        Log.Logger.Warning($"Tried to subtract ref count from label {label}, but it didn't exist");
                }

                await Models.TorrentUpdateExt.UpdateFromPluginModelMulti(Torrents.Items.Where(x => x.Owner == dp), changes);

                foreach (var torrent in Torrents.Items)
                {
                    if (torrent.Owner != dp)
                        continue;

                    if (changes.TryGetValue(torrent.Hash, out var i)) {
                        foreach (var label in torrent.Labels)
                            subtract(label);
                        foreach (var label in i.Labels)
                            add(label);

                        changes.Remove(i.Hash);
                    }
                }

                // For new torrents
                var newVMTorrents = changes.Select(x => new Models.Torrent(x.Value.Hash, dp)).ToList();
                await Models.TorrentUpdateExt.UpdateFromPluginModelMulti(newVMTorrents, changes);
                if (newVMTorrents.Count() > 0) {
                    Torrents.AddRange(newVMTorrents);
                    foreach (var torrent in newVMTorrents) {
                        TorrentsLookup.TryAdd((torrent.Hash, torrent.Owner.PluginInstance.InstanceId), torrent);
                        foreach (var label in torrent.Labels)
                            add(label);
                    }
                }

                if (labelsChanged)
                    AllLabelReferencesSubject.OnNext(AllLabelReferences.Keys.ToArray());

                TorrentBatchChange?.Invoke();
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
