using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System.Text.Json.Nodes;

namespace RTSharp.Shared.Abstractions.Client;

/// <summary>
/// Handle to the host application through a plugin
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Plugin instance configuration path
    /// </summary>
    string PluginConfigPath { get; }

    /// <summary>
    /// Plugin instance configuration
    /// </summary>
    IPluginConfigContainer<PluginInstanceConfig> Config { get; }

    /// <summary>
    /// RT# (Core) version
    /// </summary>
    Version HostVersion { get; }

    /// <summary>
    /// RT# main window
    /// </summary>
    /// <remarks>Can be used to make direct modifications to core but will break on any core version update</remarks>
    Window MainWindow { get; }

    /// <summary>
    /// Full module path as resolved from config + manifest
    /// </summary>
    string ModulePath { get; }

    /// <summary>
    /// Instance ID
    /// </summary>
    Guid InstanceId { get; }

    /// <summary>
    /// Shows tray notification / message
    /// </summary>
    /// <param name="Title">Message title</param>
    /// <param name="Message">Message text</param>
    /// <param name="Icon">Message icon</param>
    void ShowTrayMessage(string Title, string Message);

    /// <summary>
    /// Registers the action queue
    /// </summary>
    /// <param name="In">The action queue</param>
    /// <returns>Unregister</returns>
    IDisposable RegisterActionQueue(IActionQueueRenderer In);

    /// <summary>
    /// Registers a root menu item
    /// </summary>
    /// <param name="In">Menu item</param>
    /// <returns>Unregister</returns>
    IDisposable RegisterRootMenuItem(MenuItem In);

    /// <summary>
    /// Registers a menu item in tools menu
    /// </summary>
    /// <param name="In">Menu item</param>
    /// <returns>Unregister</returns>
    IDisposable RegisterToolsMenuItem(MenuItem In);

    /// <summary>
    /// Registers a menu item in torrent context menu
    /// </summary>
    /// <param name="In">Menu item creation</param>
    /// <returns>Unregister</returns>
    IDisposable RegisterTorrentContextMenuItem(Func<MenuItem> In);

    /// <summary>
    /// Registers a menu item action to torrent context menu
    /// </summary>
    /// <param name="Add">Menu item creation</param>
    /// <param name="Remove">Menu item removal</param>
    /// <returns>Unregister</returns>
    IDisposable RegisterTorrentContextMenuItem(Func<System.Collections.IList, MenuItem> Add, Action<System.Collections.IList> Remove);

    /// <summary>
    /// Hooks TorrentListing EvLoadingRow event, which then allows to modify appearance or behavior of a row
    /// </summary>
    /// <param name="fx">Hook function</param>
    /// <returns>Unhook</returns>
    IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx);

    /// <summary>
    /// Hooks TorrentListing EvLoadingCell event, which then allows to modify appearance or behavior of a cell. First argument is <c>RTSharp.Models.Torrent</c>, second is <c>ColumnKey</c>.
    /// </summary>
    /// <param name="fx">Hook function</param>
    /// <returns>Unhook</returns>
    IDisposable HookTorrentListingEvLoadingCell(Action<object, string, DataGridCell> fx);

    /// <summary>
    /// Hooks AddTorrent EvDragDrop event, which allows to modify AddTorrent window ViewModel
    /// </summary>
    /// <param name="fx">AddTorrentViewModel</param>
    /// <returns>Unhook</returns>
    IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx);

    IReadOnlyCollection<Torrent> Torrents { get; }

    IReadOnlyCollection<IPluginHost> Plugins { get; }

    /// <summary>
    /// Plugin logger
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Plugin HTTP client
    /// </summary>
    HttpClient HttpClient { get; }

    IDaemonService? GetDaemonService(string serverId);

    IServiceScope CreateScope();

    IReadOnlyList<IDaemonService> GetDaemonServices();

    /// <summary>
    /// Registers a torrent data provider.
    /// For a plugin built on <see cref="BasePlugin{TConfig}"/>,
    /// <typeparam name="TConfigDp" /> is the same as <c>TConfig</c>
    /// </summary>
    /// <typeparam name="TConfigDp">Provider-specific config type</typeparam>
    /// <param name="In">Data provider implementation</param>
    /// <returns>Handle to host application functions supporting data providers</returns>
    IDataProviderHost<TConfigDp> RegisterDataProvider<TConfigDp>(IDataProvider In) where TConfigDp : IPluginConfigContainer<DataProviderInstanceConfig>;

    /// <summary>
    /// Override current plugin configuration to disk
    /// </summary>
    /// <param name="Modifications"><see cref="JsonNode" /> of the configuration that may be modified by the caller</param>
    /// <returns></returns>
    Task SavePluginConfig(Action<JsonNode> Modifications);

    /// <summary>
    /// Override current plugin configuration to disk
    /// </summary>
    /// <param name="Modifications">Callback to modify the configuration</param>
    /// <returns></returns>
    Task SavePluginConfig<T>(Action<T> Modifications);
}


/// <summary>
/// <see cref="IPluginHost" /> with some more config on top
/// </summary>
public interface IPluginHost<TConfig> : IPluginHost
    where TConfig : IPluginConfigContainer<PluginInstanceConfig>
{
    /// <summary>
    /// Plugin instance configuration: the bound <c>Plugin</c> section (<c>Config.Plugin</c>) plus any
    /// plugin-specific settings
    /// </summary>
    new TConfig Config { get; }

    /// <summary>
    /// Override current plugin configuration to disk
    /// </summary>
    /// <param name="Modifications">Callback to modify the configuration</param>
    /// <returns></returns>
    Task SavePluginConfig(Action<TConfig> Modifications);
}
