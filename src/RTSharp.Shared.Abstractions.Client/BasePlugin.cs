namespace RTSharp.Shared.Abstractions.Client;

public abstract class BasePlugin : IPlugin, IPluginInit
{
    public abstract IPluginHost Host { get; set; }

    /// <summary>
    /// A GUID that is unique to a plugin, but not an instance of plugin.
    /// </summary>
    public abstract Guid GUID { get; }

    /// <summary>
    /// Required non-custom plugin config part
    /// </summary>
    public PluginInstanceConfig PluginInstanceConfig => Host.PluginInstanceConfig;

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string Author { get; }

    public abstract Version Version { get; }

    public abstract int CompatibleMajorVersion { get; }

    public abstract PluginCapabilities Capabilities { get; }

    public abstract Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress);

    public abstract Task ShowPluginSettings(object ParentWindow);

    public abstract Task<dynamic> CustomAccess(dynamic In);

    public abstract Task Unload();
}
