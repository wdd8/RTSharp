using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.ViewModels
{
    public partial class ServerActionQueueViewModel : ObservableObject
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
