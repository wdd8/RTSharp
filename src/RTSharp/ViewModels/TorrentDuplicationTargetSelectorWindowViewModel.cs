using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using System;
using System.ComponentModel;

namespace RTSharp.ViewModels
{
    public partial class TorrentDuplicationTargetSelectorWindowViewModel : ObservableObject
    {
        public DuplicationTargetSelectorViewModel Selector { get; } = new();

        public Shared.Abstractions.Torrent SourceTorrent { get; }

        public Action<bool> CloseDialog { get; set; } = null!; // view set

        public TorrentDuplicationTargetSelectorWindowViewModel(Shared.Abstractions.Torrent SourceTorrent)
        {
            this.SourceTorrent = SourceTorrent;

            var orig = Selector.ResolveTarget;
            Selector.ResolveTarget = async provider => {
                if (SourceTorrent.DataOwner.Host.Config.Plugin.ServerId != provider.Host.Config.Plugin.ServerId)
                    return await orig(provider);

                return (SourceTorrent.RemotePath, false);
            };

            Selector.PropertyChanged += Selector_PropertyChanged;

            foreach (var hook in Plugin.Plugins.GetHookAsync<object>(Plugin.Plugins.HookType.AddTorrent_EvDragDrop)) {
                try {
                    _ = hook(this);
                } catch (Exception ex) {
                    Log.Logger.Error(ex, "TorrentDuplicationTargetSelectorWindowViewModel ctor hook error");
                }
            }
        }

        private void Selector_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Selector.SelectedProvider))
                ConfirmClickCommand.NotifyCanExecuteChanged();
        }

        public bool CanExecuteConfirmClick() => Selector.SelectedProvider != null;

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
