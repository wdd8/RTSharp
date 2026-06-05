using Avalonia.Media.Imaging;

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

    public Bitmap StateIcon => State switch {
        DataProviderState.INACTIVE => BuiltInIcon.Get(BuiltInIcons.LINK_BROKEN),
        DataProviderState.ACTIVE => BuiltInIcon.Get(BuiltInIcons.LINK_OK),
        _ => BuiltInIcon.Get(BuiltInIcons.SERIOUS_QUESTION)
    };

    [ObservableProperty]
    public partial TimeSpan Latency { get; set; }

    [ObservableProperty]
    public partial ulong TotalDLSpeed { get; set; }

    [ObservableProperty]
    public partial ulong TotalUPSpeed { get; set; }

    [ObservableProperty]
    public partial uint ActiveTorrentCount { get; set; }
    public required string DisplayName { get; set; }

    public Guid InstanceId;
}
