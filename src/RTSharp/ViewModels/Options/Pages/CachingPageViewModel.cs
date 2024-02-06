using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.ViewModels.Options.Pages
{
    public partial class CachingPageViewModel : ObservableObject, ISettingsLoadable
    {
        public ulong FilesCacheSize { get; set; }

        [ObservableProperty]
        public bool filesCachingEnabled;
    }
}
