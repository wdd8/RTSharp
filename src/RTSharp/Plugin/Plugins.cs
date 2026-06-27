using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using DynamicData;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Plugin;

public static class Plugins
{
    public static ObservableCollection<RTSharpPlugin> LoadedPlugins { get; } = new ObservableCollection<RTSharpPlugin>();

    public static SourceList<RTSharpDataProvider> DataProviders { get; } = new SourceList<RTSharpDataProvider>();

    public enum HookType
    {
        TorrentListing_EvLoadingRow,
        TorrentListing_EvLoadingCell,
        AddTorrent_EvDragDrop,
    }

    private static object[][] Hooks = Array.Empty<object[]>();
    private static FrozenDictionary<HookType, Lock> HookLocks;

    static Plugins()
    {
        Hooks = new object[Enum.GetValues<HookType>().Length][];

        Hooks[(int)HookType.TorrentListing_EvLoadingRow] = Array.Empty<Action<object, DataGridRowEventArgs>>();
        Hooks[(int)HookType.TorrentListing_EvLoadingCell] = Array.Empty<Action<object, string, DataGridCell>>();
        Hooks[(int)HookType.AddTorrent_EvDragDrop] = Array.Empty<Func<object, ValueTask>>();

        HookLocks = Enum.GetValues<HookType>().ToDictionary(x => x, _ => new Lock()).ToFrozenDictionary();
    }

    public static IDisposable Hook<T>(HookType type, Func<T, ValueTask> fx)
    {
        lock (HookLocks[type]) {
            var list = (Func<T, ValueTask>[])Hooks[(int)type];
            var newList = list.ToList();
            newList.Add(fx);
            Hooks[(int)type] = newList.ToArray();
        }

        return Disposable.Create(() => {
            lock (HookLocks[type]) {
                var list = (Func<T, ValueTask>[])Hooks[(int)type];
                var newList = list.ToList();
                newList.Remove(fx);
                Hooks[(int)type] = newList.ToArray();
            }
        });
    }

    public static IDisposable Hook<T, T2>(HookType type, Action<T, T2> fx)
    {
        lock (HookLocks[type]) {
            var list = (Action<T, T2>[])Hooks[(int)type];
            var newList = list.ToList();
            newList.Add(fx);
            Hooks[(int)type] = newList.ToArray();
        }

        return Disposable.Create(() => {
            lock (HookLocks[type]) {
                var list = (Action<T, T2>[])Hooks[(int)type];
                var newList = list.ToList();
                newList.Remove(fx);
                Hooks[(int)type] = newList.ToArray();
            }
        });
    }

    public static IDisposable Hook<T, T2, T3>(HookType type, Action<T, T2, T3> fx)
    {
        lock (HookLocks[type]) {
            var list = (Action<T, T2, T3>[])Hooks[(int)type];
            var newList = list.ToList();
            newList.Add(fx);
            Hooks[(int)type] = newList.ToArray();
        }

        return Disposable.Create(() => {
            lock (HookLocks[type]) {
                var list = (Action<T, T2, T3>[])Hooks[(int)type];
                var newList = list.ToList();
                newList.Remove(fx);
                Hooks[(int)type] = newList.ToArray();
            }
        });
    }

    public static IDisposable Hook<T>(HookType type, Action<T> fx)
    {
        lock (HookLocks[type]) {
            var list = (Action<T>[])Hooks[(int)type];
            var newList = list.ToList();
            newList.Add(fx);
            Hooks[(int)type] = newList.ToArray();
        }

        return Disposable.Create(() => {
            lock (HookLocks[type]) {
                var list = (Action<T>[])Hooks[(int)type];
                var newList = list.ToList();
                newList.Remove(fx);
                Hooks[(int)type] = newList.ToArray();
            }
        });
    }

    internal static Action<T>[] GetHook<T>(HookType type) =>
        (Action<T>[])Hooks[(int)type];

    internal static Action<T, T2>[] GetHook<T, T2>(HookType type) =>
        (Action<T, T2>[])Hooks[(int)type];

    internal static Action<T, T2, T3>[] GetHook<T, T2, T3>(HookType type) =>
        (Action<T, T2, T3>[])Hooks[(int)type];

    internal static Func<T, ValueTask>[] GetHookAsync<T>(HookType type) =>
        (Func<T, ValueTask>[])Hooks[(int)type];

    [RequiresUnreferencedCode("Plugins")]
    private static PluginManifest? GetManifest(string ModuleDir)
    {
        if (!Path.IsPathRooted(ModuleDir)) {
            throw new InvalidOperationException("Supplied module directory path must be absolute");
        }

        var manifestPath = Path.Combine(ModuleDir, "manifest.json");
        if (!System.IO.File.Exists(manifestPath))
            return null;

        var builder = new ConfigurationBuilder();
        builder.AddJsonFile(manifestPath, optional: false, reloadOnChange: false);
        var ret = builder.Build().Get<PluginManifest>();

        if (ret?.Module == null || ret?.Description == null)
            return null;

        if (Path.IsPathRooted(ret.Module)) {
            // TODO: harsher check?
            throw new InvalidOperationException("Module name must be in it's directory");
        }

        ret.ModuleDir = ModuleDir;

        return ret;
    }

    /// <summary>
    /// Enumerates installable plugin modules (directories under
    /// <see cref="Consts.PLUGINS_PATH" /> that contain a valid manifest.json).
    /// </summary>
    [RequiresUnreferencedCode("Plugins")]
    public static IReadOnlyList<PluginManifest> GetAvailablePlugins()
    {
        if (!Directory.Exists(Consts.PLUGINS_PATH))
            return [];

        var ret = new List<PluginManifest>();

        foreach (var dir in Directory.GetDirectories(Consts.PLUGINS_PATH)) {
            var manifest = GetManifest(dir);
            if (manifest?.Module == null)
                continue;

            ret.Add(manifest);
        }

        return ret;
    }

    /// <summary>
    /// Enumerates configured plugin instances (each config file in <see cref="Consts.PLUGINS_CONFIG_PATH" />).
    /// </summary>
    [RequiresUnreferencedCode("Plugins")]
    public static IReadOnlyList<ConfiguredPlugin> ListPluginConfigurations()
    {
        var ret = new List<ConfiguredPlugin>();
        if (!Directory.Exists(Consts.PLUGINS_CONFIG_PATH))
            return ret;

        foreach (var file in Directory.GetFiles(Consts.PLUGINS_CONFIG_PATH, "*.json")) {
            try {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile(file, optional: false, reloadOnChange: false);
                var config = builder.Build().GetSection(PluginInstanceConfig.Section).Get<PluginInstanceConfig>()!;

                var moduleContentsPath = Path.IsPathRooted(config.Path) ? config.Path : Path.GetFullPath(Path.Combine(Consts.PLUGINS_PATH, config.Path));

                var manifest = GetManifest(moduleContentsPath);

                if (manifest == null) {
                    Log.Logger.Error($"Plugin config {file}: manifest.json not found or invalid");
                    continue;
                }

                DataProviderInstanceConfig? dpConfig = null;
                if (manifest.IsDataProvider) {
                    dpConfig = builder.Build().GetSection(PluginInstanceConfig.Section).Get<DataProviderInstanceConfig>()!;
                }

                ret.Add(new ConfiguredPlugin {
                    InstanceId = config.InstanceId,
                    Manifest = manifest,
                    ConfigPath = Path.GetFullPath(file),
                    InstanceName = config.Name,
                    ServerId = dpConfig?.ServerId,
                    DataProviderName = dpConfig?.ProviderName,
                    Color = dpConfig?.Color
                });
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Plugin config {file}: failed to read");
                continue;
            }
        }

        return ret;
    }

    public static async Task<string> CreatePluginConfig(string ModuleDir, string InstanceName, bool IsDataProvider, string? ServerId, string? DataProviderName, Color? Color)
    {
        PluginInstanceConfig section;
        Type type;

        var duplicate = ListPluginConfigurations().Any(x => x.InstanceName == InstanceName);
        if (duplicate) {
            throw new DuplicatePluginInstanceNameException($"Instance name {InstanceName} is already used by a different plugin configuration");
        }

        if (IsDataProvider) {
            section = new DataProviderInstanceConfig {
                InstanceId = Guid.NewGuid(),
                Name = InstanceName,
                Path = ModuleDir,
                Color = Color.HasValue ? $"#{Color.Value.ToUInt32().ToString("x8", CultureInfo.InvariantCulture)}" : null, // dup from .ToString() without named colors
                ServerId = ServerId ?? "",
                ProviderName = DataProviderName ?? ""
            };
            type = typeof(DataProviderInstanceConfig);
        } else {
            section = new PluginInstanceConfig {
                InstanceId = Guid.NewGuid(),
                Name = InstanceName,
                Path = ModuleDir
            };
            type = typeof(PluginInstanceConfig);
        }

        var baseName = PathUtils.SanitizeFileName(InstanceName);
        if (string.IsNullOrEmpty(baseName))
            baseName = PathUtils.SanitizeFileName(PathUtils.GetTopMostDirectoryName(ModuleDir)!);

        Directory.CreateDirectory(Consts.PLUGINS_CONFIG_PATH);
        var path = GetUniqueConfigPath(baseName);

        var root = new JsonObject {
            [PluginInstanceConfig.Section] = JsonSerializer.SerializeToNode(section, type, new JsonSerializerOptions {
                WriteIndented = true
            })
        };
        await WriteConfig(path, root);

        return path;
    }

    public static async Task UpdatePluginConfig(string ConfigPath, string InstanceName, string? ServerId, string? DataProviderName, Color? Color = null)
    {
        var duplicate = ListPluginConfigurations().Any(x => x.InstanceName == InstanceName && x.ConfigPath != ConfigPath);
        if (duplicate) {
            throw new DuplicatePluginInstanceNameException($"Instance name {InstanceName} is already used by a different plugin configuration");
        }

        var json = JsonNode.Parse(System.IO.File.ReadAllText(ConfigPath), documentOptions: new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        })!;

        var plugin = json[PluginInstanceConfig.Section]?.AsObject();
        if (plugin == null) {
            plugin = new JsonObject();
            json[PluginInstanceConfig.Section] = plugin;
        }
        plugin[nameof(PluginInstanceConfig.Name)] = InstanceName;

        if (ServerId != null) {
            plugin[nameof(DataProviderInstanceConfig.ServerId)] = ServerId;
            plugin[nameof(DataProviderInstanceConfig.Color)] = Color.HasValue
                ? $"#{Color.Value.ToUInt32().ToString("x8", CultureInfo.InvariantCulture)}" // dup from .ToString() without named colors
                : null;
        }
        if (DataProviderName != null)
            plugin[nameof(DataProviderInstanceConfig.ProviderName)] = DataProviderName;

        await WriteConfig(ConfigPath, json);
    }

    public static void DeletePluginConfig(string ConfigPath)
    {
        if (System.IO.File.Exists(ConfigPath))
            System.IO.File.Delete(ConfigPath);
    }

    private static string GetUniqueConfigPath(string BaseName)
    {
        var candidate = Path.Combine(Consts.PLUGINS_CONFIG_PATH, $"{BaseName}.json");
        if (!System.IO.File.Exists(candidate))
            return candidate;

        for (int x = 1;;x++) {
            candidate = Path.Combine(Consts.PLUGINS_CONFIG_PATH, $"{BaseName}-{x}.json");
            if (!System.IO.File.Exists(candidate))
                return candidate;
        }
    }

    private static async Task WriteConfig(string Path, JsonNode Json)
    {
        var tmp = Path + ".tmp";
        await System.IO.File.WriteAllTextAsync(tmp, Json.ToJsonString(new JsonSerializerOptions() {
            WriteIndented = true
        }));
        System.IO.File.Move(tmp, Path, overwrite: true);
    }

    [RequiresUnreferencedCode("Plugins")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPlugin))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Shared.Abstractions.DataProvider.IDataProvider))]
    public static async Task<RTSharpPlugin?> LoadPlugin(string Path, Action<(string Status, float Percentage)> Progress)
    {
        var pluginName = Path[(Path.LastIndexOfAny(['/', '\\']) + 1)..];

        var builder = new ConfigurationBuilder();
        builder.AddJsonFile(Path, false, true);
        IConfigurationRoot? configRaw;
        PluginInstanceConfig config;
        string moduleContentsPath;

        try {
            configRaw = builder.Build();
            config = configRaw.GetSection(PluginInstanceConfig.Section).Get<PluginInstanceConfig>()!;
            moduleContentsPath = config.Path;

            if (config == null)
                throw new NullReferenceException(nameof(config));
            if (moduleContentsPath == null)
                throw new NullReferenceException(nameof(moduleContentsPath));
        } catch (Exception ex) {
            Exception inner = ex;
            while (inner.InnerException != null)
                inner = inner.InnerException;

            throw new InvalidPluginConfigurationException($"Plugin {pluginName}: Invalid configuration file.\n{inner.Message}");
        }

        moduleContentsPath = System.IO.Path.IsPathRooted(moduleContentsPath) ? moduleContentsPath : System.IO.Path.GetFullPath(System.IO.Path.Combine(Consts.PLUGINS_PATH, moduleContentsPath));
        var manifest = GetManifest(moduleContentsPath) ?? throw new PluginLoadException("Plugin {pluginName}: manifest.json does not exist or malformed", null);
        var modulePath = System.IO.Path.Combine(moduleContentsPath, manifest.Module);

        if (LoadedPlugins.Any(x => x.InstanceId == config.InstanceId)) {
            Log.Logger.Fatal($"Plugin {moduleContentsPath}: InstanceId {config.InstanceId} already initialized");
            throw new PluginLoadException($"Plugin {moduleContentsPath}: InstanceId {config.InstanceId} already initialized", null);
        }

        if (LoadedPlugins.Any(x => x.Config.Plugin.Name == config.Name)) {
            Log.Logger.Fatal($"Plugin {moduleContentsPath}: Name {config.Name} already exists");
            throw new PluginLoadException($"Plugin {moduleContentsPath}: Name {config.Name} already exists", null);
        }

        Log.Logger.Debug($"Loading plugin {config.Name} ({modulePath})...");
        Progress(($"Loading plugin {config.Name} ({modulePath})...", 0f));

        PluginAssemblyLoadContext ctx;
        Assembly asm;

        Type? pluginType = null;

        try {
            ctx = new PluginAssemblyLoadContext(modulePath);
            asm = ctx.LoadFromAssemblyPath(modulePath);

            pluginType = asm.GetTypes().FirstOrDefault(x => typeof(IPluginInit).IsAssignableFrom(x));
        } catch (Exception ex) {
            throw new PluginLoadException($"Plugin {modulePath}: failed to load", ex);
        }

        if (pluginType == null) {
            Log.Logger.Error($"Invalid plugin {modulePath}");

            ctx.Unload();
            throw new PluginLoadException($"Invalid plugin {modulePath}", null);
        }

        var init = (IPluginInit?)Activator.CreateInstance(pluginType);

        if (init == null) {
            Log.Logger.Error($"Failed to load plugin {modulePath}");

            ctx.Unload();
            throw new PluginLoadException($"Failed to load plugin {modulePath}", null);
        }
        if (Global.Consts.Version.Major != init.CompatibleMajorVersion) {
            var msgbox = MessageBoxManager.GetMessageBoxStandard(
                title: "Plugin loader",
                text: $"Plugin {modulePath}: Incompatible to RT# core version (RT# - {Global.Consts.Version.Major}, plugin - {init.CompatibleMajorVersion}), load anyways?",
                @enum: ButtonEnum.YesNo,
                icon: Icon.Error);
            if (await msgbox.ShowWindowAsync() != ButtonResult.Yes) {
                throw new PluginLoadException($"Incompatible RT# core version for plugin {modulePath}", null);
            }
        }

        var plugin = new RTSharpPlugin(ctx, asm, configRaw, Path, config.InstanceId, modulePath, init.ConfigType) {
            PluginInit = init
        };
        LoadedPlugins.Add(plugin);

        Log.Logger.Information($"Loaded plugin {plugin.Config.Plugin.Name}");
        Progress(($"Loaded plugin {plugin.Config.Plugin.Name}", 100f));

        return plugin;
    }

    [RequiresUnreferencedCode("Plugins")]
    public static async Task LoadPlugins(Action<float, string> Progress)
    {
        if (!Directory.Exists(Consts.PLUGINS_CONFIG_PATH)) {
            Log.Logger.Information("No plugins loaded");
            Progress(100, $"No plugins loaded");
            return;
        }

        var files = Directory.GetFiles(Consts.PLUGINS_CONFIG_PATH, "*.json");

        for (var x = 0;x < files.Length;x++) {
            int progress = (int)((float)x / files.Length * 100);
            int nextProgress = (int)((float)(x + 1) / files.Length * 100);

            _ = await LoadPlugin(files[x], ((string Status, float Progress) e) => {
                Progress((int)(e.Progress / files.Length), e.Status);
            });
        }

        Progress(100, $"Plugins loaded");
    }

    [RequiresUnreferencedCode("Plugins")]
    public static void ConfigureServices(IServiceCollection Services)
    {
        foreach (var plugin in LoadedPlugins) {
            try {
                plugin.PluginInit!.ConfigureServices(Services);
            } catch (Exception ex) {
                Log.Logger.Error(ex, $"Plugin {plugin.Config.Plugin.Name} failed to configure services");
            }
        }
    }

    [RequiresUnreferencedCode("Plugins")]
    public static async Task InitPlugins(Action<float, string> Progress)
    {
        var pending = LoadedPlugins.Where(x => x.Instance == null).ToList();

        for (var x = 0; x < pending.Count; x++) {
            var plugin = pending[x];
            var pluginName = plugin.Config.Plugin.Name;
            var init = plugin.PluginInit!;

            Progress((int)((float)x / pending.Count * 100), $"Initializing plugin {pluginName}...");
            Log.Logger.Debug($"Initializing plugin {pluginName} ({plugin.PluginVersion})...");

            void evPluginProgress((string Status, float Percentage) e)
            {
                Progress((int)(e.Percentage / pending.Count), $"{pluginName}: {e.Status}");
            }

            try {
                plugin.Instance = await init.Init(plugin, evPluginProgress);
            } catch (Exception ex) {
                try {
                    await plugin.Unload();
                } catch { }

                Log.Logger.Error($"Failed to initialize plugin {plugin.ModulePath}: {ex}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                    MessageBoxManager.GetMessageBoxStandard(
                        title: "RT# - Failed to load plugin",
                        text: $"Failed to load plugin {plugin.ModulePath}\n{ex}",
                        @enum: ButtonEnum.Ok,
                        icon: Icon.Error,
                        windowStartupLocation: WindowStartupLocation.CenterOwner
                    ).ShowAsync()
                );
            }
        }

        Progress(100, "Plugins initialized");
    }

    public static async Task UnloadAll()
    {
        await Task.WhenAll(LoadedPlugins.Select(x => x.Unload()));
    }

    public static RTSharpPlugin? GetPluginOrDefault(IDataProvider Dp)
    {
        return LoadedPlugins.FirstOrDefault(x => x.Config.Plugin.InstanceId == Dp.Host.Config.Plugin.InstanceId);
    }

    public class InvalidPluginConfigurationException(string message) : Exception(message)
    {
    }

    public class PluginLoadException(string message, Exception? inner) : Exception(message, inner)
    {
    }

    public class PluginInitializationException(string message, Exception inner) : Exception(message, inner)
    {
    }

    public class DuplicatePluginInstanceNameException(string message) : Exception(message)
    {
    }
}
