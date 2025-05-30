namespace RTSharp.Shared.Abstractions;

public class DataProviderInstanceConfig
{
    public DataProviderInstanceConfig(TimeSpan ListUpdateInterval, string ServerId, string Name)
    {
        this.ListUpdateInterval = ListUpdateInterval;
        this.ServerId = ServerId;
        this.Name = Name;
    }

    public DataProviderInstanceConfig()
    {
    }

    public TimeSpan ListUpdateInterval { get; set; }
    
    public string Name { get; set; }

    public string ServerId { get; set; }
}