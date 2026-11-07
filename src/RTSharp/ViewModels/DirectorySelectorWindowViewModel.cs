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

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.Services;
using RTSharp.Core.TorrentPolling;
using RTSharp.Models;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;

using Serilog;

namespace RTSharp.ViewModels;

public partial class DirectorySelectorWindowViewModel : ObservableViewModel
{
    public required string WindowTitle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    public partial FileSystemItem? Selected { get; set; }

    public FileSystemItem? Current { get; set; }

    public bool IsBrowser { get; init; }

    public SourceList<FileSystemItem> _itemsSource { get; set; } = new();

    private ReadOnlyObservableCollection<FileSystemItem> _items;
    public ReadOnlyObservableCollection<FileSystemItem> Items => _items;

    public Action? ClearSelection { get; set; }

    public Action<string?> CloseDialog { get; set; } = null!; // view set

    public Func<FileSystemItem, Task<bool>> DoCreateDirectory { get; set; } = null!; // view set

    [ObservableProperty]
    public partial bool RemoveDirectoryAllowed { get; set; }

    [ObservableProperty]
    public partial bool PreviewAllowed { get; set; }

    private readonly IDaemonService Server;

    public DirectorySelectorWindowViewModel(IDaemonService Server)
    {
        var comparer = SortExpressionComparer<FileSystemItem>
            .Ascending(x => x.Display.Name switch { "." => 0, ".." => 1, _ => 2 })
            .ThenByAscending(x => x.Path);
        AddDisposable(_itemsSource.Connect().Sort(comparer).Bind(out _items).Subscribe());

        this.Server = Server;
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
        if (Current == null)
            return;

        var emptyItem = new FileSystemItem(Current.Path + "/New directory", true, null, null, null, null, null, null);
        _itemsSource.Add(emptyItem);
        await Task.Delay(100); // huge fucking hack to wait til the list in grid populates

        var result = await DoCreateDirectory(emptyItem);

        if (!result) {
            _itemsSource.Remove(emptyItem);
            return;
        }

        try {
            await Server.CreateDirectory(emptyItem.Path);
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

        try {
            await Server.RemoveEmptyDirectory(items.First().Path);
        } catch (Exception ex) {
            Log.Logger.Error(ex, $"Failed to remove empty directory \"{items.First().Path}\"");
        }

        if (Current == null)
            return;

        await SetCurrentFolder(Current.Path);
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (Current == null)
            return;

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

        Shared.Abstractions.FileSystemItem? dirInfo;
        try {
            dirInfo = await Server.GetDirectoryInfo(value);
        } catch {
            return;
        }

        if (!dirInfo.Directory)
            return;

        var torrentsByContentPath = TorrentPolling.Torrents.GetSnapshot()
            .Where(x => x.DataOwner.Config.Plugin.ServerId == Server.Id)
            .ToLookup(x => x.RemotePath.TrimEnd('/') + "/" + x.Name);

        List<Torrent> torrentsOf(string Path)
        {
            var ret = new List<Torrent>();
            for (var path = Path; path.Length > 0; path = path[..Math.Max(path.LastIndexOf('/'), 0)])
                ret.AddRange(torrentsByContentPath[path]);
            return ret;
        }

        var newList = new List<FileSystemItem>();

        if (dirInfo.Path != "/") {
            newList.Add(new FileSystemItem(dirInfo.Path + "/.", true, dirInfo.Size, dirInfo.LastModified, dirInfo.Permissions, dirInfo.Owner, dirInfo.Group, dirInfo.LinkTarget) {
                Torrents = torrentsOf(dirInfo.Path)
            });
            newList.Add(new FileSystemItem(dirInfo.Path + "/..", true, null, null, null, null, null, null));
        }

        foreach (var item in dirInfo.Children!.Select(x => new FileSystemItem(x) { Torrents = torrentsOf(x.Path) })) {
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
        PreviewAllowed = !value.IsDirectory;

        if (!value.IsDirectory || value.Display.Name is ".." or ".") {
            return;
        }

        Selected = value;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task Preview(IList In)
    {
        var file = In.Cast<FileSystemItem>().FirstOrDefault(x => !x.IsDirectory);

        if (file == null)
            return;

        using var scope = Core.ServiceProvider.CreateScope();
        var preview = scope.ServiceProvider.GetRequiredService<MediaPreviewService>();
        await preview.OpenPreview(Server, file.Path, file.Size ?? 0);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task MediaInfo(IList In)
    {
        var paths = In.Cast<FileSystemItem>().Where(x => x.Display.Name is not ("." or "..")).Select(x => x.Path).ToArray();

        if (paths.Length == 0)
            return;

        using var scope = Core.ServiceProvider.CreateScope();
        var preview = scope.ServiceProvider.GetRequiredService<MediaPreviewService>();
        await preview.ShowMediaInfo(Server, paths);
    }
}

public static class ExampleDirectorySelectorWindowViewModel
{
    public static DirectorySelectorWindowViewModel ViewModel { get; } = new(null!) {
        WindowTitle = "Directory selector"
    };
}