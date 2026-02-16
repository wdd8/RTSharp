using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.ViewModels
{
    public partial class ServerActionQueueViewModel : ObservableObject
    {
        public string DisplayName { get; set; }

        [ObservableProperty]
        public partial uint ActionsInQueue { get; set; }

        [ObservableProperty]
        public partial uint ErroredActions { get; set; }

        [ObservableProperty]
        public partial string ActionQueueString { get; set; }
    }
}
