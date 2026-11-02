using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;

using Serilog;

using System.Text.Json.Nodes;

namespace RTSharp.Shared.Abstractions.Client;

/// <summary>
/// <see cref="IPluginHost"/> proxy with a config type
/// </summary>
internal sealed class PluginHostProxy<TConfig>(IPluginHost Inner) : IPluginHost<TConfig>
    where TConfig : IPluginConfigContainer<PluginInstanceConfig>
{
    public TConfig Config => (TConfig)Inner.Config;
    IPluginConfigContainer<PluginInstanceConfig> IPluginHost.Config => Inner.Config;

    public string PluginConfigPath => Inner.PluginConfigPath;
    public Version HostVersion => Inner.HostVersion;
    public Window MainWindow => Inner.MainWindow;
    public string ModulePath => Inner.ModulePath;
    public Guid InstanceId => Inner.InstanceId;

    public void ShowTrayMessage(string Title, string Message) => Inner.ShowTrayMessage(Title, Message);

    public IDisposable RegisterActionQueue(IActionQueueRenderer In) => Inner.RegisterActionQueue(In);
    public IDisposable RegisterRootMenuItem(MenuItem In, string[]? Parent = null) => Inner.RegisterRootMenuItem(In, Parent);
    public IDisposable RegisterToolsMenuItem(MenuItem In) => Inner.RegisterToolsMenuItem(In);
    public IDisposable RegisterTorrentContextMenuItem(Func<MenuItem> In, string[]? Parent = null) => Inner.RegisterTorrentContextMenuItem(In, Parent);
    public IDisposable RegisterTorrentContextMenuItem(Func<System.Collections.IList, MenuItem> Add, Action<System.Collections.IList> Remove, string[]? Parent = null) => Inner.RegisterTorrentContextMenuItem(Add, Remove, Parent);

    public IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx) => Inner.HookTorrentListingEvLoadingRow(fx);
    public IDisposable HookTorrentListingEvLoadingCell(string ColumnKey, Action<object, DataGridCell> fx) => Inner.HookTorrentListingEvLoadingCell(ColumnKey, fx);
    public IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx) => Inner.HookAddTorrentEvDragDrop(fx);

    public IReadOnlyCollection<Torrent> Torrents => Inner.Torrents;
    public IReadOnlyCollection<IPluginHost> Plugins => Inner.Plugins;

    public ILogger Logger => Inner.Logger;
    public HttpClient HttpClient => Inner.HttpClient;

    public IDaemonService? GetDaemonService(string serverId) => Inner.GetDaemonService(serverId);
    public Task<(IDataProvider Provider, string RemotePath)?> SelectDuplicationTarget(Window Owner, Torrent Source, Func<IDataProvider, bool> Filter) => Inner.SelectDuplicationTarget(Owner, Source, Filter);
    public Task<byte[]> DuplicateTorrentAs(IReadOnlyList<Torrent> Sources, IDataProvider Target, string RemotePath, byte[] TargetTorrent, LinkOptions Links) => Inner.DuplicateTorrentAs(Sources, Target, RemotePath, TargetTorrent, Links);
    public IServiceScope CreateScope() => Inner.CreateScope();
    public IReadOnlyList<IDaemonService> GetDaemonServices() => Inner.GetDaemonServices();

    public IDataProviderHost<TConfigDp> RegisterDataProvider<TConfigDp>(IDataProvider In) where TConfigDp : IPluginConfigContainer<DataProviderInstanceConfig> => Inner.RegisterDataProvider<TConfigDp>(In);

    public Task SavePluginConfig(Action<JsonNode> Modifications) => Inner.SavePluginConfig(Modifications);
    public Task SavePluginConfig<T>(Action<T> Modifications) => Inner.SavePluginConfig(Modifications);
    public Task SavePluginConfig(Action<TConfig> Modifications) => Inner.SavePluginConfig(Modifications);
}
