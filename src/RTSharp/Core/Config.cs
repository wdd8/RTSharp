﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Text.Json;
using Nito.AsyncEx;
using System.IO;

namespace RTSharp.Core
{
    public class Config
    {
        public class Models
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

                public ushort DaemonPort { get; set; }

                public string? TrustedThumbprint { get; set; }

                public bool? VerifyNative { get; set; }
            }

            public class Look
            {
                public class TorrentListingConfig
                {
                    public int RowHeight { get; set; }
                }

                public TorrentListingConfig TorrentListing { get; set; }
            }
        }

        private readonly IServiceProvider Provider;

        public static string ConfigPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        private static AsyncLock GlobalRewriteLock = new AsyncLock();

        public static IServiceCollection AddConfig(IConfiguration Config, IServiceCollection Services)
        {
            Services.AddTransient(services => new Config(services));
            Services.Configure<Models.Behavior>(Config.GetSection(nameof(Behavior)));
            Services.Configure<Models.Caching>(Config.GetSection(nameof(Caching)));
            Services.Configure<Models.UIState>(Config.GetSection(nameof(UIState)));
            Services.Configure<Models.Look>(Config.GetSection(nameof(Look)));
            Services.Configure<Dictionary<string, Models.Server>>(Config.GetSection(nameof(Servers)));

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
                json[nameof(Look)] = JsonSerializer.SerializeToNode(Look.Value);
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
  "Look": {
    "TorrentListing": {
      "RowHeight": 32
    }
  },
  "Servers": {
    "example": {
      "Host": "example.com",
      "DaemonPort": 12345,
      "TrustedThumbprint": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
      "VerifyNative": false
    }
  }
}
""";

        public Lazy<Models.Behavior> Behavior => new Lazy<Models.Behavior>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Behavior>>().CurrentValue);

        public Lazy<Models.Caching> Caching => new Lazy<Models.Caching>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Caching>>().CurrentValue);

        public Lazy<Models.UIState> UIState => new Lazy<Models.UIState>(() => Provider.GetRequiredService<IOptionsMonitor<Models.UIState>>().CurrentValue);

        public Lazy<Dictionary<string, Models.Server>> Servers => new Lazy<Dictionary<string, Models.Server>>(() => Provider.GetRequiredService<IOptionsMonitor<Dictionary<string, Models.Server>>>().CurrentValue);

        public Lazy<Models.Look> Look => new Lazy<Models.Look>(() => Provider.GetRequiredService<IOptionsMonitor<Models.Look>>().CurrentValue);
    }
}
