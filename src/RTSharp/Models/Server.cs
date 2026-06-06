using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Models;

public partial class Server : ObservableObject
{
    [ObservableProperty]
    public partial string ServerId { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial ushort Port { get; set; }

    public string Endpoint => $"{Host}:{Port}";

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Unknown";

    [ObservableProperty]
    public partial string? Latency { get; set; }

    [ObservableProperty]
    public partial string? CertThumbprint { get; set; }

    [ObservableProperty]
    public partial string? CertNotBefore { get; set; }

    [ObservableProperty]
    public partial string? CertNotAfter { get; set; }

    [ObservableProperty]
    public partial string? CertAlgorithm { get; set; }
}
