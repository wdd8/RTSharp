using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Shared.Abstractions.Client;

public interface IPluginInit
{
    /// <summary>
    /// Called first, before any of the init functions
    /// </summary>
    void ConfigureServices(IServiceCollection Services) { }

    /// <summary>
    /// Hooks the torrent listing, before it shows any torrents. Called after <see cref="ConfigureServices"/>
    /// </summary>
    /// <param name="Host">Handle to the host application.</param>
    void InitHooks(IPluginHost Host) { }

    /// <summary>
    /// Registers data providers, which are polled once this returns. Called after <see cref="InitHooks"/>, before the main window exists
    /// </summary>
    /// <param name="Host">Handle to the host application.</param>
    Task InitDataProviders(IPluginHost Host) => Task.CompletedTask;

    /// <summary>
    /// Initialize plugin. Called after <see cref="InitDataProviders"/>, once the main window exists
    /// </summary>
    /// <param name="Host">Handle to the host application.</param>
    /// <param name="Progress">Progress report - status message and progress %</param>
    Task Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress);

    /// <summary>
    /// Plugin configuration container type (the <typeparamref name="TConfig"/> of
    /// <see cref="BasePlugin{TConfig}"/>).
    /// </summary>
    Type ConfigType { get; }

    /// <summary>
    /// Major RT# version which this plugin is compatible with
    /// </summary>
    public int CompatibleMajorVersion { get; }
}
