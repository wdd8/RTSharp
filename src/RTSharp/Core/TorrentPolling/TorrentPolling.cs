using Avalonia.Threading;

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

namespace RTSharp.Core.TorrentPolling;

public static class TorrentPolling
{
    private static Channel<(RTSharpDataProvider DataProvider, ListingChanges<Torrent, Torrent, byte[]> Changes)> ListingChanges = Channel.CreateUnbounded<(RTSharpDataProvider DataProvider, ListingChanges<Torrent, Torrent, byte[]> Changes)>(new UnboundedChannelOptions()
    {
        SingleReader = true,
        SingleWriter = true
    });

    public static TorrentStore Torrents { get; } = new();

    public static Dictionary<string, int> AllLabelReferences { get; } = new();
    public static IObservable<string[]> AllLabelReferencesObservable { get; private set; }
    private static BehaviorSubject<string[]> AllLabelReferencesSubject;

    static TorrentPolling()
    {
        AllLabelReferencesSubject = new BehaviorSubject<string[]>([]);
        AllLabelReferencesObservable = AllLabelReferencesSubject.AsObservable();

    }

    public static void Start()
    {
        _ = Task.Factory.StartNew(TorrentModelUpdates, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = Task.Factory.StartNew(GetTorrentChanges, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static async Task TorrentModelUpdates()
    {
        try {
            await foreach (var (dp, listingChanges) in ListingChanges.Reader.ReadAllAsync())
            {
                var changes = listingChanges.Changes;
                var fullUpdate = listingChanges.FullUpdate.ToInfoHashDictionary(x => x.Hash);
                var dpId = dp.Host.InstanceId;

                List<GlobalTorrentKey>? removedKeys = null;
                if (listingChanges.Removed.Count > 0) {
                    removedKeys = new List<GlobalTorrentKey>(listingChanges.Removed.Count);
                    foreach (var hash in listingChanges.Removed)
                        removedKeys.Add(new GlobalTorrentKey(hash, dpId));
                }

                var vmTorrents = await Models.TorrentUpdateExt.NewOrUpdateFromPluginModelMulti(Torrents, dp, fullUpdate);

                var changedTorrents = new List<Models.Torrent>(changes.Count);
                foreach (var change in changes) {
                    if (Torrents.TryGet(new GlobalTorrentKey(change.Hash, dpId), out var torrent)) {
                        await torrent.PendingEdit(change);
                        changedTorrents.Add(torrent);
                    }
                }

                Torrents.Edit(dp, (torrents) => {
                    {
                        // Since it is possible to remove torrent and add it right after in same change, we must handle removals first
                        if (removedKeys != null) {
                            torrents.RemoveKeys(removedKeys);
                        }
                    }

                    {
                        // Full update
                        foreach (var torrent in vmTorrents) {
                            if (torrents.TryGet(new GlobalTorrentKey(torrent.Hash, dpId), out _)) {
                                torrents.Refresh(torrent);
                            } else {
                                torrents.Add(torrent);
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
                            if (torrents.TryGet(new GlobalTorrentKey(change.Hash, dpId), out var torrent)) {
                                foreach (var label in torrent.Labels)
                                    subtract(label);
                                foreach (var label in change.Labels)
                                    add(label);
                            }
                        }
                        foreach (var change in changes) {
                            if (torrents.TryGet(new GlobalTorrentKey(change.Hash, dpId), out var torrent)) {
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
                        foreach (var torrent in changedTorrents)
                            torrents.Refresh(torrent);
                    }
                });
            }
        } catch (Exception ex) {
            Log.Logger.Fatal(ex, "GetTorrentChanges task has died.");
            Debugger.Break();
        }
    }

    private static async Task ReadTorrentChanges(RTSharpDataProvider DataProvider)
    {
        try {
            var channel = await DataProvider.Instance.GetTorrentChanges(DataProvider.CurrentTorrentChangesTaskCts.Token);

            await foreach (var listingChanges in channel.ReadAllAsync())
            {
                if (listingChanges.Changes.Count > 0 || listingChanges.FullUpdate.Count > 0 || listingChanges.Removed.Count > 0)
                    await ListingChanges.Writer.WriteAsync((DataProvider, listingChanges));
            }
        } catch (Exception ex) {
            Log.Logger.Error(ex, $"{DataProvider.Config.Plugin.Name} GetTorrentChanges threw an error");

            await Task.Delay(TimeSpan.FromSeconds(5));

            throw;
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

                foreach (var dp in dataProviders.Where(x => x.CurrentTorrentChangesTask == null))
                {
                    dp.CurrentTorrentChangesTask = ReadTorrentChanges(dp);
                }

                var tasks = dataProviders.Select(x => x.CurrentTorrentChangesTask!)!;
                var dataProvidersChangedTask = dataProvidersChanged.WaitAsync();

                var task = await Task.WhenAny([..tasks, dataProvidersChangedTask]);

                if (task != dataProvidersChangedTask)
                {
                    var dps = dataProviders.Where(x => x.CurrentTorrentChangesTask?.IsCompleted == true);

                    foreach (var dp in dps)
                        dp.CurrentTorrentChangesTask = null;
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
