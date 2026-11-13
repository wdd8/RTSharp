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
    void IPluginInit.InitHooks(IPluginHost Host) => InitHooks(new PluginHostProxy<TConfig>(Host));

    /// <inheritdoc cref="IPluginInit.InitHooks" />
    public virtual void InitHooks(IPluginHost<TConfig> Host) { }

    /// <inheritdoc />
    Task IPluginInit.InitDataProviders(IPluginHost Host) => InitDataProviders(new PluginHostProxy<TConfig>(Host));

    /// <inheritdoc cref="IPluginInit.InitDataProviders" />
    public virtual Task InitDataProviders(IPluginHost<TConfig> Host) => Task.CompletedTask;

    /// <inheritdoc />
    Task IPluginInit.Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress) => Init(new PluginHostProxy<TConfig>(Host), Progress);

    /// <inheritdoc cref="IPluginInit.Init" />
    public virtual Task Init(IPluginHost<TConfig> Host, Action<(string Status, float Percentage)> Progress) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task ShowPluginSettings(object ParentWindow) { throw new NotImplementedException(); }

    /// <inheritdoc />
    public virtual Task<dynamic?> CustomAccess(dynamic? In) { return Task.FromResult<dynamic?>(null); }

    /// <inheritdoc />
    public abstract Task Unload();
}
