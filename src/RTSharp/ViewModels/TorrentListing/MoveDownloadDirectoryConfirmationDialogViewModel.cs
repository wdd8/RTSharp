using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class MoveDownloadDirectoryConfirmationDialogViewModel : ObservableObject
    {
        public string LeftSide { get; set; }

        public string RightSide { get; set; }

        public string MoveWarning { get; set; }

        public Action<bool> CloseWithResult { get; set; }

        public MoveDownloadDirectoryConfirmationDialogViewModel(string LeftSide, string RightSide, string MoveWarning)
        {
            this.LeftSide = LeftSide;
            this.RightSide = RightSide;
            this.MoveWarning = MoveWarning;
        }

        [RelayCommand]
        public void CancelClick()
        {
            CloseWithResult(false);
        }

        [RelayCommand]
        public void ConfirmClick()
        {
            CloseWithResult(true);
        }
    }

    public static class ExampleMoveDownloadDirectoryConfirmationDialogViewModel
    {
        public static MoveDownloadDirectoryConfirmationDialogViewModel ViewModel { get; } = new MoveDownloadDirectoryConfirmationDialogViewModel("Left", "Right", "Warning!");
    }
}
