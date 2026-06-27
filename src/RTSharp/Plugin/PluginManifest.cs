using System.Text.Json.Serialization;

namespace RTSharp.Plugin;

/// <summary>
/// Plugin manifest, mostly deserialized from <c>manifest.json</c>
/// </summary>
public record PluginManifest
{
    [JsonPropertyName("Module")]
    public required string Module { get; set; }

    [JsonPropertyName("Type")]
    public PLUGIN_TYPE? Type { get; set; }

    [JsonPropertyName("Description")]
    public required string Description { get; set; }

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Module);

    [JsonIgnore]
    public bool IsDataProvider => Type == PLUGIN_TYPE.DATA_PROVIDER;

    /// <summary>
    /// Absolute module directory path
    /// </summary>
    [JsonIgnore]
    public required string ModuleDir { get; set; }
}

public enum PLUGIN_TYPE
{
    DATA_PROVIDER
}