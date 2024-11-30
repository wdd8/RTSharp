using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Binding;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RTSharp.Core;
using RTSharp.Plugin;
using RTSharp.ViewModels.Converters;

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

            Plugins.DataProviders.CollectionChanged += DataProviders_CollectionChanged;
        }

        private void DataProviders_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems?.Count > 0) {
                foreach (var dp in e.NewItems.Cast<DataProvider>()) {
                    dp.Instance.TotalDLSpeed.PropertyChanged += TotalSpeed_PropertyChanged;
                    dp.Instance.TotalUPSpeed.PropertyChanged += TotalSpeed_PropertyChanged;
                }
            }

            if (Plugins.DataProviders.Count == 0)
                TrayIconText = "RT#";
        }

        private void TotalSpeed_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (Plugins.DataProviders.Count == 0) {
                TrayIconText = "RT#";
                return;
            }

            var upSpeed = (string)SIDataSpeedConverterInstance.Convert(Plugins.DataProviders.Sum(x => x.Instance.TotalUPSpeed.Value), typeof(string), null, CultureInfo.InvariantCulture);
            var dlSpeed = (string)SIDataSpeedConverterInstance.Convert(Plugins.DataProviders.Sum(x => x.Instance.TotalDLSpeed.Value), typeof(string), null, CultureInfo.InvariantCulture);
            TrayIconText = $"RT# - 🠉 {upSpeed} 🠋 {dlSpeed}";
        }
    }
}
