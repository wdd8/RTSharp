using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Core;
using RTSharp.Shared.Abstractions;

using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

using Peer = RTSharp.Models.Peer;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentPeersViewModel : ObservableObject
    {
        public ObservableCollection<Peer> Peers { get; } = new();

        public Models.Torrent CurrentlySelectedTorrent { get; set; }

        static readonly FrozenDictionary<string, Func<DataProviderPeerCapabilities, bool>> StrToCap = new Dictionary<string, Func<DataProviderPeerCapabilities, bool>>() {
            { "Add peers", x => x.AddPeer },
            { "Ban peer", x => x.BanPeer},
            { "Kick peer", x => x.KickPeer },
            { "Snub peer", x => x.SnubPeer },
            { "Unsnub peer", x => x.UnsnubPeer }
        }.ToFrozenDictionary();

        bool CanExecuteAction(string Action)
        {
            Debug.Assert(StrToCap.ContainsKey(Action));

            return StrToCap[Action](CurrentlySelectedTorrent.DataOwner.Instance.Peer.Capabilities);
        }

        public bool CanExecuteAddPeers() => CanExecuteAction("Add peers");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteAddPeers))]
        public async Task AddPeers(IList Input)
        {

        }

        public bool CanExecuteBanPeer() => CanExecuteAction("Ban peer");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteBanPeer))]
        public async Task BanPeer(IList Input)
        {

        }

        public bool CanExecuteKickPeer() => CanExecuteAction("Kick peer");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteKickPeer))]
        public async Task KickPeer(IList Input)
        {

        }

        public bool CanExecuteSnubPeer() => CanExecuteAction("Snub peer");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteSnubPeer))]
        public async Task SnubPeer(IList Input)
        {

        }

        public bool CanExecuteUnsnubPeer() => CanExecuteAction("Unsnub peer");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteUnsnubPeer))]
        public async Task UnsnubPeer(IList Input)
        {

        }

        public TorrentPeersViewModel()
        {
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-user-group");
    }
}
