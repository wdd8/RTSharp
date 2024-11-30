using RTSharp.Core.Util;
using RTSharp.Shared.Controls;
using RTSharp.ViewModels.TorrentListing;

using System.Linq;

namespace RTSharp.Views.TorrentListing
{
    public partial class TorrentPeersView : VmUserControl<TorrentPeersViewModel>
    {
        public TorrentPeersView()
        {
            InitializeComponent();
        }
    }
}
