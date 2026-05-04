using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentListing;

using System.Threading.Tasks;

namespace RTSharp.Views.TorrentListing;

public partial class GeneralTorrentInfoView : VmUserControl<GeneralTorrentInfoViewModel>
{
    public GeneralTorrentInfoView()
    {
        InitializeComponent();

        this.BindViewModelActions(vm => {
            vm!.Copy = Copy;
        });
    }

    public async Task Copy(string In)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        await topLevel!.Clipboard!.SetTextAsync(In);
    }

    private async void CopyHash_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel!.CopyInfoHash();
    }
}
