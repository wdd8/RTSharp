namespace RTSharp.Shared.Abstractions.Client;

/// <summary>
/// Handle to the host application through a data provider
/// </summary>
/// <typeparam name="TConfig">Provider config type</typeparam>
public interface IDataProviderHost<TConfig> : IDataProviderHost, IPluginHost<TConfig>, IDisposable
    where TConfig : IPluginConfigContainer<DataProviderInstanceConfig>
{
    /// <summary>
    /// Provider instance configuration: the bound <c>Plugin</c> section (<c>Config.Plugin</c>) plus any
    /// plugin-specific root-level settings.
    /// </summary>
    new TConfig Config { get; }
}
