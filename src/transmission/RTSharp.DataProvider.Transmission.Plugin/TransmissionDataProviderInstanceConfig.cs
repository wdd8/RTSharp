using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class TransmissionDataProviderInstanceConfig : PluginConfigContainer<DataProviderInstanceConfig>
{
    public TimeSpan ListUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);
}
