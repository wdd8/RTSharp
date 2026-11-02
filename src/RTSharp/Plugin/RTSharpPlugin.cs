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
    internal IPlugin Instance { get; set; } = null!; // See Init
    internal IPluginInit? PluginInit { get; set; }
    public IPluginConfigContainer<PluginInstanceConfig> Config { get; private set; }

    public string ModulePath { get; init; }

    public Guid InstanceId { get; init; }

    internal IConfigurationRoot PluginConfig { get; }
    private Type ConfigType { get; }
    public string PluginConfigPath { get; }

    public Shared.Abstractions.Version HostVersion => Global.Consts.Version;

    public string PluginVersion {
        get {
            var info = Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!String.IsNullOrEmpty(info)) {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return Assembly?.GetName().Version?.ToString() ?? "?";
        }
    }

    public Window MainWindow => App.MainWindow;

    public void ShowTrayMessage(string Title, string Message)
    {
        throw new NotImplementedException();
    }

    public RTSharpPlugin(PluginAssemblyLoadContext Context, Assembly? Assembly, IConfigurationRoot PluginConfig, string PluginConfigPath, Guid InstanceId, string ModulePath, Type ConfigType)
    {
        this.Assembly = Assembly;
        this.AssemblyLoadContext = Context;
        this.PluginConfig = PluginConfig;
        this.PluginConfigPath = PluginConfigPath;
        this.ConfigType = ConfigType;
        this.Config = (IPluginConfigContainer<PluginInstanceConfig>)PluginConfig.Get(ConfigType)!;

        this.InstanceId = InstanceId;

        if (!Path.IsPathRooted(ModulePath))
            throw new InvalidOperationException($"{nameof(ModulePath)} must be rooted");

        this.ModulePath = ModulePath;
    }

    public IDataProviderHost<TConfig> RegisterDataProvider<TConfig>(IDataProvider In) where TConfig : IPluginConfigContainer<DataProviderInstanceConfig>
    {
        var provider = new RTSharpDataProvider<TConfig>(this, In);

        if (provider.Config.Plugin.ServerId == null)
            throw new InvalidOperationException($"{InstanceId} tried to register, but {nameof(provider.Config.Plugin.ServerId)} is missing");

        Dispatcher.UIThread.Invoke(() => Plugin.Plugins.DataProviders.Edit(x => {
            x.Add(provider);
        }));

        return provider;
    }

    public void UnregisterDataProvider(RTSharpDataProvider In)
    {
        Dispatcher.UIThread.Invoke(() => Plugin.Plugins.DataProviders.Edit(x => {
            x.Remove(In);
        }));
        In.CurrentTorrentChangesTaskCts.Cancel();
    }

    public IDisposable RegisterActionQueue(IActionQueueRenderer In) => Core.ActionQueue.RegisterActionQueue(this, In);

    private static System.Collections.IList MenuChildren(System.Collections.IList Items, string[]? Parent)
    {
        foreach (var name in Parent ?? []) {
            var item = Items.OfType<MenuItem>().FirstOrDefault(x => x.Name == name) ?? throw new InvalidOperationException($"Menu item {name} was not found");
            Items = item.ItemsSource as System.Collections.IList ?? item.Items;
        }

        return Items;
    }

    public IDisposable RegisterRootMenuItem(MenuItem In, string[]? Parent = null)
    {
        Dispatcher.UIThread.VerifyAccess();

        var items = MenuChildren(App.MainWindowViewModel.MenuItems, Parent);
        items.Add(In);
        return Disposable.Create(() => {
            items.Remove(In);
        });
    }

    public IDisposable RegisterToolsMenuItem(MenuItem In) => RegisterRootMenuItem(In, ["ToolsMenu"]);

    public IDisposable RegisterTorrentContextMenuItem(Func<MenuItem> In, string[]? Parent = null)
    {
        Dispatcher.UIThread.VerifyAccess();

        MenuItem fx(System.Collections.IList items)
        {
            var control = In();
            Debug.Assert(control.GetVisualParent() == null);
            MenuChildren(items, Parent).Add(control);
            return control;
        }
        RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Add(fx);
        return Disposable.Create(() => {
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Remove(fx);
        });
    }

    public IDisposable RegisterTorrentContextMenuItem(Func<System.Collections.IList, MenuItem> Add, Action<System.Collections.IList> Remove, string[]? Parent = null)
    {
        Dispatcher.UIThread.VerifyAccess();

        MenuItem add(System.Collections.IList items) => Add(MenuChildren(items, Parent));
        void remove(System.Collections.IList items) => Remove(MenuChildren(items, Parent));

        RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Add(add);
        return Disposable.Create(() => {
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemInserts.Remove(add);
            RTSharp.Views.TorrentListing.TorrentListingView.MenuItemRemoves.Remove(remove);
        });
    }

    public IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx) => Plugin.Plugins.Hook(Plugin.Plugins.HookType.TorrentListing_EvLoadingRow, fx);
    public IDisposable HookTorrentListingEvLoadingCell(string ColumnKey, Action<object, DataGridCell> fx) => Plugin.Plugins.HookLoadingCell(ColumnKey, fx);

    /// <summary>
    /// ViewModels.AddTorrentViewModel
    /// </summary>
    /// <param name="fx"></param>
    /// <returns></returns>
    public IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx) => Plugin.Plugins.Hook(Plugin.Plugins.HookType.AddTorrent_EvDragDrop, fx);

    public ILogger Logger =>
        Log.Logger
            .ForContext("PluginInstanceId", InstanceId)
            .ForContext("PluginGuid", Instance?.GUID.ToString() ?? "?")
            .ForContext("PluginDisplayName", Config.Plugin.Name);

    public IReadOnlyCollection<Torrent> Torrents => [.. Core.TorrentPolling.TorrentPolling.Torrents.GetSnapshot().Select(x => x.ToPluginModel())];

    public IReadOnlyCollection<IPluginHost> Plugins => [.. Plugin.Plugins.LoadedPlugins.ToArray() ];

    public HttpClient HttpClient => Core.Http.Client;

    internal async Task Unload()
    {
        if (Instance != null)
            await Instance.Unload();
        AssemblyLoadContext!.Unload();
        Assembly = null;
        Instance = null!;
        AssemblyLoadContext = null!;
        Plugin.Plugins.LoadedPlugins.Remove(this);
    }

    public async Task<(IDataProvider Provider, string RemotePath)?> SelectDuplicationTarget(Window Owner, Torrent Source, Func<IDataProvider, bool> Filter)
    {
        var target = await Core.TorrentDuplication.SelectTarget(Owner, Source, Filter);

        return target == null ? null : (target.Provider, target.RemoteTargetPath);
    }

    public Task<byte[]> DuplicateTorrentAs(IReadOnlyList<Torrent> Sources, IDataProvider Target, string RemotePath, byte[] TargetTorrent, LinkOptions Links) =>
        Core.TorrentDuplication.DuplicateAs(Sources, new Core.TorrentDuplication.DuplicationTarget(Target, RemotePath), TargetTorrent, Links);

    public IServiceScope CreateScope() => Core.ServiceProvider.CreateScope();

    public IReadOnlyList<IDaemonService> GetDaemonServices() => Core.Servers.All.Select(x => x.Value).ToList().AsReadOnly();

    public IDaemonService? GetDaemonService(string ServerId)
    {
        if (Core.Servers.All.TryGetValue(ServerId, out var ret))
            return ret;

        return null;
    }

    public async Task SavePluginConfig(Action<JsonNode> Modifications)
    {
        var jsonRaw = await System.IO.File.ReadAllTextAsync(PluginConfigPath);
        var json = JsonNode.Parse(jsonRaw, documentOptions: new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        })!;

        Modifications(json);

        await WriteConfig(json.ToJsonString(new JsonSerializerOptions() { WriteIndented = true }));
    }

    public async Task SavePluginConfig<T>(Action<T> Modifications)
    {
        var jsonRaw = await System.IO.File.ReadAllTextAsync(PluginConfigPath);
        var config = JsonSerializer.Deserialize<T>(jsonRaw, new JsonSerializerOptions {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        })!;

        Modifications(config);

        await WriteConfig(JsonSerializer.Serialize(config, new JsonSerializerOptions() { WriteIndented = true }));
    }

    private async Task WriteConfig(string content)
    {
        var newFileName = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(newFileName, content);

        System.IO.File.Move(PluginConfigPath, PluginConfigPath + ".bak");
        System.IO.File.Move(newFileName, PluginConfigPath);
        System.IO.File.Delete(PluginConfigPath + ".bak");

        PluginConfig.Reload();
        this.Config = (IPluginConfigContainer<PluginInstanceConfig>)PluginConfig.Get(ConfigType)!;
    }
}
