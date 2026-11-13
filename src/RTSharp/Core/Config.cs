using Avalonia.Controls;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Nito.AsyncEx;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RTSharp.Core;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config.Models.Behavior))]
[JsonSerializable(typeof(Config.Models.Caching))]
[JsonSerializable(typeof(Config.Models.UIState))]
[JsonSerializable(typeof(Dictionary<string, Config.Models.Server>))]
[JsonSerializable(typeof(Config.Models.Look))]
[JsonSerializable(typeof(Config.Models.Proxy))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

public class Config
{
    public class Models
    {
        public class Behavior
        {
            public TimeSpan FilesPollingInterval { get; set; }

            public TimeSpan PeersPollingInterval { get; set; }

            public TimeSpan TrackersPollingInterval { get; set; }

            public TimeSpan SearchAsYouGoDelay { get; set; }

            public Dictionary<string, string> PeerOriginReplacements { get; set; } = new();

            public bool UseFaviconGoogleAPI { get; set; }

            public bool PollActiveTabOnly { get; set; }

            public bool AutoStartRecheckedTorrents { get; set; }

            public string PeerOriginLookupMethod { get; set; } = "RDAP";
        }

        public class Caching
        {
            public bool FilesCachingEnabled { get; set; }

            public int ConcurrentPeerCachingRequests { get; set; }

            public int InMemoryImages { get; set; }
        }

        public class UIState
        {
            public string? TorrentGridState { get; set; }

            public string? DockState { get; set; }

            public int? LastPosX { get; set; }

            public int? LastPosY { get; set; }

            /// <summary>
            /// Keyed by window type name
            /// </summary>
            public Dictionary<string, WindowSize> WindowSizes { get; set; } = new();

            public bool TrayIconVisible { get; set; }

            public double[]? TorrentListingGridRowHeights { get; set; }

            public void RestoreWindowSize(Window Window)
            {
                if (!WindowSizes.TryGetValue(Window.GetType().Name, out var size))
                    return;

                Window.Width = size.Width;
                Window.Height = size.Height;
                Window.WindowState = size.State;
            }

            /// <returns>Whether the stored size changed</returns>
            public bool StoreWindowSize(Window Window)
            {
                var key = Window.GetType().Name;
                WindowSizes.TryGetValue(key, out var previous);

                var state = Window.WindowState == WindowState.Minimized ? WindowState.Normal : Window.WindowState;
                var size = Window.WindowState != WindowState.Normal && previous != null ?
                    previous with { State = state } :
                    new WindowSize((uint)Window.ClientSize.Width, (uint)Window.ClientSize.Height, state);

                if (size == previous)
                    return false;

                WindowSizes[key] = size;
                return true;
            }
        }

        public record WindowSize(uint Width, uint Height, [property: JsonConverter(typeof(JsonStringEnumConverter<WindowState>))] WindowState State);

        public class Server
        {
            public string Host { get; set; } = null!; // set by deserializer

            public ushort DaemonPort { get; set; }

            public string TrustedThumbprint { get; set; } = null!; // set by deserializer
        }

        public class Proxy
        {
            public bool Enabled { get; set; }

            public string Address { get; set; } = "";

            public string Username { get; set; } = "";

            public string Password { get; set; } = "";
        }

        public class Look
        {
            public class DataProvidersConfig
            {
                public bool SpeedChartEnabled { get; set; }
            }

            public class TorrentListingConfig
            {
                public int RowHeight { get; set; }
            }

            public DataProvidersConfig DataProviders { get; set; } = new() {
                SpeedChartEnabled = true
            };

            public TorrentListingConfig TorrentListing { get; set; } = new() {
                RowHeight = 32
            };

            public bool AlternatingRowColors { get; set; }

            public string Theme { get; set; } = Themes.Standard;

            public enum Variant
            {
                System,
                Light,
                Dark
            }

            public Variant ThemeVariant { get; set; } = Variant.System;
        }
    }

    private readonly IServiceProvider Provider;

    private static AsyncLock GlobalRewriteLock = new AsyncLock();

    public static IServiceCollection AddConfig(IConfiguration Config, IServiceCollection Services)
    {
        Services.AddTransient(services => new Config(services));
        Services.Configure<Models.Behavior>(Config.GetSection(nameof(Behavior)));
        Services.Configure<Models.Caching>(Config.GetSection(nameof(Caching)));
        Services.Configure<Models.UIState>(Config.GetSection(nameof(UIState)));
        Services.Configure<Models.Look>(Config.GetSection(nameof(Look)));
        Services.Configure<Models.Proxy>(Config.GetSection(nameof(Proxy)));
        Services.Configure<Dictionary<string, Models.Server>>(Config.GetSection(nameof(Servers)));

        Services.AddOptions();
        return Services;
    }

    public Config(IServiceProvider Provider)
    {
        this.Provider = Provider;
    }

    public static void WriteDefaultConfig()
    {
        Directory.CreateDirectory(Shared.Abstractions.Consts.USER_DATA_PATH);
        if (!File.Exists(Shared.Abstractions.Consts.APP_CONFIG_PATH))
            File.WriteAllText(Shared.Abstractions.Consts.APP_CONFIG_PATH, DefaultConfig);
    }

    public async Task Rewrite()
    {
        using (await GlobalRewriteLock.LockAsync()) {
            var jsonRaw = await File.ReadAllTextAsync(Shared.Abstractions.Consts.APP_CONFIG_PATH);
            var json = JsonNode.Parse(jsonRaw)!;

            var sourceGenOptions = new JsonSerializerOptions {
                TypeInfoResolver = SourceGenerationContext.Default
            };

            json[nameof(Behavior)] = JsonSerializer.SerializeToNode(Behavior.Value, SourceGenerationContext.Default.Behavior);
            json[nameof(Caching)] = JsonSerializer.SerializeToNode(Caching.Value, SourceGenerationContext.Default.Caching);
            json[nameof(UIState)] = JsonSerializer.SerializeToNode(UIState.Value, SourceGenerationContext.Default.UIState);
            json[nameof(Servers)] = JsonSerializer.SerializeToNode(Servers.Value, SourceGenerationContext.Default.DictionaryStringServer);
            json[nameof(Look)] = JsonSerializer.SerializeToNode(Look.Value, SourceGenerationContext.Default.Look);
            json[nameof(Proxy)] = JsonSerializer.SerializeToNode(Proxy.Value, SourceGenerationContext.Default.Proxy);
            await File.WriteAllTextAsync(Shared.Abstractions.Consts.APP_CONFIG_PATH, json.ToJsonString(new JsonSerializerOptions() {
                WriteIndented = true
            }));
        }
    }

    public static string DefaultConfig => """
{
  "Behavior": {
    "FilesPollingInterval": "00:00:01",
    "PeersPollingInterval": "00:00:01",
    "TrackersPollingInterval": "00:00:01",
    "PeerOriginReplacements": {
    },
    "SearchAsYouGoDelay": "00:00:01",
    "UseFaviconGoogleAPI": false,
    "PollActiveTabOnly": false,
    "AutoStartRecheckedTorrents": false,
    "PeerOriginLookupMethod": "RDAP"
  },
  "Caching": {
    "FilesCachingEnabled": true,
    "ConcurrentPeerCachingRequests": 50,
    "InMemoryImages": 100
  },
  "UIState": {
    "TorrentGridState": null,
    "DockState": null,
    "LastPosX": null,
    "LastPosY": null,
    "WindowSizes": {}
  },
  "Look": {
    "DataProviders": {
      "SpeedChartEnabled": true
    },
    "TorrentListing": {
      "RowHeight": 21
    },
    "AlternatingRowColors": false,
    "Theme": "Standard",
    "ThemeVariant": "System"
  },
  "Proxy": {
    "Enabled": false,
    "Address": "",
    "Username": "",
    "Password": ""
  },
  "Servers": {
  }
}
""";

    public Lazy<Models.Behavior> Behavior => new Lazy<Models.Behavior>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Behavior>>().CurrentValue);

    public Lazy<Models.Caching> Caching => new Lazy<Models.Caching>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Caching>>().CurrentValue);

    public Lazy<Models.UIState> UIState => new Lazy<Models.UIState>(() => Provider.GetRequiredService<IOptionsMonitor<Models.UIState>>().CurrentValue);

    public Lazy<Dictionary<string, Models.Server>> Servers => new Lazy<Dictionary<string, Models.Server>>(() => Provider.GetRequiredService<IOptionsMonitor<Dictionary<string, Models.Server>>>().CurrentValue);

    public Lazy<Models.Look> Look => new Lazy<Models.Look>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Look>>().CurrentValue);

    public Lazy<Models.Proxy> Proxy => new Lazy<Models.Proxy>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Proxy>>().CurrentValue);
}
