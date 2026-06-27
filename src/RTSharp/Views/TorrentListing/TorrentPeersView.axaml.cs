using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentPeersView : VmUserControl<TorrentPeersViewModel>
{
    public TorrentPeersView()
    {
        InitializeComponent();
        grid.PointerPressed += EvGridPointerPressedClearSelection;
    }

    private void EvGridPointerPressedClearSelection(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is not Visual source)
            return;
        if (source is DataGridRow || source.FindAncestorOfType<DataGridRow>() != null)
            return;
        if (source is DataGridColumnHeader || source.FindAncestorOfType<DataGridColumnHeader>() != null)
            return;
        grid.SelectedItems.Clear();
    }
}
