using CommunityToolkit.Mvvm.ComponentModel;

using System;

namespace RTSharp.Plugin;

/// <summary>
/// A configured plugin instance backed by the config file in the plugins config
/// path <see cref="Shared.Abstractions.Consts.PLUGINS_PATH" />.
/// </summary>
public partial class ConfiguredPlugin : ObservableObject
{
    public required Guid InstanceId { get; set; }

    public required PluginManifest Manifest { get; set; }

    public required string ConfigPath { get; set; }

    [ObservableProperty]
    public partial string InstanceName { get; set; } = "";

    [ObservableProperty]
    public partial string? ServerId { get; set; }

    [ObservableProperty]
    public partial string? DataProviderName { get; set; }

    [ObservableProperty]
    public partial string? Color { get; set; }
}
