using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentPeersView : VmUserControl<TorrentPeersViewModel>
{
    public TorrentPeersView()
    {
        InitializeComponent();
    }
}
