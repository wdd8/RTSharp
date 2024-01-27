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
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using RTSharp.Core.Util;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace RTSharp.Views.TorrentListing;

public partial class TorrentListingView : VmUserControl<TorrentListingViewModel>
{
	public TorrentListingView()
    {
        InitializeComponent();

		BindViewModelActions(vm => {
			using var scope = Core.ServiceProvider.CreateScope();
			var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

			grid.RestoreState(config.UIState.Value.TorrentGridState);

			vm!.ResortList = ResortList;
			vm!.RecheckTorrentsConfirmationDialog = ShowRecheckTorrentsConfirmationDialog;
			vm!.MoveDownloadDirectoryConfirmationDialog = ShowMoveDownloadDirectoryConfirmationDialog;
			vm!.SelectDirectoryDialog = ShowSelectDirectoryDialog;
			vm!.DeleteTorrentsConfirmationDialog = ShowDeleteTorrentsConfirmationDialog;
			vm!.SelectRemoteDirectoryDialog = ShowSelectRemoteDirectoryDialog;
			vm!.ShowAddLabelDialog = ShowAddLabelDialog;
			vm!.CloseAddLabelDialog = CloseAddLabelDialog;
			vm!.OnViewModelAttached(this);
		}, null);

		grid.Sorting += async (sender, e) => {
			await Task.Delay(3000); // HACKHACK: Extreme hack! Sorting doesn't provide any information about the sort action itself and current sorting state of columns is not updated, so sort action did not happen yet. Hope that it completes in 3 seconds
			SaveGridState();
		};

		this.Loaded += (sender, e) => {
			using var scope = Core.ServiceProvider.CreateScope();
			var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

			grid.RestoreState(config.UIState.Value.TorrentGridState);
			App.RegisterOnExit($"{nameof(TorrentListingView)}_{nameof(grid)}_{nameof(SaveGridState)}", () => SaveGridState());
		};
	}

	public override void EndInit() => base.EndInit();

	public TorrentListingViewModel? TypedContext => (TorrentListingViewModel?)DataContext;

	private void ResortList()
	{
		foreach (var col in grid.Columns) {
			var sortDirection = col.GetSortDirection();
			if (sortDirection == null)
				continue;

			col.Sort(sortDirection.Value);
		}
	}

	public async void SaveGridState()
	{
		try {
			if (grid.StateChanged()) {
				var state = grid.SaveState();

				using var scope = Core.ServiceProvider.CreateScope();
				var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
				config.UIState.Value.TorrentGridState = state;
				await config.Rewrite();
			}
		} catch (Exception ex) {
			Log.Logger.Error(ex, "SaveGridState");
		}
	}

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
        if (Color.TryParse(torrent!.Owner.PluginInstance.PluginInstanceConfig.Color, out var color)) {
			e.Row.Background = new SolidColorBrush(color);
			if (color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 186) {
				e.Row.Foreground = Brushes.Black;
			} else {
				e.Row.Foreground = Brushes.White;
			}
        }

		var hooks = Plugin.Plugins.GetHook<object, DataGridRowEventArgs>(Plugin.Plugins.HookType.TorrentListing_EvLoadingRow);
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
