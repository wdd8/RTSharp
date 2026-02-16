using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using NP.UniDockService;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace RTSharp.ViewModels
{
    public class DockDataProvidersViewModel : DockItemViewModel<DataProvidersViewModel> { }

    public class DataProvidersViewModel : ObservableObject, IDockable
    {
        /*public ReadOnlyObservableCollection<Models.DataProvider> _items;

        public ReadOnlyObservableCollection<Models.DataProvider> Items => _items;*/
        public ObservableCollection<Models.DataProvider> Items { get; } = new();

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-network-wired");

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

            TorrentPolling.TorrentBatchChange += TorrentPolling_TorrentBatchChange;
            //RefreshingToken.Token.Register(() => TorrentPolling.TorrentBatchChange -= TorrentPolling_TorrentBatchChange);
        }

        private void TorrentPolling_TorrentBatchChange(List<NotifyCollectionChangedEventArgs> In)
        {
            foreach (var item in Items.ToArray()) {
                var totalDLSpeed = 0UL;
                var totalUPSpeed = 0UL;
                var activeTorrentCount = 0U;

                foreach (var torrent in TorrentPolling.Torrents.Items.Where(x => x.DataOwner.PluginInstance.InstanceId == item.InstanceId)) {
                    totalDLSpeed += torrent.DLSpeed;
                    totalUPSpeed += torrent.UPSpeed;
                    activeTorrentCount += (torrent.InternalState & Shared.Abstractions.TORRENT_STATE.ACTIVE) == Shared.Abstractions.TORRENT_STATE.ACTIVE ? 1U : 0U;
                }

                item.TotalDLSpeed = totalDLSpeed;
                item.TotalUPSpeed = totalUPSpeed;
                item.ActiveTorrentCount = activeTorrentCount;
            }
        }

        private CancellationTokenSource RefreshingToken { get; set; }

        public void StartRefreshing()
        {
        }

        public void StopRefreshing()
        {
            RefreshingToken.Cancel();
        }
    }
}
