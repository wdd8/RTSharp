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

            BindViewModelActions(vm => {
                vm!.ResortList = ResortList;
            });
        }

        private void ResortList()
        {
            if (!grid.IsLoaded)
                return;

            foreach (var col in grid.Columns) {
                var sortDirection = col.GetSortDirection();
                if (sortDirection == null)
                    continue;

                col.Sort(sortDirection.Value);
            }
        }
    }
}
