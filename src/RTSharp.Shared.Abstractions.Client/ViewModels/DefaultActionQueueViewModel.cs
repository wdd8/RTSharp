using CommunityToolkit.Mvvm.ComponentModel;

using Avalonia.Media;

using System.Collections.ObjectModel;

namespace RTSharp.Shared.Abstractions.Client.ViewModels;

public partial class DefaultActionQueueViewModel : ObservableObject
{
    public string DisplayName { get; set; }

    public IImage? Icon { get; set; }

    public bool HasIcon => Icon != null;

    [ObservableProperty]
    public partial uint ActionsInQueue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial uint ErroredActions { get; set; }

    public bool HasErrors => ErroredActions > 0;

    public ObservableCollection<DefaultActionQueueActionViewModel> Actions { get; } = new();
}
