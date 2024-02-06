using Avalonia;

namespace RTSharp.Models;

public class PluginInfo : AvaloniaObject
{
    public string InstanceGuid { get; init; }

    public string DisplayName { get; init; }

    public string Description { get; init; }

    public string Author { get; init; }

    public string Version { get; init; }

    public string PluginGuid { get; init; }
}