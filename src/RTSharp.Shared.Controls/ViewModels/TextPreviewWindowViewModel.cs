
using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Controls.ViewModels
{
    public partial class TextPreviewWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string Text { get; set; } = "";

        [ObservableProperty]
        public partial bool Monospace { get; set; }

        [ObservableProperty]
        public partial string FontFamily { get; set; } = "Consolas";

        public TextPreviewWindowViewModel()
        {
        }

        partial void OnMonospaceChanged(bool oldValue, bool newValue)
        {
            this.FontFamily = newValue ? "Consolas" : "sans-serif";
        }
    }
}
