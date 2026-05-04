using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Plugin;
using RTSharp.ViewModels.Converters;

using System;
using System.Globalization;
using System.Linq;

namespace RTSharp.ViewModels
{
    public partial class AppViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial bool TrayIconVisible { get; set; }

        [ObservableProperty]
        public partial string TrayIconText { get; set; } = "RT#";

        private SIDataSpeedConverter SIDataSpeedConverterInstance = new();

        public AppViewModel()
        {
            using var scope = Core.ServiceProvider.CreateScope();

            var uiStateMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<Config.Models.UIState>>();

            uiStateMonitor.OnChange(state => {
                this.TrayIconVisible = state.TrayIconVisible;
            });
            this.TrayIconVisible = uiStateMonitor.CurrentValue.TrayIconVisible;

            TorrentPolling.Torrents.Changed += TorrentPolling_TorrentBatchChange;
        }

        private void TorrentPolling_TorrentBatchChange(object? sender, TorrentStoreChangeSet e)
        {
            if (Plugins.DataProviders.Count == 0) {
                TrayIconText = "RT#";
                return;
            }

            long totalUPSpeed = 0;
            long totalDLSpeed = 0;
            foreach (var torrent in TorrentPolling.Torrents.GetSnapshot()) {
                totalUPSpeed += (long)torrent.UPSpeed;
                totalDLSpeed += (long)torrent.DLSpeed;
            }

            var upSpeed = (string)SIDataSpeedConverterInstance.Convert(totalUPSpeed, typeof(string), null, CultureInfo.InvariantCulture);
            var dlSpeed = (string)SIDataSpeedConverterInstance.Convert(totalDLSpeed, typeof(string), null, CultureInfo.InvariantCulture);
            TrayIconText = $"RT# - 🠉 {upSpeed} 🠋 {dlSpeed}";
        }
    }
}
