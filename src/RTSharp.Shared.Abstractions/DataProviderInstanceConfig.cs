namespace RTSharp.Shared.Abstractions;

public class DataProviderInstanceConfig
{
    public DataProviderInstanceConfig(TimeSpan ListUpdateInterval, string ServerId)
    {
        this.ListUpdateInterval = ListUpdateInterval;
        this.ServerId = ServerId;
    }

    public DataProviderInstanceConfig()
    {
    }

    public TimeSpan ListUpdateInterval { get; set; }

    public string ServerId { get; set; }
}