
using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Controls.ViewModels
{
	public partial class TextPreviewWindowViewModel : ObservableObject
	{
		[ObservableProperty]
		public string text;

		[ObservableProperty]
		public bool monospace;

		[ObservableProperty]
		public string fontFamily = "Consolas";

		public TextPreviewWindowViewModel()
		{
		}

		partial void OnMonospaceChanged(bool oldValue, bool newValue)
		{
			this.FontFamily = newValue ? "Consolas" : "sans-serif";
		}
	}
}
