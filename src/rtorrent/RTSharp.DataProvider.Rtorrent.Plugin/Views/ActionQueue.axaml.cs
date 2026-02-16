using RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Views;

public partial class ActionQueue : VmUserControl<ActionQueueViewModel>
{
    public ActionQueue()
    {
        InitializeComponent();
    }
}
