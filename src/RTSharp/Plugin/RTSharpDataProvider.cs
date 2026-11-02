using Avalonia.Controls;

using Grpc.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Plugin;

public abstract class RTSharpDataProvider : IDataProviderHost
{
    public RTSharpPlugin Host { get; }

    public IDataProvider Instance { get; }

    public IPlugin Plugin => Host.Instance;

    public Task? CurrentTorrentChangesTask { get; set; }

    public CancellationTokenSource CurrentTorrentChangesTaskCts { get; set; } = new();

    public abstract IPluginConfigContainer<DataProviderInstanceConfig> Config { get; }

    public Notifyable<DataProviderState> State { get; } = new();

    protected RTSharpDataProvider(RTSharpPlugin PluginInstance, IDataProvider DataProvider)
    {
        this.Host = PluginInstance;
        this.Instance = DataProvider;
    }

    public override bool Equals(object? obj) => obj is RTSharpDataProvider dp && Host.InstanceId == dp.Host.InstanceId;

    public override int GetHashCode() => Host.InstanceId.GetHashCode();

    public static bool operator ==(RTSharpDataProvider? dp1, RTSharpDataProvider? dp2)
    {
        if (dp1 is null) {
            if (dp2 is null)
                return true;

            return false;
        }
        if (dp2 is null) {
            if (dp1 is null)
                return true;

            return false;
        }

        return dp1.Equals(dp2);
    }

    public static bool operator !=(RTSharpDataProvider? dp1, RTSharpDataProvider? dp2) => !(dp1 == dp2);

    public override string ToString() => $"{Host.Config.Plugin.Name} ({Config.Plugin.ServerId})";

    private readonly HashSet<Guid> SupportedBuiltInDataProviders = new() {
        new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C"),
        new Guid("E347109B-A06D-4894-9E3A-6FFF63411370"),
        new Guid("0671CE03-09F4-4F07-9402-548CF2E201B1")
    };

    public Metadata GetBuiltInDataProviderGrpcHeaders()
    {
        if (!SupportedBuiltInDataProviders.Contains(Host.Instance.GUID)) {
            throw new InvalidOperationException("Not a built-in data provider");
        }

        return [
            new Metadata.Entry("data-provider", Config.Plugin.ProviderName)
        ];
    }

    public IDaemonService AttachedDaemonService {
        get {
            var serverId = Config.Plugin.ServerId;
            if (serverId == null)
                throw new InvalidOperationException("No singular server data providers registered");

            return Core.Servers.All[serverId];
        }
    }

    // IPluginHost<TConfig> proxy ___

    public string PluginConfigPath => Host.PluginConfigPath;
    public Shared.Abstractions.Version HostVersion => Host.HostVersion;
    public Window MainWindow => Host.MainWindow;
    public string ModulePath => Host.ModulePath;
    public Guid InstanceId => Host.InstanceId;
    public void ShowTrayMessage(string Title, string Message) => Host.ShowTrayMessage(Title, Message);
    public IDisposable RegisterActionQueue(IActionQueueRenderer In) => Host.RegisterActionQueue(In);
    public IDisposable RegisterRootMenuItem(MenuItem In, string[]? Parent = null) => Host.RegisterRootMenuItem(In, Parent);
    public IDisposable RegisterToolsMenuItem(MenuItem In) => Host.RegisterToolsMenuItem(In);
    public IDisposable RegisterTorrentContextMenuItem(Func<MenuItem> In, string[]? Parent = null) => Host.RegisterTorrentContextMenuItem(In, Parent);
    public IDisposable RegisterTorrentContextMenuItem(Func<System.Collections.IList, MenuItem> Add, Action<System.Collections.IList> Remove, string[]? Parent = null) => Host.RegisterTorrentContextMenuItem(Add, Remove, Parent);
    public IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx) => Host.HookTorrentListingEvLoadingRow(fx);
    public IDisposable HookTorrentListingEvLoadingCell(string ColumnKey, Action<object, DataGridCell> fx) => Host.HookTorrentListingEvLoadingCell(ColumnKey, fx);
    public IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx) => Host.HookAddTorrentEvDragDrop(fx);
    public IReadOnlyCollection<Torrent> Torrents => Host.Torrents;
    public IReadOnlyCollection<IPluginHost> Plugins => Host.Plugins;
    public ILogger Logger => Host.Logger;
    public HttpClient HttpClient => Host.HttpClient;
    public IDaemonService? GetDaemonService(string serverId) => Host.GetDaemonService(serverId);
    public Task<(IDataProvider Provider, string RemotePath)?> SelectDuplicationTarget(Window Owner, Torrent Source, Func<IDataProvider, bool> Filter) => Host.SelectDuplicationTarget(Owner, Source, Filter);
    public Task<byte[]> DuplicateTorrentAs(IReadOnlyList<Torrent> Sources, IDataProvider Target, string RemotePath, byte[] TargetTorrent, LinkOptions Links) => Host.DuplicateTorrentAs(Sources, Target, RemotePath, TargetTorrent, Links);
    public IServiceScope CreateScope() => Host.CreateScope();
    public IReadOnlyList<IDaemonService> GetDaemonServices() => Host.GetDaemonServices();
    public IDataProviderHost<TConfigDp> RegisterDataProvider<TConfigDp>(IDataProvider In) where TConfigDp : IPluginConfigContainer<DataProviderInstanceConfig> => Host.RegisterDataProvider<TConfigDp>(In);
    public Task SavePluginConfig(Action<JsonNode> Modifications) => Host.SavePluginConfig(Modifications);
    public Task SavePluginConfig<T>(Action<T> Modifications) => Host.SavePluginConfig(Modifications);
}

internal sealed class RTSharpDataProvider<TConfig> : RTSharpDataProvider, IDataProviderHost<TConfig>
    where TConfig : IPluginConfigContainer<DataProviderInstanceConfig>
{
    private TConfig _config;

    public override IPluginConfigContainer<DataProviderInstanceConfig> Config => _config;
    TConfig IDataProviderHost<TConfig>.Config => _config;
    TConfig IPluginHost<TConfig>.Config => _config;
    IPluginConfigContainer<PluginInstanceConfig> IPluginHost.Config => _config;

    public RTSharpDataProvider(RTSharpPlugin PluginInstance, IDataProvider DataProvider)
        : base(PluginInstance, DataProvider)
    {
        _config = PluginInstance.PluginConfig.Get<TConfig>() ?? throw new NullReferenceException($"{nameof(Config)} must not be null");
    }

    public async Task SavePluginConfig(Action<TConfig> Modifications)
    {
        await Host.SavePluginConfig(Modifications);
        _config = Host.PluginConfig.Get<TConfig>() ?? throw new NullReferenceException($"{nameof(Config)} must not be null");
    }

    public void Dispose()
    {
        Host.UnregisterDataProvider(this);
    }
}
