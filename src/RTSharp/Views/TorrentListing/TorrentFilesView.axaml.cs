using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.Services;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Controls.Views;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentFilesView : VmUserControl<TorrentFilesViewModel>
{
    public TorrentFilesView()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.ShowTextPreviewWindow = ShowTextPreviewWindow;
        });

        MainGrid.PointerPressed += EvGridPointerPressedClearSelection;
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
        MainGrid.SelectedItems.Clear();
    }

    public void ShowTextPreviewWindow(string Input)
    {
        var wnd = new TextPreviewWindow() {
            ViewModel = new Shared.Controls.ViewModels.TextPreviewWindowViewModel() { 
                Text = Input,
                Monospace = true
            }
        };

        wnd.Show();
    }
}
