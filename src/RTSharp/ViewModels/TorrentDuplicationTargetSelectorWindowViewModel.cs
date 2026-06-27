using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Models;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels
{
    public partial class TorrentDuplicationTargetSelectorWindowViewModel : ObservableObject
    {
        public List<IDataProvider> DataProviders => [.. Plugins.DataProviders.Items.Select(x => x.Instance)];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
        [NotifyCanExecuteChangedFor(nameof(ConfirmClickCommand))]
        public partial IDataProvider? SelectedProvider { get; set; }

        [ObservableProperty]
        public partial string? RemoteTargetPath { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
        public partial bool RemoteTargetPathEnabled { get; set; }
        public Torrent SourceTorrent { get; }

        public Func<string?, Task<string?>> SelectRemoteDirectoryDialog { get; set; } = null!; // view set

        public Action<bool> CloseDialog { get; set; } = null!; // view set

        public TorrentDuplicationTargetSelectorWindowViewModel(Torrent SourceTorrent)
        {
            this.SourceTorrent = SourceTorrent;

            foreach (var hook in Plugin.Plugins.GetHookAsync<object>(Plugin.Plugins.HookType.AddTorrent_EvDragDrop)) {
                try {
                    _ = hook(this);
                } catch (Exception ex) {
                    Log.Logger.Error(ex, "TorrentDuplicationTargetSelectorWindowViewModel ctor hook error");
                }
            }
        }

        partial void OnSelectedProviderChanged(IDataProvider? value)
        {
            _ = UpdateRemoteTargetForProvider(value);
        }

        private async Task UpdateRemoteTargetForProvider(IDataProvider? provider)
        {
            try {
                RemoteTargetPathEnabled = false;
                if (provider == null)
                    return;

                RemoteTargetPathEnabled = true;

                if (SourceTorrent.DataOwner.Config.Plugin.ServerId != provider.Host.Config.Plugin.ServerId) {
                    var savePath = await provider.Files.GetDefaultSavePath();
                    RemoteTargetPath = savePath;
                } else {
                    RemoteTargetPath = SourceTorrent.RemotePath;
                    RemoteTargetPathEnabled = false;
                }
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Failed to get default save path");
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

        public bool CanExecuteConfirmClick()
        {
            if (SelectedProvider == null)
                return false;

            if (SourceTorrent.DataOwner.Host.InstanceId == SelectedProvider.Host.Config.Plugin.InstanceId)
                return false;

            return true;
        }
        [RelayCommand(CanExecute = nameof(CanExecuteConfirmClick))]
        public void ConfirmClick()
        {
            CloseDialog(true);
        }

        [RelayCommand]
        public void CancelClick()
        {
            CloseDialog(false);
        }
    }
}
