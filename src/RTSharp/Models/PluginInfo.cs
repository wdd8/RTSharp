using Avalonia;

namespace RTSharp.Models;

public class PluginInfo : AvaloniaObject
{
    public required string InstanceGuid { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Author { get; init; }

    public required string Version { get; init; }

    public required string PluginGuid { get; init; }
}