using RTSharp.Core.Util;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using RTSharp.Shared.Abstractions;
using Serilog;
using System.Collections.Concurrent;
using System.Reactive.Disposables;
using Avalonia.Controls;
using System.Collections.Generic;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Diagnostics;
using LiveChartsCore.Measure;
using RTSharp.Shared.Controls.Views;
using RTSharp.Core;
using System.Text.RegularExpressions;

namespace RTSharp.Plugin
{
    public static class Plugins
    {
        public static ObservableCollection<PluginInstance> LoadedPlugins = new ObservableCollection<PluginInstance>();

        public static ObservableCollection<DataProvider> DataProviders = new ObservableCollection<DataProvider>();

        public enum HookType
        {
            TorrentListing_EvLoadingRow,
			AddTorrent_EvDragDrop,
        }

        private static ConcurrentDictionary<HookType, object> Hooks = new();

        static Plugins()
        {
            Hooks.TryAdd(HookType.TorrentListing_EvLoadingRow, new ThreadSafeList<Action<object, DataGridRowEventArgs>>());
			Hooks.TryAdd(HookType.AddTorrent_EvDragDrop, new ThreadSafeList<Func<object, ValueTask>>());
		}

		public static IDisposable Hook<T>(HookType type, Func<T, ValueTask> fx)
		{
			var list = (ThreadSafeList<Func<T, ValueTask>>)Hooks[type];
			list.Add(fx);

			return Disposable.Create(() => {
				list.Remove(fx);
			});
		}

		public static IDisposable Hook<T, T2>(HookType type, Action<T, T2> fx)
		{
			var list = (ThreadSafeList<Action<T, T2>>)Hooks[type];
			list.Add(fx);

			return Disposable.Create(() => {
				list.Remove(fx);
			});
		}

		public static IDisposable Hook<T>(HookType type, Action<T> fx)
        {
            var list = (ThreadSafeList<Action<T>>)Hooks[type];
			list.Add(fx);

            return Disposable.Create(() => {
                list.Remove(fx);
            });
		}

        internal static IEnumerable<Action<T>> GetHook<T>(HookType type)
        {
            return (ThreadSafeList<Action<T>>)Hooks[type];
        }

		internal static IEnumerable<Action<T, T2>> GetHook<T, T2>(HookType type)
		{
			return (ThreadSafeList<Action<T, T2>>)Hooks[type];
		}

		internal static IEnumerable<Func<T, ValueTask>> GetHookAsync<T>(HookType type)
		{
			return (ThreadSafeList<Func<T, ValueTask>>)Hooks[type];
		}

        public static string GetFirstPluginConfigOrDefault(string FullModuleContentsPath)
        {
            foreach (var json in Directory.GetFiles(Consts.PLUGINS_PATH, "*.json")) {
                try {
                    var builder = new ConfigurationBuilder();
                    builder.AddJsonFile(json, false, true);
                    var configRaw = builder.Build();
                    var config = configRaw.GetSection("Plugin").Get<PluginInstanceConfig>();
                    var moduleContentsPath = configRaw.GetSection("Plugin").GetValue<string>("Path");

                    moduleContentsPath = System.IO.Path.IsPathRooted(moduleContentsPath) ? moduleContentsPath : System.IO.Path.GetFullPath(System.IO.Path.Combine(Shared.Abstractions.Consts.PLUGINS_PATH, moduleContentsPath));

                    if (moduleContentsPath == FullModuleContentsPath)
                        return json;
                } catch {
                    continue;
                }
            }

            return default;
        }

        public static async Task<string> GeneratePluginConfig(string ModuleContentsPath)
        {
            var json = new JsonObject();
            var modName = System.IO.Path.GetFileName(ModuleContentsPath);

            json["Plugin"] = new JsonObject();
            json["Plugin"]["Path"] = ModuleContentsPath;
            json["Plugin"]["InstanceId"] = Guid.NewGuid();
            json["Plugin"]["Name"] = modName;

            var existing = Directory.GetFiles(Shared.Abstractions.Consts.PLUGINS_PATH, $"{modName}-*.json").OrderBy(file => Regex.Replace(file, @"\d+", match => match.Value.PadLeft(4, '0')));
            int next = 1;
            foreach (var file in existing) {
                if ($"{modName}-{next}.json" == file) {
                    next++;
                }
            }

            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(Shared.Abstractions.Consts.PLUGINS_PATH, $"{modName}-{next}.json"), json.ToJsonString(new JsonSerializerOptions() {
                WriteIndented = true
            }));

            return $"{Shared.Abstractions.Consts.PLUGINS_PATH}/{modName}-{next}.json";
        }

        public static async Task LoadPlugin(string Path, IProgress<(string Status, float Percentage)> Progress)
        {
            var pluginName = Path[(Shared.Abstractions.Consts.PLUGINS_PATH.Length + 1)..];

            var builder = new ConfigurationBuilder();
            builder.AddJsonFile(Path, false, true);
            IConfigurationRoot? configRaw;
            PluginInstanceConfig config;
            string moduleContentsPath, rawInstanceId;

            try {
                configRaw = builder.Build();
                config = configRaw.GetSection("Plugin").Get<PluginInstanceConfig>();
                moduleContentsPath = configRaw.GetSection("Plugin").GetValue<string>("Path");
                rawInstanceId = configRaw.GetSection("Plugin").GetValue<string>("InstanceId");
            } catch (Exception ex) {
                Exception inner = ex;
                while (inner.InnerException != null)
                    inner = inner.InnerException;

                throw new InvalidPluginConfigurationException($"Plugin {pluginName}: Invalid configuration file.\n{inner.Message}");
            }

            moduleContentsPath = System.IO.Path.IsPathRooted(moduleContentsPath) ? moduleContentsPath : System.IO.Path.GetFullPath(System.IO.Path.Combine(Shared.Abstractions.Consts.PLUGINS_PATH, moduleContentsPath));

            var manifest = System.IO.Path.Combine(moduleContentsPath, "manifest.json");
            if (!System.IO.File.Exists(manifest)) {
                throw new PluginLoadException("manifest.json does not exist", null);
            }

            var manifestBuilder = new ConfigurationBuilder();
            manifestBuilder.AddJsonFile(manifest, false, true);
            var manifestConfig = manifestBuilder.Build();
            var modulePath = manifestConfig.GetValue<string>("Module");

            modulePath = System.IO.Path.IsPathRooted(modulePath) ? modulePath : System.IO.Path.Combine(moduleContentsPath, modulePath);

            // Generate InstanceId and Name if needed
            bool needRewrite = false;
            if (!Guid.TryParse(rawInstanceId, out var instanceId)) {
                instanceId = Guid.NewGuid();
                needRewrite = true;
            }
            if (String.IsNullOrEmpty(config.Name))
                config.Name = pluginName;

            if (needRewrite) {
                var jsonRaw = await System.IO.File.ReadAllTextAsync(Path);
                var json = JsonNode.Parse(jsonRaw);
                json["Plugin"]["InstanceId"] = instanceId;
                json["Plugin"]["Name"] = config.Name;
                await System.IO.File.WriteAllTextAsync(Path, json.ToJsonString(new JsonSerializerOptions() {
                    WriteIndented = true
                }));
            }

            if (LoadedPlugins.Any(x => x.InstanceId == instanceId)) {
                Log.Logger.Fatal($"Plugin {moduleContentsPath}: InstanceId {instanceId} already initialized");
                throw new PluginLoadException($"Plugin {moduleContentsPath}: InstanceId {instanceId} already initialized", null);
            }

            if (LoadedPlugins.Any(x => x.PluginInstanceConfig.Name == config.Name)) {
                Log.Logger.Fatal($"Plugin {moduleContentsPath}: Name {config.Name} already exists");
                throw new PluginLoadException($"Plugin {moduleContentsPath}: Name {config.Name} already exists", null);
            }

            Log.Logger.Debug($"Loading plugin {config.Name} ({modulePath})...");
            Progress.Report(($"Loading plugin {config.Name} ({modulePath})...", 0f));

            PluginAssemblyLoadContext ctx;
            Assembly asm;

            Type? pluginType = null;

            try {
                ctx = new PluginAssemblyLoadContext(modulePath);
                asm = ctx.LoadFromAssemblyPath(modulePath);

                pluginType = asm.GetTypes().FirstOrDefault(x => typeof(IPlugin).IsAssignableFrom(x));
            } catch (Exception ex) {
                throw new PluginLoadException($"Plugin {modulePath}: failed to load", ex);
            }

            if (pluginType == null) {
                Log.Logger.Error($"Invalid plugin {modulePath}");

                ctx.Unload();
                throw new PluginLoadException($"Invalid plugin {modulePath}", null);
            }

            var inst = (IPlugin?)Activator.CreateInstance(pluginType);

            if (inst == null) {
                Log.Logger.Error($"Failed to load plugin {modulePath}");

                ctx.Unload();
                throw new PluginLoadException($"Failed to load plugin {modulePath}", null);
            }
            if (Global.Consts.Version.Major != inst.CompatibleMajorVersion) {
                var msgbox = MessageBoxManager.GetMessageBoxStandard(
                    "Plugin loader",
                    $"Plugin {modulePath}: Incompatible to RT# core version (RT# - {Global.Consts.Version.Major}, plugin - {inst.CompatibleMajorVersion}), load anyways?",
                    ButtonEnum.YesNo,
                    Icon.Error);
                if (await msgbox.ShowWindowAsync() != ButtonResult.Yes) {
                    throw new PluginLoadException($"Incompatible RT# core version for plugin {modulePath}", null);
                }
            }

            var plugin = new PluginInstance(ctx, asm, inst, configRaw, Path, instanceId, modulePath);

            Progress.Report(($"Initializing plugin {pluginName} ({inst.Version.VersionDisplayString})...", 0f));
            Log.Logger.Debug($"Initializing plugin {pluginName} ({inst.Version.VersionDisplayString})...");

            void evPluginProgress(object? sender, (string Status, float Percentage) e)
            {
                Progress.Report(($"{pluginName}: {e.Status}", e.Percentage));
            }

            var pluginProgress = new Progress<(string Status, float Percentage)>();
            pluginProgress.ProgressChanged += evPluginProgress;
            try {
                await inst.Init(plugin, pluginProgress);
            } catch (Exception ex) {
                try {
                    await inst.Unload();
                } catch { }

                LoadedPlugins.Remove(plugin);
                ctx.Unload();

                Log.Logger.Error($"Failed to initialize plugin {modulePath}: {ex}");
                await MessageBoxManager.GetMessageBoxStandard("RT# - Failed to load plugin", $"Failed to load plugin {modulePath}\n{ex}", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterOwner).ShowAsync();

                return;
            }
            pluginProgress.ProgressChanged -= evPluginProgress;
            LoadedPlugins.Add(plugin);

            Log.Logger.Information($"Loaded plugin {pluginName}");
            Progress.Report(($"Loaded plugin {pluginName}", 100f));
        }

		public static async Task LoadPlugins(WaitingBox Progress)
        {
            if (!Directory.Exists(Shared.Abstractions.Consts.PLUGINS_PATH)) {
                Log.Logger.Information("No plugins loaded");
                Progress.Report((100, $"No plugins loaded"));
                return;
            }

            var files = Directory.GetFiles(Shared.Abstractions.Consts.PLUGINS_PATH, "*.json");

            for (var x = 0;x < files.Length;x++) {
                int progress = (int)((float)x / files.Length * 100);
                int nextProgress = (int)((float)(x + 1) / files.Length * 100);

                await LoadPlugin(files[x], new Progress<(string Status, float Progress)>((e) => {
                    Progress.Report(((int)(e.Progress / files.Length), e.Status));
                }));
            }

            Progress.Report((100, $"Plugins loaded"));
        }

        public static List<string> ListUnloadedPluginDirs()
        {
            var dirs = Directory.GetDirectories(Shared.Abstractions.Consts.PLUGINS_PATH);
            var ret = new List<string>();

            foreach (var dir in dirs) {
                if (!LoadedPlugins.Any(x => x.FullModulePath.StartsWith(Path.GetFullPath(dir)))) {
                    ret.Add(dir);
                }
            }

            return ret;
        }

        public static async Task UnloadAll()
        {
            await Task.WhenAll(LoadedPlugins.Select(x => x.Unload()));
        }

        public class InvalidPluginConfigurationException : Exception
        {
            public InvalidPluginConfigurationException(string message) : base(message)
            {
            }
        }

        public class PluginLoadException : Exception
        {
            public PluginLoadException(string message, Exception? inner) : base(message, inner)
            {
            }
        }

        public class PluginInitializationException : Exception
        {
            public PluginInitializationException(string message, Exception inner) : base(message, inner)
            {
            }
        }
    }
}
