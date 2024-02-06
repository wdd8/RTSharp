using Avalonia.Controls;

using Microsoft.Extensions.Configuration;
using Serilog;

namespace RTSharp.Shared.Abstractions
{
    public interface IPluginHost
    {
        /// <summary>
        /// Plugin instance configuration path
        /// </summary>
        string PluginConfigPath { get; }

        /// <summary>
        /// Plugin instance configuration
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
        object RegisterDataProvider(IDataProvider In);

        /// <summary>
        /// Unregisters a data provider
        /// </summary>
        /// <param name="In">Handle</param>
        void UnregisterDataProvider(object In);

        /// <summary>
        /// Registers the action queue
        /// </summary>
        /// <param name="In">The action queue</param>
        void RegisterActionQueue(IActionQueue In);

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
        /// Hooks TorrentListing EvLoadingRow event, which then allows to modify appearance or behavior of row or its cells
        /// </summary>
        /// <param name="fx">Hook function</param>
        /// <returns>Unhook</returns>
        IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx);

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

        IAuxiliaryService GetAuxiliaryService(string serverId);
    }
}
