using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using RTSharp.Shared.Controls;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing
{
	public partial class TorrentTrackersView : VmUserControl<TorrentTrackersViewModel>
	{
		public TorrentTrackersView()
		{
			InitializeComponent();

			BindViewModelActions(vm => {
				vm!.BrowseForIconDialog = ShowBrowserForIconDialog;
			});
		}

		private async Task<string?> ShowBrowserForIconDialog(Window Input)
		{
			var dialog = await Input.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions() {
				Title = "RT# - Browse for icon...",
				AllowMultiple = false,
				FileTypeFilter = new List<FilePickerFileType> {
					new("Image files") {
						Patterns = new List<string> {
							"*.bmp",
							"*.gif",
							"*.ico",
							"*.jpg",
							"*.jpeg",
							"*.png",
							"*.tiff",
							"*.wdp"
						}
					}
				}
			});

			return dialog.FirstOrDefault()?.Path?.LocalPath;
		}
	}
}
