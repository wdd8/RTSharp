using System.Collections.ObjectModel;
using Avalonia.Media;
using RTSharp.Models;
using RTSharp.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using System.Collections;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentPeersViewModel : ObservableObject
    {
        public ObservableCollection<Peer> Peers { get; } = new();

        [RelayCommand]
        public async Task AddPeers(IList Input)
        {

        }

        [RelayCommand]
        public async Task BanPeer(IList Input)
        {

        }

        [RelayCommand]
        public async Task KickPeer(IList Input)
        {

        }

        [RelayCommand]
        public async Task SnubPeer(IList Input)
        {

        }
        [RelayCommand]
        public async Task UnsnubPeer(IList Input)
        {

        }

        public TorrentPeersViewModel()
        {
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-user-group");
    }
}
