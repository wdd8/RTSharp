using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Binding;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Plugin;
using RTSharp.ViewModels.Converters;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;

namespace RTSharp.ViewModels
{
    public partial class AppViewModel : ObservableObject
    {
        [ObservableProperty]
        public bool trayIconVisible;

        [ObservableProperty]
        public string trayIconText = "RT#";

        private SIDataSpeedConverter SIDataSpeedConverterInstance = new();

        public AppViewModel()
        {
            using var scope = Core.ServiceProvider.CreateScope();

            var uiStateMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<Config.Models.UIState>>();

            uiStateMonitor.OnChange(state => {
                this.TrayIconVisible = state.TrayIconVisible;
            });
            this.TrayIconVisible = uiStateMonitor.CurrentValue.TrayIconVisible;

            TorrentPolling.TorrentBatchChange += TorrentPolling_TorrentBatchChange;
        }

        private void TorrentPolling_TorrentBatchChange(List<NotifyCollectionChangedEventArgs> In)
        {
            if (Plugins.DataProviders.Count == 0) {
                TrayIconText = "RT#";
                return;
            }

            var upSpeed = (string)SIDataSpeedConverterInstance.Convert(TorrentPolling.Torrents.Items.Sum(x => (long)x.UPSpeed), typeof(string), null, CultureInfo.InvariantCulture);
            var dlSpeed = (string)SIDataSpeedConverterInstance.Convert(TorrentPolling.Torrents.Items.Sum(x => (long)x.DLSpeed), typeof(string), null, CultureInfo.InvariantCulture);
            TrayIconText = $"RT# - 🠉 {upSpeed} 🠋 {dlSpeed}";
        }
    }
}
