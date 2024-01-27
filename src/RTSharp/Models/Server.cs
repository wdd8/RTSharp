using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Models;

public partial class Server : ObservableObject
{
    [ObservableProperty]
    public string serverId;

    [ObservableProperty]
    public string host;

    [ObservableProperty]
    public ushort auxiliaryServicePort;
}
