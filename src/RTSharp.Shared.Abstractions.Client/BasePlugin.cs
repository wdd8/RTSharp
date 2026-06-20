using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Shared.Abstractions.Client;

public abstract class BasePlugin : IPlugin, IPluginInit
{
    public virtual void ConfigureServices(IServiceCollection Services) { }

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

    public abstract int CompatibleMajorVersion { get; }

    public abstract PluginCapabilities Capabilities { get; }

    public abstract Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress);

    public virtual Task ShowPluginSettings(object ParentWindow) { throw new NotImplementedException(); }

    public virtual Task<dynamic?> CustomAccess(dynamic? In) { return Task.FromResult<dynamic?>(null); }

    public abstract Task Unload();
}
