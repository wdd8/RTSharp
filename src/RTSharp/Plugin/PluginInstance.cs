using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core.Services.Auxiliary;
using RTSharp.Core.Util;
using RTSharp.Shared.Abstractions;
using Serilog;

namespace RTSharp.Plugin
{
    public class PluginInstance : IPluginHost
    {
        internal PluginAssemblyLoadContext AssemblyLoadContext { get; private set; }
        internal Assembly Assembly { get; private set; }
        internal IPlugin Instance { get; private set; }
        public PluginInstanceConfig PluginInstanceConfig => PluginConfig.GetSection("Plugin").Get<PluginInstanceConfig>()!;

        public string ModulePath { get; init; }

        public string FullModulePath => Path.GetFullPath(Path.Combine(Consts.PLUGINS_PATH, ModulePath));

        public Guid InstanceId { get; init; }

        public IConfigurationRoot PluginConfig { get; }
        public string PluginConfigPath { get; }

        /// <inheritdoc />
        public Shared.Abstractions.Version Version => Global.Consts.Version;

        /// <inheritdoc />
        public object MainWindow => App.MainWindow;

        /// <inheritdoc />
        public void ShowTrayMessage(string Title, string Message)
        {
            throw new NotImplementedException();
        }

        public PluginInstance(PluginAssemblyLoadContext Context, Assembly Assembly, IPlugin Instance, IConfigurationRoot PluginConfig, string PluginConfigPath, Guid InstanceId, string ModulePath)
        {
            this.Assembly = Assembly;
            this.Instance = Instance;
            this.AssemblyLoadContext = Context;
            this.PluginConfig = PluginConfig;
            this.PluginConfigPath = PluginConfigPath;

            this.InstanceId = InstanceId;
            this.ModulePath = ModulePath;
        }

        public IConfigurationRoot Config { get; private set; }

        private static object DataProviderLock = new();

        public object RegisterDataProvider(IDataProvider In)
        {
            if (Instance != In.Plugin)
                throw new InvalidOperationException($"{nameof(IDataProvider.Plugin)} in data provider must be same as caller");

            if (PluginInstanceConfig.ServerId == null)
                throw new InvalidOperationException($"{InstanceId} tried to register, but {nameof(PluginInstanceConfig.ServerId)} is missing");

            var provider = new DataProvider(this, In);
            lock (DataProviderLock) {
                Plugins.DataProviders.Add(provider);
            }

            return provider;
        }

        public void UnregisterDataProvider(object In)
        {
            lock (DataProviderLock) {
                Plugins.DataProviders.Remove((DataProvider)In);
            }
        }

        public void RegisterActionQueue(IActionQueue In) => Core.ActionQueue.RegisterActionQueue(this, In);

        public void UnregisterActionQueue() => Core.ActionQueue.UnregisterActionQueue(this);

        public IDisposable RegisterRootMenuItem(MenuItem In)
        {
            App.MainWindowViewModel.MenuItems.Add(In);
            return Disposable.Create(() => {
                App.MainWindowViewModel.MenuItems.Remove(In);
            });
        }

        public IDisposable HookTorrentListingEvLoadingRow(Action<object, DataGridRowEventArgs> fx) => Plugins.Hook(Plugins.HookType.TorrentListing_EvLoadingRow, fx);

        /// <summary>
        /// ViewModels.AddTorrentViewModel
        /// </summary>
        /// <param name="fx"></param>
        /// <returns></returns>
        public IDisposable HookAddTorrentEvDragDrop(Func<object, ValueTask> fx) => Plugins.Hook(Plugins.HookType.AddTorrent_EvDragDrop, fx);

        public ILogger Logger =>
            Log.Logger
                .ForContext("PluginInstanceId", InstanceId)
                .ForContext("PluginGuid", Instance.GUID)
                .ForContext("PluginColor", PluginInstanceConfig.Color)
                .ForContext("PluginDisplayName", PluginInstanceConfig.Name);

        public IReadOnlyCollection<Torrent> Torrents => Core.TorrentPolling.TorrentPolling.Torrents.Select(x => x.ToPluginModel()).ToArray();

        public HttpClient HttpClient => Core.Http.Client;

        internal async Task Unload()
        {
            await Instance.Unload();
            AssemblyLoadContext.Unload();
            Assembly = null;
            Instance = null;
            AssemblyLoadContext = null;
            Plugins.LoadedPlugins.Remove(this);
        }

        public IAuxiliaryService GetAuxiliaryService(string serverId)
        {
            return new AuxiliaryService(serverId);
        }

        public IServiceScope CreateScope() => Core.ServiceProvider.CreateScope();
    }
}
