using RTSharp.Shared.Controls;
using RTSharp.Shared.Controls.Views;
using RTSharp.ViewModels.TorrentListing;

namespace RTSharp.Views.TorrentListing
{
	public partial class TorrentFilesView : VmUserControl<TorrentFilesViewModel>
	{
		public TorrentFilesView()
		{
			InitializeComponent();

			BindViewModelActions(vm => {
				vm!.ShowTextPreviewWindow = ShowTextPreviewWindow;
			});
		}

		public void ShowTextPreviewWindow(string Input)
		{
			var wnd = new TextPreviewWindow() {
				ViewModel = new Shared.Controls.ViewModels.TextPreviewWindowViewModel() { 
					Text = Input,
					Monospace = true
				}
			};

			wnd.Show();
		}
	}
}
