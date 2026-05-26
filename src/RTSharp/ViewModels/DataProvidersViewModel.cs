using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace RTSharp.ViewModels
{
    public class DataProvidersViewModel : ObservableObject
    {
        public ObservableCollection<Models.DataProvider> Items { get; } = new();

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-network-wired");

        public string HeaderName => "Data providers";

        public DataProvidersViewModel()
        {
            Plugin.Plugins.DataProviders
                .Connect()
                .AutoRefreshOnObservable(x => x.State)
                .AutoRefreshOnObservable(x => x.PluginInstance.AttachedDaemonService.Latency)
                .Subscribe(x => {
                    foreach (var change in x) {
                        var item = Items.FirstOrDefault(i => i.InstanceId == change.Item.Current.PluginInstance.InstanceId);
                        if (item == default) {
                            Items.Add(new Models.DataProvider {
                                DisplayName = change.Item.Current.ToString(),
                                InstanceId = change.Item.Current.PluginInstance.InstanceId,
                                State = change.Item.Current.State.Value,
                                Latency = change.Item.Current.PluginInstance.AttachedDaemonService.Latency.Value,
                                TotalDLSpeed = 0,
                                TotalUPSpeed = 0,
                                ActiveTorrentCount = 0
                            });
                        } else {
                            item.State = change.Item.Current.State.Value;
                            item.Latency = change.Item.Current.PluginInstance.AttachedDaemonService.Latency.Value;
                        }
                    }
                });

            TorrentPolling.Torrents.Changed += TorrentPolling_TorrentBatchChange;
        }

        private void TorrentPolling_TorrentBatchChange(object? sender, TorrentStoreChangeSet e)
        {
            var torrents = TorrentPolling.Torrents.GetSnapshot();
            foreach (var item in Items.ToArray()) {
                var totalDLSpeed = 0UL;
                var totalUPSpeed = 0UL;
                var activeTorrentCount = 0U;

                foreach (var torrent in torrents) {
                    if (torrent.DataOwner.PluginInstance.InstanceId != item.InstanceId) {
                        continue;
                    }

                    totalDLSpeed += torrent.DLSpeed;
                    totalUPSpeed += torrent.UPSpeed;
                    activeTorrentCount += (torrent.InternalState & Shared.Abstractions.TORRENT_STATE.ACTIVE) == Shared.Abstractions.TORRENT_STATE.ACTIVE ? 1U : 0U;
                }

                item.TotalDLSpeed = totalDLSpeed;
                item.TotalUPSpeed = totalUPSpeed;
                item.ActiveTorrentCount = activeTorrentCount;
            }
        }
    }
}
