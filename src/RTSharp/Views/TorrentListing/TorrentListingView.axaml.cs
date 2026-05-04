using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using CommunityToolkit.Mvvm.Input;

using DialogHostAvalonia;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;
using RTSharp.ViewModels.TorrentListing;
using RTSharp.Views.Util;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;
using System.Collections.Generic;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentListingView : VmUserControl<TorrentListingViewModel>
{
    private int RowHeight { get; set; }

    private static readonly Dictionary<string, (SolidColorBrush? Background, IBrush? Foreground)> RowBrushCache = new();

    public static ObservableCollection<Func<System.Collections.IList, MenuItem>> MenuItemInserts = new();
    public static ObservableCollection<Action<System.Collections.IList>> MenuItemRemoves = new();

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
            vm!.CaptureGridState = CaptureGridState;
            vm!.ScrollToItem = grid_ScrollToItem;

            grid.SortingAdapterFactory = vm!.SortingAdapterFactory;
            grid.FilteringAdapterFactory = vm!.FilteringAdapterFactory;
        }, null);

        MenuItemInserts.CollectionChanged += MenuItemInserts_CollectionChanged;
        HandleInserts(MenuItemInserts);

        MenuItemRemoves.CollectionChanged += MenuItemRemoves_CollectionChanged;
        HandleRemoves(MenuItemRemoves);

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        grid.TemplateApplied += (_, __) => {
            var scrollViewer = grid.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null) {
                scrollViewer.VerticalSnapPointsType = SnapPointsType.MandatorySingle;
                scrollViewer.VerticalSnapPointsAlignment = SnapPointsAlignment.Near;
            }
        };
        grid.CopyingRowClipboardContent += (_, e) => ViewModel!.EvCopyingRowClipboardContent(e);

        RowHeight = config.Look.Value.TorrentListing?.RowHeight ?? 20;
        grid.RowHeight = RowHeight;
    }

    private void grid_ScrollToItem(object item)
    {
        grid.SelectedItem = item;
        grid.ScrollIntoView(item, null);
    }

    private string? CaptureGridState()
    {
        string? serialized = null;
        var @lock = new ManualResetEvent(false);
        var stateOpts = new DataGridStateOptions { };

        Dispatcher.UIThread.Invoke(() => {
            try {
                serialized = DataGridStatePersistence.SerializeStateToString(
                    grid,
                    DataGridStateSections.Columns | DataGridStateSections.Sorting | DataGridStateSections.Filtering /*| DataGridStateSections.Grouping TODO: */,
                    stateOpts,
                    new SystemTextJsonDataGridStateSerializer(),
                    new DataGridStatePersistenceOptions {
                        TokenProvider = new TorrentListingViewModel.DataGridStatePersistenceTokenProvider()
                    }
                );
            } catch {
                // probe point
            }
            @lock.Set();
        });

        @lock.WaitOne();
        return serialized;
    }

    public void RestoreGridState(string In)
    {
        try {
            DataGridStatePersistence.RestoreStateFromString(
                grid,
                In,
                DataGridStateSections.Columns | DataGridStateSections.Sorting | DataGridStateSections.Filtering /*| DataGridStateSections.Grouping TODO: */,
                new DataGridStateOptions { },
                new SystemTextJsonDataGridStateSerializer(),
                new DataGridStatePersistenceOptions {
                    TokenResolver = new TorrentListingViewModel.DataGridStatePersistenceTokenResolver()
                }
            );

            ViewModel!.PostGridStateRestore();
        } catch {
            // probe point
        }
    }

    public void StateFilterFlyoutOpening(object? sender, EventArgs e) => ViewModel!.StateFilterFlyoutOpening();

    public void LabelsFilterFlyoutOpening(object? sender, EventArgs e) => ViewModel!.LabelsFilterFlyoutOpening();

    public void ConnectionFilterFlyoutOpening(object? sender, EventArgs e) => ViewModel!.ConnectionFilterFlyoutOpening();

    public void TrackerFilterFlyoutOpening(object? sender, EventArgs e) => ViewModel!.TrackerFilterFlyoutOpening();

    private void MenuItemInserts_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) {
            HandleInserts(e.NewItems);
        }
    }

    private void MenuItemRemoves_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) {
            HandleRemoves(e.NewItems);
        }
    }

    private void HandleInserts(System.Collections.IList List)
    {
        if (this.Resources.TryGetResource("GridContextMenuItems", null, out var obj) && obj is System.Collections.IList list) {
            foreach (var fx in List.OfType<Func<System.Collections.IList, MenuItem>>()) {
                var menuItem = fx(list);

                var oldCommand = menuItem.Command;
                menuItem.Command = new RelayCommand(() => {
                    oldCommand?.Execute(SelectedItemsToPluginModelConverter.Instance.Convert(grid.SelectedItems, null!, null, null!));
                });
            }
        }
    }

    private void HandleRemoves(System.Collections.IList List)
    {
        if (this.Resources.TryGetResource("GridContextMenuItems", null, out var obj) && obj is System.Collections.IList list) {
            foreach (var fx in List.OfType<Action<System.Collections.IList>>()) {
                MenuItemRemoves.Remove(fx);
                fx(list);
            }
        }
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

    public void EvLoadingRow(object sender, DataGridRowEventArgs e)
    {
        var torrent = (Torrent)e.Row.DataContext!;
        var colorStr = torrent!.DataOwner.PluginInstance.PluginInstanceConfig.Color;
        if (colorStr != null) {
            if (!RowBrushCache.TryGetValue(colorStr, out var brushes)) {
                if (Color.TryParse(colorStr, out var color)) {
                    var luminance = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
                    brushes = (new SolidColorBrush(color), luminance > 186 ? Brushes.Black : Brushes.White);
                }
                RowBrushCache[colorStr] = brushes;
            }
            if (brushes.Background != null && !ReferenceEquals(e.Row.Background, brushes.Background)) {
                e.Row.Background = brushes.Background;
                e.Row.Foreground = brushes.Foreground;
            }
        }

        var loadingCellHooks = Plugin.Plugins.GetHook<object, string, DataGridCell>(Plugin.Plugins.HookType.TorrentListing_EvLoadingCell);

        if (loadingCellHooks.Length > 0) {
            for (var x = 0;x < e.Row.Cells.Count;x++) {
                var cell = e.Row.Cells[x];
                var columnKey = (string)cell.OwningColumn.ColumnKey;

                foreach (var hook in loadingCellHooks) {
                    hook(torrent, columnKey, cell);
                }
            }
        }

        var loadingRowHooks = Plugin.Plugins.GetHook<object, DataGridRowEventArgs>(Plugin.Plugins.HookType.TorrentListing_EvLoadingRow);
        if (loadingRowHooks.Length > 0) {
            foreach (var hook in loadingRowHooks) {
                hook(sender, e);
            }
        }
    }

    private async Task<bool> ShowRecheckTorrentsConfirmationDialog((Window BaseWindow, ulong Size, int Count) Input)
    {
        var window = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams() {
            ButtonDefinitions = ButtonEnum.YesNo,
            ContentTitle = "RT#",
            ContentMessage = $"Are you sure you want to recheck {Input.Count} torrent{(Input.Count == 1 ? "" : "s")} ({Shared.Utils.Converters.GetSIDataSize(Input.Size)})?",
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
            ContentMessage = $"Are you sure you want to delete {Input.Count} torrent{(Input.Count == 1 ? "" : "s")}{(Input.AndData ? $" and their data ({Shared.Utils.Converters.GetSIDataSize(Input.Size)})" : "")}?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon.Question
        });

        return (await window.ShowWindowDialogAsync(Input.BaseWindow)) == ButtonResult.Yes;
    }

    private async Task<string?> ShowSelectRemoteDirectoryDialog((Window Owner, string Title, Plugin.RTSharpDataProvider DataProvider, string? StartingDir) Input)
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

    public void EvLabelsMenuOpening(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewModel!.RefreshLabelCheckedState();

    public void EvGridKeyUp(object? sender, Avalonia.Input.KeyEventArgs e) => ViewModel!.EvGridKeyUp(e);
}
