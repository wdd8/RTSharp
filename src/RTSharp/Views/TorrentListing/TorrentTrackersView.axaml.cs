using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentTrackersView : VmUserControl<TorrentTrackersViewModel>
{
    public TorrentTrackersView()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.BrowseForIconDialog = ShowBrowserForIconDialog;
        });

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

    private async Task<string?> ShowBrowserForIconDialog(Window Input)
    {
        var dialog = await Input.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions() {
            Title = "RT# - Browse for icon...",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> {
                new("Image files") {
                    Patterns = new List<string> {
                        "*.bmp",
                        "*.gif",
                        "*.ico",
                        "*.jpg",
                        "*.jpeg",
                        "*.png",
                        "*.tiff",
                        "*.wdp"
                    }
                }
            }
        });

        return dialog.FirstOrDefault()?.Path?.LocalPath;
    }
}
