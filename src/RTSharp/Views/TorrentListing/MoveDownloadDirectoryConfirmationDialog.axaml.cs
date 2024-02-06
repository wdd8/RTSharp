using RTSharp.Shared.Controls;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing
{
    public partial class MoveDownloadDirectoryConfirmationDialog : VmWindow<MoveDownloadDirectoryConfirmationDialogViewModel>
    {
        public MoveDownloadDirectoryConfirmationDialog()
        {
            InitializeComponent();

            BindViewModelActions(vm => {
                vm!.CloseWithResult = CloseWithResult;
            });
        }

        public void CloseWithResult(bool Input)
        {
            this.Close(Input);
        }
    }
}
