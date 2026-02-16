using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Models;

public partial class Server : ObservableObject
{
    [ObservableProperty]
    public partial string ServerId { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial ushort DaemonPort { get; set; }
}
