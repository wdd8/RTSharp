using Avalonia.Controls;

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
}
