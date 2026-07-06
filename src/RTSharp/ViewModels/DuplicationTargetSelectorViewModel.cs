using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels;

public partial class DuplicationTargetSelectorViewModel : ObservableObject
{
    public Func<IDataProvider, bool> ProviderFilter { get; set; } = _ => true;

    public List<IDataProvider> DataProviders => [.. Plugins.DataProviders.Items.Select(x => x.Instance).Where(ProviderFilter)];

    public void RefreshDataProviders() => OnPropertyChanged(nameof(DataProviders));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
    public partial IDataProvider? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial string? RemoteTargetPath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
    public partial bool RemoteTargetPathEnabled { get; set; }

    public Func<string?, Task<string?>> SelectRemoteDirectoryDialog { get; set; } = null!; // consumer view set

    public Func<IDataProvider, Task<(string? Path, bool Enabled)>> ResolveTarget { get; set; } =
        async provider => (await provider.Files.GetDefaultSavePath(), true);

    partial void OnSelectedProviderChanged(IDataProvider? value) => _ = UpdateRemoteTargetForProvider(value);

    private async Task UpdateRemoteTargetForProvider(IDataProvider? provider)
    {
        try {
            RemoteTargetPathEnabled = false;
            if (provider == null)
                return;

            RemoteTargetPath = null;
            var (path, enabled) = await ResolveTarget(provider);
            RemoteTargetPath = path;
            RemoteTargetPathEnabled = enabled;
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to get default save path");
        }
    }

    public bool CanExecuteBrowseRemoteDirectoryClick() => RemoteTargetPathEnabled;

    [RelayCommand(CanExecute = nameof(CanExecuteBrowseRemoteDirectoryClick))]
    public async Task BrowseRemoteDirectoryClick()
    {
        var directory = await SelectRemoteDirectoryDialog(RemoteTargetPath);

        if (directory != null)
            RemoteTargetPath = directory;
    }
}
