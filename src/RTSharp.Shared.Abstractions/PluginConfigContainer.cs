namespace RTSharp.Shared.Abstractions;

/// <summary>
/// The required <c>Plugin</c> section. The surrounding container
/// (<see cref="IPluginConfigContainer{TPlugin}"/>) carries plugin-specific settings
/// at the config root.
/// </summary>
public class PluginInstanceConfig
{
    /// <summary>
    /// Config key for the shared metadata section (a sibling of the plugin's own root-level settings).
    /// </summary>
    public const string Section = "Plugin";

    /// <summary>
    /// Path to module, relative off of <see cref="Consts.PLUGINS_PATH"/> or absolute
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Unique name for plugin instance
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Unique plugin instance id
    /// </summary>
    public required Guid InstanceId { get; set; }
}

/// <summary>
/// The <c>Plugin</c> section for data providers. Extends <see cref="PluginInstanceConfig"/> with
/// the data provider specific settings.
/// </summary>
public class DataProviderInstanceConfig : PluginInstanceConfig
{
    /// <summary>
    /// RTSharp.Daemon-side data provider name. Sent as the <c>data-provider</c> gRPC header to
    /// select which configured provider the daemon should target.
    /// </summary>
    public required string ProviderName { get; set; }

    /// <summary>
    /// Attached RTSharp.Daemon server ID.
    /// </summary>
    public required string ServerId { get; set; }

    /// <summary>
    /// Instance color as a hex string (e.g. "#7F7F7F"). <c>null</c> uses the default.
    /// </summary>
    public required string? Color { get; set; }
}

/// <summary>
/// Interface enforcing required <c>Plugin</c> section.
/// </summary>
public interface IPluginConfigContainer<out TPlugin>
    where TPlugin : PluginInstanceConfig
{
    /// <summary>
    /// The required <c>Plugin</c> section
    /// </summary>
    TPlugin Plugin { get; }
}

/// <summary>
/// Default implementation for <see cref="IPluginConfigContainer{TPlugin}"/>
/// </summary>
/// <typeparam name="TPlugin">Plugin-specific configuration part</typeparam>
public class PluginConfigContainer<TPlugin> : IPluginConfigContainer<TPlugin>
    where TPlugin : PluginInstanceConfig
{
    public required TPlugin Plugin { get; set; }
}
