namespace RTSharp.Shared.Abstractions;

public class DataProviderInstanceConfig
{
    public DataProviderInstanceConfig(TimeSpan ListUpdateInterval, string ServerId, string Name)
    {
        this.ListUpdateInterval = ListUpdateInterval;
        this.ServerId = ServerId;
        this.Name = Name;
    }

    [Obsolete("For serialization purposes only.", true)]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public DataProviderInstanceConfig()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
    }

    public TimeSpan ListUpdateInterval { get; set; }
    
    public string Name { get; set; }

    public string ServerId { get; set; }
}