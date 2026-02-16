using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Models;
using RTSharp.Plugin;

using Serilog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels
{
    public partial class TorrentDuplicationTargetSelectorWindowViewModel : ObservableObject
    {
        public List<Plugin.RTSharpDataProvider> DataProviders => [.. Plugin.Plugins.DataProviders.Items];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
        [NotifyCanExecuteChangedFor(nameof(ConfirmClickCommand))]
        public partial Plugin.RTSharpDataProvider? SelectedProvider { get; set; }

        [ObservableProperty]
        public partial string RemoteTargetPath { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
        public partial bool RemoteTargetPathEnabled { get; set; }
        public Torrent SourceTorrent { get; }

        public Func<string?, Task<string?>> SelectRemoteDirectoryDialog { get; set; }

        public Action<bool> CloseDialog { get; set; }

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

        [RelayCommand]
        public async Task ProviderChanged(Plugin.RTSharpDataProvider SelectedProvider)
        {
            try {
                RemoteTargetPathEnabled = false;
                if (SelectedProvider == null)
                    return;

                RemoteTargetPathEnabled = true;

                if (SourceTorrent.DataOwner.DataProviderInstanceConfig.ServerId != SelectedProvider.DataProviderInstanceConfig.ServerId) {
                    var savePath = await SelectedProvider.Instance.Files.GetDefaultSavePath();
                    RemoteTargetPath = savePath;
                } else {
                    RemoteTargetPath = SourceTorrent.RemotePath;
                    RemoteTargetPathEnabled = false;
                }
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Failed to get default save path");
                throw;
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

            if (SourceTorrent.DataOwner.PluginInstance.InstanceId == SelectedProvider.PluginInstance.InstanceId)
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
