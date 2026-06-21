using Avalonia.Media;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DialogHostAvalonia;

using RTSharp.Core;
using RTSharp.Shared.Abstractions;

using Serilog;

using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

using Peer = RTSharp.Models.Peer;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentPeersViewModel : ObservableObject
    {
        public ObservableCollection<Peer> Peers { get; } = new();
        public DataGridCollectionView PeersView { get; }

        [ObservableProperty]
        public partial Models.Torrent? Torrent { get; set; }

        [ObservableProperty]
        public partial string AddPeerEndpointText { get; set; } = "";

        static readonly FrozenDictionary<string, Func<DataProviderPeerCapabilities, bool>> StrToCap = new Dictionary<string, Func<DataProviderPeerCapabilities, bool>>() {
            { "Add peers", x => x.AddPeer },
            { "Ban peer", x => x.BanPeer}
        }.ToFrozenDictionary();

        bool CanExecuteAction(string Action)
        {
            Debug.Assert(StrToCap.ContainsKey(Action));

            if (Torrent == null)
                return false;

            return StrToCap[Action](Torrent.DataOwner.Instance.Peer.Capabilities);
        }

        public bool CanExecuteAddPeers() => CanExecuteAction("Add peers");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteAddPeers))]
        public async Task AddPeers()
        {
            if (!IPEndPoint.TryParse(AddPeerEndpointText, out var endpoint) || Torrent == null) {
                return;
            }

            try {
                await Torrent.DataOwner.Instance.Peer.AddPeer(Torrent.ToPluginModel(), endpoint);
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Adding peer failed");
            } finally {
                DialogHost.GetDialogSession("AddPeerHost")!.Close(false);
                AddPeerEndpointText = "";
            }
        }

        public bool CanExecuteBanPeer() => CanExecuteAction("Ban peer");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteBanPeer))]
        public async Task BanPeer(IList Input)
        {
            if (Torrent == null)
                return;

            var selected = new List<Peer>();
            foreach (var item in Input) {
                if (item is Peer peer)
                    selected.Add(peer);
            }

            var torrent = Torrent.ToPluginModel();

            foreach (var peer in selected) {
                try {
                    await Torrent.DataOwner.Instance.Peer.BanPeer(torrent, peer.IPPort);
                } catch (Exception ex) {
                    Log.Logger.Error(ex, "Banning peer {Peer} failed", peer.IPPortDisplay);
                }
            }
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Peer))]
        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Accesses Peer")]
        public TorrentPeersViewModel()
        {
            PeersView = new DataGridCollectionView(Peers);
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-user-group");
    }
}
