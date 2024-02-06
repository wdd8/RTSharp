using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.DataProvider.Rtorrent.Plugin.ViewModels
{
    public partial class ActionQueueViewModel : ObservableObject
    {
        public string DisplayName { get; set; }

        [ObservableProperty]
        public uint actionsInQueue;

        [ObservableProperty]
        public uint erroredActions;

        [ObservableProperty]
        public string actionQueueString;
    }
}
