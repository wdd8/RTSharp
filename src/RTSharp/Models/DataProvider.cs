using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Controls;

using System;

namespace RTSharp.Models;

public partial class DataProvider : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateIcon))]
    public partial DataProviderState State { get; set; }

    public BuiltInIcons StateIcon => State switch {
        DataProviderState.INACTIVE => BuiltInIcons.LINK_BROKEN,
        DataProviderState.ACTIVE => BuiltInIcons.LINK_OK,
        _ => BuiltInIcons.SERIOUS_QUESTION
    };

    [ObservableProperty]
    public partial string Latency { get; set; }

    [ObservableProperty]
    public partial ulong TotalDLSpeed { get; set; }

    [ObservableProperty]
    public partial ulong TotalUPSpeed { get; set; }

    [ObservableProperty]
    public partial uint ActiveTorrentCount { get; set; }
    public required string DisplayName { get; set; }

    public Guid InstanceId;
}
