using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Text.Json;
using Nito.AsyncEx;
using System.Net;
using Avalonia;
using System.IO;

namespace RTSharp.Core
{
    public class Behavior
    {
        public TimeSpan FilesPollingInterval { get; set; }

        public TimeSpan PeersPollingInterval { get; set; }

        public TimeSpan TrackersPollingInterval { get; set; }

        public Dictionary<string, string> PeerOriginReplacements { get; set; }
    }

    public class Caching
    {
        public bool FilesCachingEnabled { get; init; }

        public int ConcurrentPeerCachingRequests { get; set; }

        public int InMemoryImages { get; set; }
    }

    public class UIState
    {
        public string TorrentGridState { get; set; }

        public string DockState { get; set; }

        public string DockVMState { get; set; }

        public bool TrayIconVisible { get; set; }
    }

    public class Server
    {
        public string Host { get; set; }

        public ushort AuxiliaryServicePort { get; set; }

        public string? TrustedThumbprint { get; set; }

        public bool? VerifyNative { get; set; }
    }

    public class Config
    {
        private readonly IServiceProvider Provider;

        public static string ConfigPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        private static AsyncLock GlobalRewriteLock = new AsyncLock();

        public static IServiceCollection AddConfig(IConfiguration Config, IServiceCollection Services)
        {
            Services.AddTransient(services => new Config(services));
            Services.Configure<Behavior>(Config.GetSection(nameof(Behavior)));
            Services.Configure<Caching>(Config.GetSection(nameof(Caching)));
            Services.Configure<UIState>(Config.GetSection(nameof(UIState)));
            Services.Configure<Dictionary<string, Server>>(Config.GetSection(nameof(Servers)));

            Services.AddOptions();
            return Services;
        }

        public Config(IServiceProvider Provider)
        {
            this.Provider = Provider;
        }

        public static async Task WriteDefaultConfig()
        {
            if (!File.Exists(ConfigPath))
                await File.WriteAllTextAsync(ConfigPath, DefaultConfig);
        }

        public async Task Rewrite()
        {
            using (await GlobalRewriteLock.LockAsync()) {
                var jsonRaw = await File.ReadAllTextAsync(ConfigPath);
                var json = JsonNode.Parse(jsonRaw)!;

                json[nameof(Behavior)] = JsonSerializer.SerializeToNode(Behavior.Value);
                json[nameof(Caching)] = JsonSerializer.SerializeToNode(Caching.Value);
                json[nameof(UIState)] = JsonSerializer.SerializeToNode(UIState.Value);
                json[nameof(Servers)] = JsonSerializer.SerializeToNode(Servers.Value);
                await File.WriteAllTextAsync(ConfigPath, json.ToJsonString(new JsonSerializerOptions() {
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
    }
  },
  "Caching": {
    "FilesCachingEnabled": true,
    "ConcurrentPeerCachingRequests": 50,
    "InMemoryImages": 100
  },
  "UIState": {
    "TorrentGridState": null
  },
  "Servers": {
    "example": {
      "Host": "example.com",
      "AuxiliaryServicePort": 12345,
      "TrustedThumbprint": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
      "VerifyNative": false
    }
  }
}
""";

        public Lazy<Behavior> Behavior => new Lazy<Behavior>(() => Provider.GetRequiredService<IOptionsMonitor<Behavior>>().CurrentValue);

        public Lazy<Caching> Caching => new Lazy<Caching>(() => Provider.GetRequiredService<IOptionsMonitor<Caching>>().CurrentValue);

        public Lazy<UIState> UIState => new Lazy<UIState>(() => Provider.GetRequiredService<IOptionsMonitor<UIState>>().CurrentValue);

        public Lazy<Dictionary<string, Server>> Servers => new Lazy<Dictionary<string, Server>>(() => Provider.GetRequiredService<IOptionsMonitor<Dictionary<string, Server>>>().CurrentValue);
    }
}
