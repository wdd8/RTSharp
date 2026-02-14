using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions;

using System;

namespace RTSharp.Models;

public partial class DataProvider : ObservableObject
{
    [ObservableProperty]
    public DataProviderState state;

    [ObservableProperty]
    public TimeSpan latency;

    [ObservableProperty]
    public ulong totalDLSpeed;

    [ObservableProperty]
    public ulong totalUPSpeed;

    [ObservableProperty]
    public uint activeTorrentCount;

    public string DisplayName { get; set; }

    public Guid InstanceId;
}
