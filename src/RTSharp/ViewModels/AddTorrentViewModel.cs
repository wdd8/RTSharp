using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.Plugin;
using Serilog;

namespace RTSharp.ViewModels
{
    public partial class AddTorrentViewModel : ObservableObject
    {
        public List<RTSharpDataProvider> DataProviders => [.. Plugin.Plugins.DataProviders.Items.Where(x => x.Instance.Capabilities.AddTorrent)];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ForceStartTorrentOnAdd))]
        [NotifyCanExecuteChangedFor(nameof(BrowseRemoteDirectoryClickCommand))]
        public partial RTSharpDataProvider? SelectedProvider { get; set; }

        [ObservableProperty]
        public partial bool FromFileSelected { get; set; }

        private List<string>? _selectedFiles = null;
        public string? SelectedFileTextBox {
            get {
                return _selectedFiles switch {
                    null => "",
                    { Count: 1 } => _selectedFiles.First(),
                    _ => "<multiple files>"
                };
            }
            set {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                this.SetProperty(ref _selectedFiles, new List<string>() { value }, nameof(SelectedFileTextBox));
            }
        }

        [ObservableProperty]
        public partial bool FromUriSelected { get; set; }

        private string? _uri = null;
        public string? Uri {
            get {
                return _uri;
            }
            set {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (!System.Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new DataValidationException("Uri is not valid");

                this.SetProperty(ref _uri, value, nameof(Uri));
            }
        }

        [ObservableProperty]
        public partial bool FromClipboardSelected { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddClickCommand))]
        public partial string RemoteTargetPath { get; set; }

        [ObservableProperty]
        public partial object DataProviderOptions { get; set; }

        [ObservableProperty]
        public partial List<RTSharpDataProvider> DuplicationTargets { get; set; }

        private bool _startTorrent;
        public bool StartTorrent {
            get {
                if (SelectedProvider == null)
                    return false;

                if (SelectedProvider.Instance.Capabilities.ForceStartTorrentOnAdd != null)
                    return SelectedProvider.Instance.Capabilities.ForceStartTorrentOnAdd.Value;

                return _startTorrent;
            }
            set {
                this.SetProperty(ref _startTorrent, value, nameof(StartTorrent));
            }
        }

        public bool ForceStartTorrentOnAdd => SelectedProvider?.Instance.Capabilities.ForceStartTorrentOnAdd != null;

        public Action CancelWindow { get; set; }

        public Func<string?, Task<IEnumerable<IStorageFile>>> OpenLocalFileDialog { get; set; }

        public Func<string?, Task<string?>> SelectRemoteDirectoryDialog { get; set; }

        public Func<Task> PreviewClipboard { get; set; }

        public Func<Task<string?>> GetClipboard { get; set; }

        [RelayCommand]
        public async Task BrowseRemoteDirectoryClick()
        {
            var directory = await SelectRemoteDirectoryDialog(RemoteTargetPath);

            if (directory != null)
                RemoteTargetPath = directory;
        }

        [RelayCommand]
        public async Task PreviewClipboardClick()
        {
            await PreviewClipboard();
        }

        [RelayCommand]
        public async Task BrowseLocalFileClick()
        {
            var files = await OpenLocalFileDialog(SelectedFileTextBox);
            if (!files.Any())
                return;
            _selectedFiles = files.Select(x => x.Path.LocalPath).ToList();
            this.OnPropertyChanged(nameof(SelectedFileTextBox));
        }

        [RelayCommand]
        public void CancelClick()
        {
            CancelWindow();
        }

        public bool CanExecuteAddClick() => !String.IsNullOrEmpty(this.RemoteTargetPath);

        [RelayCommand(CanExecute = nameof(CanExecuteAddClick))]
        public async Task AddClick()
        {
            var input = new Core.TorrentAdd.TorrentAddInput();
            IClipboard? clipboardObj;
            IList<string> sources;

            if (SelectedProvider == null)
                return;

            if (FromFileSelected) {
                if (_selectedFiles == null)
                    return;

                sources = _selectedFiles;
            } else if (FromUriSelected) {
                if (_uri == null)
                    return;

                sources = new[] { _uri };
            } else if (FromClipboardSelected) {
                var clipboard = await GetClipboard();

                if (String.IsNullOrEmpty(clipboard))
                    return;

                sources = clipboard.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            } else
                return;

            input.Sources = sources.Select(x => (x, new Shared.Abstractions.AddTorrentsOptions(null, RemoteTargetPath))).ToList();
            input.StartMode = StartTorrent ? Core.TorrentAdd.START_MODE.START : Core.TorrentAdd.START_MODE.DO_NOTHING;
            input.DuplicationTargets = DuplicationTargets;

            try {
                await Core.TorrentAdd.AddTorrents(SelectedProvider, input);
            } catch { }
        }

        [RelayCommand]
        public async Task ProviderChanged(RTSharpDataProvider SelectedProvider)
        {
            try {
                if (SelectedProvider == null)
                    return;

                var savePath = await SelectedProvider.Instance.Files.GetDefaultSavePath();
                RemoteTargetPath = savePath;
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Failed to get default save path");
                throw;
            }
        }
    }
}
