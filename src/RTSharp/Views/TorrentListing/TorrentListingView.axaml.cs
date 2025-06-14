using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

using RTSharp.ViewModels;
using RTSharp.ViewModels.TorrentListing;

using System.Threading.Tasks;
using DialogHostAvalonia;
using RTSharp.Shared.Utils;
using Torrent = RTSharp.Models.Torrent;
using RTSharp.Shared.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Models.TreeDataGrid;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentListingView : VmUserControl<TorrentListingViewModel>
{
    private int RowHeight { get; set; }
    private Thickness GridBorderThickness { get; } = Thickness.Parse("0 0 0 1");

    public TorrentListingView()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.RecheckTorrentsConfirmationDialog = ShowRecheckTorrentsConfirmationDialog;
            vm!.MoveDownloadDirectoryConfirmationDialog = ShowMoveDownloadDirectoryConfirmationDialog;
            vm!.SelectDirectoryDialog = ShowSelectDirectoryDialog;
            vm!.DeleteTorrentsConfirmationDialog = ShowDeleteTorrentsConfirmationDialog;
            vm!.SelectRemoteDirectoryDialog = ShowSelectRemoteDirectoryDialog;
            vm!.ShowAddLabelDialog = ShowAddLabelDialog;
            vm!.CloseAddLabelDialog = CloseAddLabelDialog;
            vm!.OnViewModelAttached(this);
        }, null);

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        RowHeight = config.Look.Value.TorrentListing?.RowHeight ?? 32;
    }

    public override void EndInit() => base.EndInit();

    public TorrentListingViewModel? TypedContext => (TorrentListingViewModel?)DataContext;

    public void ShowAddLabelDialog()
    {
        AddLabelDialogHost.OpenDialogCommand.Execute(null);
    }

    public void CloseAddLabelDialog()
    {
        DialogHost.GetDialogSession(AddLabelDialogHost.Identifier)!.Close(false);
    }

    public void EvRowPrepared(object sender, TreeDataGridRowEventArgs e)
    {
        var torrent = (Torrent)e.Row.DataContext!;
        if (Color.TryParse(torrent!.Owner.PluginInstance.PluginInstanceConfig.Color, out var color)) {
            e.Row.Background = new SolidColorBrush(color);
            if (color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 186) {
                e.Row.Foreground = Brushes.Black;
            } else {
                e.Row.Foreground = Brushes.White;
            }
        }
        e.Row.BorderThickness = GridBorderThickness;
        e.Row.BorderBrush = Brushes.DarkGray;
        e.Row.Height = RowHeight;

        var hooks = Plugin.Plugins.GetHook<object, TreeDataGridRowEventArgs>(Plugin.Plugins.HookType.TorrentListing_EvRowPrepared);
        foreach (var hook in hooks) {
            hook(sender, e);
        }
    }

    public void EvCellPrepared(object sender, TreeDataGridCellEventArgs e)
    {
        Torrent torrent;
        if (e.Cell.DataContext is TemplateCell template) {
            torrent = (Torrent)template.Value;
        } else {
            torrent = (Torrent)e.Cell.DataContext!;
        }
        
        var cell = (TreeDataGridCell)e.Cell;

        // Guard against cell recycling. If plugins modify properties of some cells, others might get recycled from before and jumbled
        cell.Background = null;
        if (Color.TryParse(torrent!.Owner.PluginInstance.PluginInstanceConfig.Color, out var color)) {
            if (color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 186) {
                cell.Foreground = Brushes.Black;
            } else {
                cell.Foreground = Brushes.White;
            }
        }
        cell.BorderThickness = GridBorderThickness;
        cell.BorderBrush = Brushes.DarkGray;

        var hooks = Plugin.Plugins.GetHook<object, TreeDataGridCellEventArgs>(Plugin.Plugins.HookType.TorrentListing_EvCellPrepared);
        foreach (var hook in hooks) {
            hook(sender, e);
        }
    }

    private async Task<bool> ShowRecheckTorrentsConfirmationDialog((Window BaseWindow, ulong Size, int Count) Input)
    {
        var window = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams() {
            ButtonDefinitions = ButtonEnum.YesNo,
            ContentTitle = "RT#",
            ContentMessage = $"Are you sure you want to recheck {Input.Count} torrent{(Input.Count == 1 ? "" : "s")} ({Converters.GetSIDataSize(Input.Size)})?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon.Question
        });

        return (await window.ShowWindowDialogAsync(Input.BaseWindow)) == ButtonResult.Yes;
    }

    private async Task<bool> ShowMoveDownloadDirectoryConfirmationDialog((Window Owner, string[] CurrentFiles, string[] FutureFiles, string MoveWarning) Input)
    {
        var window = new MoveDownloadDirectoryConfirmationDialog() {
            ViewModel = new MoveDownloadDirectoryConfirmationDialogViewModel(
                String.Join(Environment.NewLine, Input.CurrentFiles),
                String.Join(Environment.NewLine, Input.FutureFiles),
                Input.MoveWarning
            )
        };

        var result = await window.ShowDialog<bool>(Input.Owner);

        return result;
    }

    private async Task<string?> ShowSelectDirectoryDialog((Window Owner, string Title) Input)
    {
        var dir = await Input.Owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions() {
            Title = "RT# - " + Input.Title,
            AllowMultiple = false
        });

        return dir?.FirstOrDefault()?.Path?.LocalPath;
    }

    private async Task<bool> ShowDeleteTorrentsConfirmationDialog((Window BaseWindow, ulong Size, int Count, bool AndData) Input)
    {
        var window = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams() {
            ButtonDefinitions = ButtonEnum.YesNo,
            ContentTitle = "RT#",
            ContentMessage = $"Are you sure you want to delete {Input.Count} torrent{(Input.Count == 1 ? "" : "s")}{(Input.AndData ? $" and their data ({Converters.GetSIDataSize(Input.Size)})" : "")}?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon.Question
        });

        return (await window.ShowWindowDialogAsync(Input.BaseWindow)) == ButtonResult.Yes;
    }

    private async Task<string?> ShowSelectRemoteDirectoryDialog((Window Owner, string Title, Plugin.DataProvider DataProvider, string? StartingDir) Input)
    {
        var dialog = new DirectorySelectorWindow() {
            ViewModel = new DirectorySelectorWindowViewModel(Input.DataProvider) {
                WindowTitle = Input.Title
            }
        };

        await dialog.ViewModel.SetCurrentFolder(Input.StartingDir ?? "/");
        var result = await dialog.ShowDialog<string?>(Input.Owner);
        return result;
    }
}
