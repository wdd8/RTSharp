using Avalonia.Controls;
using Avalonia.VisualTree;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using Serilog;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RTSharp.Plugin;

public class RTSharpPlugin : IPluginHost
{
    internal PluginAssemblyLoadContext AssemblyLoadContext { get; private set; }
    internal Assembly? Assembly { get; private set; }
    internal IPlugin Instance { get; set; }
    internal IPluginInit? PluginInit { get; set; }
    public PluginInstanceConfig PluginInstanceConfig { get; }

    public string ModulePath { get; init; }

    public Guid InstanceId { get; init; }

    public IConfigurationRoot PluginConfig { get; }
    public string PluginConfigPath { get; }

    private string? AttachedServerId { get; set; }

    /// <inheritdoc />
    public Shared.Abstractions.Version Version => Global.Consts.Version;

    /// <inheritdoc />
    public object MainWindow => App.MainWindow;

    /// <inheritdoc />
    public void ShowTrayMessage(string Title, string Message)
    {
        throw new NotImplementedException();
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public RTSharpPlugin(PluginAssemblyLoadContext Context, Assembly Assembly, IConfigurationRoot PluginConfig, string PluginConfigPath, Guid InstanceId, string ModulePath)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
        this.Assembly = Assembly;
        this.AssemblyLoadContext = Context;
        this.PluginConfig = PluginConfig;
        this.PluginConfigPath = PluginConfigPath;
        this.PluginInstanceConfig = PluginConfig.GetSection("Plugin").Get<PluginInstanceConfig>()!;

        this.InstanceId = InstanceId;

        if (!Path.IsPathRooted(ModulePath))
            throw new InvalidOperationException($"{nameof(ModulePath)} must be rooted");

        this.ModulePath = ModulePath;
    }

    private static object DataProviderLock = new();

    public IDataProviderHost RegisterDataProvider(IDataProvider In)
    {
        var provider = new RTSharpDataProvider(this, In);

        if (provider.DataProviderInstanceConfig == null)
            throw new InvalidOperationException($"{InstanceId} tried to register, but data provider section is not present");

        if (provider.DataProviderInstanceConfig.ServerId == null)
            throw new InvalidOperationException($"{InstanceId} tried to register, but {nameof(provider.DataProviderInstanceConfig.ServerId)} is missing");

        if (AttachedServerId == null)
            AttachedServerId = provider.DataProviderInstanceConfig.ServerId;
        else if (AttachedServerId != provider.DataProviderInstanceConfig.ServerId)
            AttachedServerId = null;

        lock (DataProviderLock) {
            Plugins.DataProviders.Edit(x => {
                x.Add(provider);
            });
        }

        return provider;
    }

    public void UnregisterDataProvider(object In)
    {
        var dp = (RTSharpDataProvider)In;

        if (dp == null)
            return;

        lock (DataProviderLock) {
            Plugins.DataProviders.Edit(x => {
                x.Remove(dp);
            });
        }
        dp.CurrentTorrentChangesTaskCts.Cancel();
    }

    public void RegisterActionQueue(IActionQueueRenderer In) => Core.ActionQueue.RegisterActionQueue(this, In);

    public void UnregisterActionQueue() => Core.ActionQueue.UnregisterActionQueue(this);

    public IDisposable RegisterRootMenuItem(MenuItem In)
    {
        Dispatcher.UIThread.VerifyAccess();

        App.MainWindowViewModel.MenuItems.Add(In);
        return Disposable.Create(() => {
            App.MainWindowViewModel.MenuItems.Remove(In);
        });
    }

    public IDisposable RegisterToolsMenuItem(MenuItem In)
    {
        Dispatcher.UIThread.VerifyAccess();

        var tools = App.MainWindowViewModel.MenuItems.First(x => x.Name == "ToolsMenu");
        tools.Items.Add(In);
        return Disposable.Create(() => {
            tools.Items.Remove(In);
        });
    }

    public IDisposable RegisterTorrentContextMenuItem(Func<MenuItem> In)
    {
        Dispatcher.UIThread.VerifyAccess();

        MenuItem fx(System.Collections.IList items)
        {
            var control = In();
            Debug.Assert(control.GetVisualParent() == null);
            items.Add(control);
            return control;
        }
        RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Add(fx);
        return Disposable.Create(() => {
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Remove(fx);
        });
    }

    public IDisposable RegisterTorrentContextMenuItem(Func<System.Collections.IList, MenuItem> Add, Action<System.Collections.IList> Remove)
    {
        Dispatcher.UIThread.VerifyAccess();

        RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Add(Add);
        return Disposable.Create(() => {
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Remove(Add);
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemRemoves.Remove(Remove);
        });
    }

    public IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx) => Plugins.Hook(Plugins.HookType.TorrentListing_EvLoadingRow, fx);
    public IDisposable HookTorrentListingEvLoadingCell(Action<object, string, DataGridCell> fx) => Plugins.Hook(Plugins.HookType.TorrentListing_EvLoadingCell, fx);

    /// <summary>
    /// ViewModels.AddTorrentViewModel
    /// </summary>
    /// <param name="fx"></param>
    /// <returns></returns>
    public IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx) => Plugins.Hook(Plugins.HookType.AddTorrent_EvDragDrop, fx);

    public ILogger Logger =>
        Log.Logger
            .ForContext("PluginInstanceId", InstanceId)
            .ForContext("PluginGuid", Instance?.GUID.ToString() ?? "?")
            .ForContext("PluginColor", PluginInstanceConfig.Color)
            .ForContext("PluginDisplayName", PluginInstanceConfig.Name);

    public IReadOnlyCollection<Torrent> Torrents => [.. Core.TorrentPolling.TorrentPolling.Torrents.GetSnapshot().Select(x => x.ToPluginModel())];

    public HttpClient HttpClient => Core.Http.Client;

    internal async Task Unload()
    {
        if (Instance != null)
            await Instance.Unload();
        AssemblyLoadContext!.Unload();
        Assembly = null;
        Instance = null!;
        AssemblyLoadContext = null!;
        Plugins.LoadedPlugins.Remove(this);
    }

    public IDaemonService AttachedDaemonService
    {
        get {
            if (AttachedServerId == null)
                throw new InvalidOperationException("No singular server data providers registered");

            return Core.Servers.Value[AttachedServerId];
        }
    }

    public IServiceScope CreateScope() => Core.ServiceProvider.CreateScope();

    public IReadOnlyList<IDaemonService> GetDaemonServices() => Core.Servers.Value.Select(x => x.Value).ToList().AsReadOnly();

    public IDaemonService? GetDaemonService(string ServerId)
    {
        if (Core.Servers.Value.TryGetValue(ServerId, out var ret))
            return ret;

        return null;
    }

    public async Task SavePluginConfig(Action<JsonNode> Modifications)
    {
        var jsonRaw = await System.IO.File.ReadAllTextAsync(PluginConfigPath);
        var json = JsonNode.Parse(jsonRaw)!;

        Modifications(json);

        var newFileName = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(newFileName, json.ToJsonString(new JsonSerializerOptions() {
            WriteIndented = true
        }));

        System.IO.File.Move(PluginConfigPath, PluginConfigPath + ".bak");
        System.IO.File.Move(newFileName, PluginConfigPath);
        System.IO.File.Delete(PluginConfigPath + ".bak");

        PluginConfig.Reload();
    }
}
