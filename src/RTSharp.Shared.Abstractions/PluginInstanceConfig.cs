namespace RTSharp.Shared.Abstractions;

public class PluginInstanceConfig
{
    public PluginInstanceConfig(string Name, string Color)
    {
        this.Name = Name;
        this.Color = Color;
    }

    [Obsolete("For serialization only", true)]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public PluginInstanceConfig()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
    }

    public string Name { get; set; }
    public string Color { get; init; }
}