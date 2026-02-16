namespace RTSharp.Shared.Abstractions;

/// <summary>
/// Plugin
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Plugin name
    /// </summary>
    string Name { get; }
    /// <summary>
    /// Additional description
    /// </summary>
    string Description { get; }
    /// <summary>
    /// Author
    /// </summary>
    string Author { get; }
    /// <summary>
    /// Version
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Major RT# version which this plugin is compatible with
    /// </summary>
    public int CompatibleMajorVersion { get; }

    /// <summary>
    /// Unload plugin
    /// </summary>
    Task Unload();

    /// <summary>
    /// Field for accessing custom functions specific for plugin type
    /// </summary>
    /// <remarks>Used to access data from plugin to plugin. See target plugin documentation for input and output formats</remarks>
    /// <param name="In">Anything and everything</param>
    /// <returns>Anything and everything</returns>
    Task<dynamic> CustomAccess(dynamic In) => throw new NotImplementedException();

    /// <summary>
    /// Form for plugin settings
    /// </summary>
    /// <param name="Self">Plugin self</param>
    /// <returns>Settings form</returns>
    Task ShowPluginSettings(object ParentWindow) => throw new NotImplementedException();

    /// <summary>
    /// A GUID that is unique to a plugin, but not an instance of plugin.
    /// </summary>
    Guid GUID { get; }

    /// <summary>
    /// Required non-custom plugin config part
    /// </summary>
    public PluginInstanceConfig PluginInstanceConfig { get; }

    public PluginCapabilities Capabilities { get; }
}
