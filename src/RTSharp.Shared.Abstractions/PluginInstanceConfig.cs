namespace RTSharp.Shared.Abstractions;

public class PluginInstanceConfig
{
    public PluginInstanceConfig(string Name, string Color)
    {
        this.Name = Name;
        this.Color = Color;
    }

    public PluginInstanceConfig()
    {
    }

    public string Name { get; set; }
    public string Color { get; init; }
}