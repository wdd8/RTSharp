using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Shared.Abstractions.Client;

public interface IPluginInit
{
    /// <summary>
    /// Called before <see cref="Init"/>
    /// </summary>
    void ConfigureServices(IServiceCollection Services) { }

    /// <summary>
    /// Initialize plugin
    /// </summary>
    /// <param name="Host">Host object</param>
    /// <param name="Progress">Progress report - status message and progress %</param>
    Task<IPlugin> Init(IPluginHost Host, Action<(string Status, float Percentage)> Progress);

    /// <summary>
    /// Major RT# version which this plugin is compatible with
    /// </summary>
    public int CompatibleMajorVersion { get; }

    /// <summary>
    /// Version
    /// </summary>
    Version Version { get; }

    public PluginCapabilities Capabilities { get; }
}
