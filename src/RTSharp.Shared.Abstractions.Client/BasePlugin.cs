using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Shared.Abstractions.Client;

public abstract class BasePlugin<TConfig> : IPlugin, IPluginInit
    where TConfig : IPluginConfigContainer<PluginInstanceConfig>
{
    public virtual void ConfigureServices(IServiceCollection Services) { }

    /// <inheritdoc />
    public abstract Guid GUID { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract string Author { get; }

    /// <inheritdoc />
    public abstract int CompatibleMajorVersion { get; }

    /// <inheritdoc />
    public abstract PluginCapabilities Capabilities { get; }

    Type IPluginInit.ConfigType => typeof(TConfig);

    /// <inheritdoc />
    Task<IPlugin> IPluginInit.Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress)
        => Init(new PluginHostProxy<TConfig>(Host), Progress);

    /// <summary>
    /// Initialize plugin
    /// </summary>
    public abstract Task<IPlugin> Init(IPluginHost<TConfig> Host, Action<(string Status, float Percentage)> Progress);

    /// <inheritdoc />
    public virtual Task ShowPluginSettings(object ParentWindow) { throw new NotImplementedException(); }

    /// <inheritdoc />
    public virtual Task<dynamic?> CustomAccess(dynamic? In) { return Task.FromResult<dynamic?>(null); }

    /// <inheritdoc />
    public abstract Task Unload();
}
