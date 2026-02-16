#nullable enable

using Avalonia.Controls;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System.Text.Json.Nodes;

namespace RTSharp.Shared.Abstractions.Client;

public interface IPluginHost
{
    /// <summary>
    /// Plugin instance configuration path
    /// </summary>
    string PluginConfigPath { get; }

    /// <summary>
    /// Plugin instance configuration. This property is read only. To make modifications, see <see cref="SavePluginConfig(Action{JsonNode})"/>
    /// </summary>
    IConfigurationRoot PluginConfig { get; }

    /// <summary>
    /// Required RT# plugin instance config part from <see cref="PluginConfig" />
    /// </summary>
    PluginInstanceConfig PluginInstanceConfig { get; }

    /// <summary>
    /// RT# (Core) version
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Avalonia.Controls.Window object of RT# main window
    /// </summary>
    /// <remarks>Can be used to make direct modifications to core but will break on any core version update</remarks>
    object MainWindow { get; }

    /// <summary>
    /// Module path as in specified in config + manifest
    /// </summary>
    string ModulePath { get; }

    /// <summary>
    /// Full module path
    /// </summary>
    string FullModulePath { get; }

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
    /// Registers a data provider
    /// </summary>
    /// <param name="In">Data provider</param>
    /// <returns>Handle</returns>
    IDataProviderHost RegisterDataProvider(IDataProvider In);

    /// <summary>
    /// Unregisters a data provider
    /// </summary>
    /// <param name="In">Handle</param>
    void UnregisterDataProvider(object In);

    /// <summary>
    /// Registers the action queue
    /// </summary>
    /// <param name="In">The action queue</param>
    void RegisterActionQueue(IActionQueueRenderer In);

    /// <summary>
    /// Unregisters the action queue
    /// </summary>
    void UnregisterActionQueue();

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
    /// Hooks TorrentListing EvRowPrepared event, which then allows to modify appearance or behavior of a row
    /// </summary>
    /// <param name="fx">Hook function</param>
    /// <returns>Unhook</returns>
    IDisposable HookTorrentListingEvRowPrepared(Action<object, TreeDataGridRowEventArgs> fx);

    /// <summary>
    /// Hooks TorrentListing EvRowPrepared event, which then allows to modify appearance or behavior of a cell
    /// </summary>
    /// <param name="fx">Hook function</param>
    /// <returns>Unhook</returns>
    IDisposable HookTorrentListingEvCellPrepared(Action<object, TreeDataGridCellEventArgs> fx);

    /// <summary>
    /// Hooks AddTorrent EvDragDrop event, which allows to modify AddTorrent window ViewModel
    /// </summary>
    /// <param name="fx">AddTorrentViewModel</param>
    /// <returns>Unhook</returns>
    IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx);

    IReadOnlyCollection<Torrent> Torrents { get; }

    /// <summary>
    /// Plugin logger
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Plugin HTTP client
    /// </summary>
    HttpClient HttpClient { get; }

    IDaemonService? GetDaemonService(string serverId);

    IDaemonService? AttachedDaemonService { get; }

    IServiceScope CreateScope();

    IReadOnlyList<IDaemonService> GetDaemonServices();

    /// <summary>
    /// Override current plugin configuration to disk. See <see cref="PluginConfig" />
    /// </summary>
    /// <param name="Modifications"><see cref="JsonNode" /> of the configuration that may be modified by the caller.</param>
    /// <remarks><see cref="PluginConfig" /> is read only. Changes in <see cref="PluginConfig" /> do not persist.</remarks>
    /// <returns></returns>
    Task SavePluginConfig(Action<JsonNode> Modifications);
}
