using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;
using DynamicData.Binding;

using RTSharp.Core.Services.Auxiliary;
using RTSharp.Models;
using RTSharp.Plugin;
using Serilog;

namespace RTSharp.ViewModels;

public partial class DirectorySelectorWindowViewModel : ObservableObject
{
    public string WindowTitle { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SelectCommand))]
	public FileSystemItem? selected;

	public FileSystemItem Current { get; set; }

	public DataProvider DataProvider { get; init; }

	public SourceList<FileSystemItem> _itemsSource { get; set; } = new();

    private ReadOnlyObservableCollection<FileSystemItem> _items;
    public ReadOnlyObservableCollection<FileSystemItem> Items => _items;

	public Action ClearSelection { get; set; }

	public Action<string?> CloseDialog { get; set; }

	public Func<FileSystemItem, Task<bool>> DoCreateDirectory { get; set; }

	[ObservableProperty]
	public bool removeDirectoryAllowed;

	public DirectorySelectorWindowViewModel(DataProvider DataProvider)
    {
		this.DataProvider = DataProvider;
        _itemsSource.Connect().Sort(SortExpressionComparer<FileSystemItem>.Ascending(x => x.Path)).Bind(out _items).Subscribe();
    }

	[RelayCommand]
	public async Task Cancel()
	{
		CloseDialog(null);
	}

	public bool CanExecuteSelect() => this.Selected != null;

	[RelayCommand(CanExecute = nameof(CanExecuteSelect))]
	public async Task Select()
	{
		CloseDialog(Selected!.Path);
	}

	[RelayCommand]
	private async Task CreateDirectory()
	{
		var emptyItem = new FileSystemItem(Current.Path + "/New directory", true, null, null, "d?????????");
		_itemsSource.Add(emptyItem);
		await Task.Delay(100); // huge fucking hack to wait til the list in grid populates

		var result = await DoCreateDirectory(emptyItem);

		if (!result) {
			_itemsSource.Remove(emptyItem);
			return;
		}

        var auxiliary = new AuxiliaryService(DataProvider.PluginInstance.PluginInstanceConfig.ServerId);

        try {
			await auxiliary.CreateDirectory(emptyItem.Path);
		} catch (Exception ex) {
			Log.Logger.Error(ex, $"Failed to create directory \"{emptyItem.Path}\"");
			_itemsSource.Remove(emptyItem);
		}

		await SetCurrentFolder(Current.Path);
	}

	[RelayCommand]
	private async Task RemoveEmptyDirectory(IList Input)
	{
		var items = Input.Cast<FileSystemItem>();

		if (items.Count() > 1)
			return;

		var auxiliary = new AuxiliaryService(DataProvider.PluginInstance.PluginInstanceConfig.ServerId);

		try {
			await auxiliary.RemoveEmptyDirectory(items.First().Path);
		} catch (Exception ex) {
			Log.Logger.Error(ex, $"Failed to remove empty directory \"{items.First().Path}\"");
		}

		await SetCurrentFolder(Current.Path);
	}

	[RelayCommand]
	public async Task SetCurrentFolder(string? value)
	{
		if (ClearSelection != null)
			ClearSelection();

		if (string.IsNullOrEmpty(value)) {
			value = "/";
		}

        var auxiliary = new AuxiliaryService(DataProvider.PluginInstance.PluginInstanceConfig.ServerId);

        Shared.Abstractions.FileSystemItem? dirInfo;
		try {
			dirInfo = await auxiliary.GetDirectoryInfo(value);
		} catch {
			return;
		}

		if (!dirInfo.Directory)
			return;

		var newList = new List<FileSystemItem> {
            new FileSystemItem(dirInfo.Path + "/.", true, dirInfo.Size, dirInfo.LastModified, dirInfo.Permissions),
            new FileSystemItem(dirInfo.Path + "/..", true, 0, null, "d?????????")
        };

		foreach (var item in dirInfo.Children!.Select(x => new FileSystemItem(x))) {
			newList.Add(item);
		}

		_itemsSource.EditDiff(newList, new FileSystemItemEqualityComparer());

        Selected = new FileSystemItem(dirInfo);
		Current = Selected;
	}

	public void SetSelectedItem(FileSystemItem value)
	{
		Selected = Current;
		RemoveDirectoryAllowed = value.IsDirectory;

		if (!value.IsDirectory || value.Display.Name is ".." or ".") {
			return;
		}

		Selected = value;
	}
}

public static class ExampleDirectorySelectorWindowViewModel
{
    public static DirectorySelectorWindowViewModel ViewModel { get; } = new(null!) {
        WindowTitle = "Directory selector"
    };
}