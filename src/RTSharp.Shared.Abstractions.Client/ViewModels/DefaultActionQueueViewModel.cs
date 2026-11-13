using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;

namespace RTSharp.Shared.Abstractions.Client.ViewModels;

public partial class DefaultActionQueueViewModel : ObservableObject
{
    public required string DisplayName { get; set; }

    /// <summary>
    /// An IImage, or a value a data template displays, such as a built-in icon
    /// </summary>
    public object? Icon { get; set; }

    public bool HasIcon => Icon != null;

    [ObservableProperty]
    public partial uint ActionsInQueue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial uint ErroredActions { get; set; }

    public bool HasErrors => ErroredActions > 0;

    public ObservableCollection<DefaultActionQueueActionViewModel> Actions { get; } = new();
}
