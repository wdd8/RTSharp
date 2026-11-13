using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Controls.ViewModels;

public partial class WaitingBoxViewModel : ObservableObject
{
    public string Text { get; }
    public BuiltInIcons Icon { get; }

    public WaitingBoxViewModel(string Text, string Description, BuiltInIcons Icon)
    {
        this.Text = Text;
        this.Description = Description;
        this.Icon = Icon;
    }

    [ObservableProperty]
    public int progress;

    [ObservableProperty]
    public string description;
}
