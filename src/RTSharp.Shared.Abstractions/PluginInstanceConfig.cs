namespace RTSharp.Shared.Abstractions;

public class PluginInstanceConfig
{
    public PluginInstanceConfig(string Name, string Color, string ServerId)
    {
        this.Name = Name;
        this.Color = Color;
        this.ServerId = ServerId;
    }

    public PluginInstanceConfig()
    {
    }

    public string Name { get; set; }
    public string Color { get; init; }
    public string ServerId { get; init; }
}