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

            var (totalDLSpeed, totalUPSpeed) = TorrentPolling.Torrents.GetTotalSpeeds();

            var upSpeed = (string)SIDataSpeedConverterInstance.Convert((long)totalUPSpeed, typeof(string), null, CultureInfo.InvariantCulture)!;
            var dlSpeed = (string)SIDataSpeedConverterInstance.Convert((long)totalDLSpeed, typeof(string), null, CultureInfo.InvariantCulture)!;
            TrayIconText = $"RT# - 🠉 {upSpeed} 🠋 {dlSpeed}";
        }
    }
}
