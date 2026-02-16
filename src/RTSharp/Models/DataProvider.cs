using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions;

using System;

namespace RTSharp.Models;

public partial class DataProvider : ObservableObject
{
    [ObservableProperty]
    public partial DataProviderState State { get; set; }

    [ObservableProperty]
    public partial TimeSpan Latency { get; set; }

    [ObservableProperty]
    public partial ulong TotalDLSpeed { get; set; }

    [ObservableProperty]
    public partial ulong TotalUPSpeed { get; set; }

    [ObservableProperty]
    public partial uint ActiveTorrentCount { get; set; }
    public string DisplayName { get; set; }

    public Guid InstanceId;
}
