using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Abstractions.Client;

using SkiaSharp;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;

namespace RTSharp.ViewModels
{
    public class DataProvidersViewModel : ObservableViewModel
    {
        private readonly bool SpeedChartEnabled;

        public ObservableCollection<DataProviderItemViewModel> Items { get; } = new();

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-network-wired");

        public string HeaderName => "Data providers";

        public DataProvidersViewModel()
        {
            using (var scope = Core.ServiceProvider.CreateScope()) {
                var config = scope.ServiceProvider.GetRequiredService<Config>();
                SpeedChartEnabled = config.Look.Value.DataProviders.SpeedChartEnabled;
            }

            AddDisposable(Plugin.Plugins.DataProviders
                .Connect()
                .AutoRefreshOnObservable(x => x.State)
                .AutoRefreshOnObservable(x => x.PluginInstance.AttachedDaemonService.Latency)
                .Subscribe(x => {
                    foreach (var change in x) {
                        var item = Items.FirstOrDefault(i => i.DataProvider.InstanceId == change.Item.Current.PluginInstance.InstanceId);
                        if (item == default) {
                            var model = new Models.DataProvider {
                                DisplayName = change.Item.Current.PluginInstance.PluginInstanceConfig.Name,
                                InstanceId = change.Item.Current.PluginInstance.InstanceId,
                                State = change.Item.Current.State.Value,
                                Latency = Shared.Utils.Converters.ToAgoString(change.Item.Current.PluginInstance.AttachedDaemonService.Latency.Value),
                                TotalDLSpeed = 0,
                                TotalUPSpeed = 0,
                                ActiveTorrentCount = 0
                            };

                            Items.Add(new DataProviderItemViewModel(model, SpeedChartEnabled));
                        } else {
                            if (change.Reason == ListChangeReason.Remove) {
                                Items.Remove(item);
                                continue;
                            }

                            item.DataProvider.State = change.Item.Current.State.Value;
                            if (item.DataProvider.State != Shared.Abstractions.DataProviderState.INACTIVE)
                                item.DataProvider.Latency = Shared.Utils.Converters.ToAgoString(change.Item.Current.PluginInstance.AttachedDaemonService.Latency.Value);
                            else
                                item.DataProvider.Latency = "?";
                        }
                    }
                }));

            TorrentPolling.Torrents.Changed += TorrentPolling_TorrentBatchChange;
            AddDisposable(Disposable.Create(() => TorrentPolling.Torrents.Changed -= TorrentPolling_TorrentBatchChange));
        }

        private void TorrentPolling_TorrentBatchChange(object? sender, TorrentStoreChangeSet e)
        {
            var vm = Items.FirstOrDefault(x => x.DataProvider.InstanceId == e.DataProvider.PluginInstance.InstanceId);

            Debug.Assert(vm != null);
            if (vm == null)
                return;

            vm.ApplyTorrentsChanges(e);
        }
    }

    public partial class DataProviderItemViewModel : ObservableObject
    {
        private enum SPEED_CHART_MODE
        {
            DOWNLOAD,
            UPLOAD
        }

        private const int MAX_SPEED_CHART_SAMPLES = 120;

        private readonly record struct TorrentStats(ulong DLSpeed, ulong UPSpeed, bool Active);
        private readonly Dictionary<Models.Torrent, TorrentStats> PreviousTorrentStats = new(ReferenceEqualityComparer.Instance);

        private ulong DLSpeed;
        private ulong UPSpeed;
        private uint ActiveTorrentCount;

        private SPEED_CHART_MODE? CurrentSpeedChartMode;

        private readonly Axis SpeedChartYAxis;

        private double SpeedChartYAxisTopValue = 1;

        public Models.DataProvider DataProvider { get; }

        public ObservableCollection<double?> DLSpeedChartValues { get; } = new();

        public ObservableCollection<double?> UPSpeedChartValues { get; } = new();

        public ISeries[] SpeedChartSeries { get; }

        public ICartesianAxis[] SpeedChartXAxes { get; }

        public ICartesianAxis[] SpeedChartYAxes { get; }

        public bool SpeedChartEnabled { get; }

        [ObservableProperty]
        public partial string SpeedChartTopLabel { get; set; } = String.Empty;

        [ObservableProperty]
        public partial bool SpeedChartTopLabelVisible { get; set; }

        public DataProviderItemViewModel(Models.DataProvider Dp, bool SpeedChartEnabled)
        {
            DataProvider = Dp;
            this.SpeedChartEnabled = SpeedChartEnabled;

            var dlSpeedPaint = new SolidColorPaint(SKColor.Parse("#D64040")) {
                StrokeThickness = 2
            };
            var dlSpeedFill = new SolidColorPaint(SKColor.Parse("#D64040"));

            var upSpeedPaint = new SolidColorPaint(SKColor.Parse("#40D640")) {
                StrokeThickness = 2
            };
            var upSpeedFill = new SolidColorPaint(SKColor.Parse("#40D640"));

            var axisPaint = new SolidColorPaint(SKColor.Parse("#00000000"));

            SpeedChartSeries = [
                new LineSeries<double?> {
                    Name = "DL",
                    Values = DLSpeedChartValues,
                    Fill = dlSpeedFill,
                    Stroke = dlSpeedPaint,
                    GeometrySize = 0,
                    LineSmoothness = 1,
                    AnimationsSpeed = TimeSpan.FromMilliseconds(120),
                    DataPadding = new LvcPoint(0, 0)
                },
                new LineSeries<double?> {
                    Name = "UP",
                    Values = UPSpeedChartValues,
                    Fill = upSpeedFill,
                    Stroke = upSpeedPaint,
                    GeometrySize = 0,
                    LineSmoothness = 1,
                    AnimationsSpeed = TimeSpan.FromMilliseconds(120),
                    DataPadding = new LvcPoint(0, 0)
                }
            ];

            SpeedChartXAxes = [
                new Axis {
                    IsVisible = false,
                    SeparatorsPaint = axisPaint,
                    Padding = new LiveChartsCore.Drawing.Padding(0)
                }
            ];

            SpeedChartYAxis = new Axis {
                IsVisible = false,
                Position = AxisPosition.End,
                MinLimit = 0,
                MaxLimit = SpeedChartYAxisTopValue,
                MinStep = SpeedChartYAxisTopValue,
                ForceStepToMin = true,
                ShowSeparatorLines = false,
                SeparatorsPaint = axisPaint,
                Padding = new LiveChartsCore.Drawing.Padding(0)
            };

            SpeedChartYAxes = [SpeedChartYAxis];
        }

        public void ApplyTorrentsChanges(TorrentStoreChangeSet e)
        {
            foreach (var t in e.Removed) {
                if (PreviousTorrentStats.Remove(t, out var prev)) {
                    DLSpeed -= prev.DLSpeed;
                    UPSpeed -= prev.UPSpeed;
                    if (prev.Active)
                        ActiveTorrentCount--;
                }
            }

            foreach (var torrent in e.Added)
                ApplyTorrentChanges(torrent);

            foreach (var torrent in e.Refreshed)
                ApplyTorrentChanges(torrent);

            DataProvider.TotalDLSpeed = DLSpeed;
            DataProvider.TotalUPSpeed = UPSpeed;
            DataProvider.ActiveTorrentCount = ActiveTorrentCount;

#if DEBUG
            VerifyAgainstSnapshot();
#endif

            if (SpeedChartEnabled) {
                AddSpeedChartSample(DLSpeed, UPSpeed);
            }
        }

#if DEBUG
        private void VerifyAgainstSnapshot()
        {
            var expectedDLSpeed = 0UL;
            var expectedUPSpeed = 0UL;
            var expectedActiveCount = 0U;

            foreach (var t in TorrentPolling.Torrents.GetSnapshot()) {
                if (t.DataOwner.PluginInstance.InstanceId != DataProvider.InstanceId)
                    continue;

                expectedDLSpeed += t.DLSpeed;
                expectedUPSpeed += t.UPSpeed;
                if (t.InternalState.HasFlag(Shared.Abstractions.TORRENT_STATE.ACTIVE))
                    expectedActiveCount++;
            }

            Debug.Assert(DLSpeed == expectedDLSpeed);
            Debug.Assert(UPSpeed == expectedUPSpeed);
            Debug.Assert(ActiveTorrentCount == expectedActiveCount);
        }
#endif

        private void ApplyTorrentChanges(Models.Torrent In)
        {
            var active = In.InternalState.HasFlag(Shared.Abstractions.TORRENT_STATE.ACTIVE);

            ref var prev = ref CollectionsMarshal.GetValueRefOrAddDefault(PreviousTorrentStats, In, out var exists);

            if (exists) {
                DLSpeed += In.DLSpeed - prev.DLSpeed;
                UPSpeed += In.UPSpeed - prev.UPSpeed;
                if (active && !prev.Active)
                    ActiveTorrentCount++;
                else if (!active && prev.Active)
                    ActiveTorrentCount--;
            } else {
                DLSpeed += In.DLSpeed;
                UPSpeed += In.UPSpeed;
                if (active)
                    ActiveTorrentCount++;
            }

            prev = new TorrentStats(In.DLSpeed, In.UPSpeed, active);
        }

        public void AddSpeedChartSample(ulong DLSpeed, ulong UPSpeed)
        {
            SPEED_CHART_MODE chartMode = SPEED_CHART_MODE.UPLOAD;

            if (DLSpeed > UPSpeed) {
                chartMode = SPEED_CHART_MODE.DOWNLOAD;
            }

            if (UPSpeed >= DLSpeed) {
                chartMode = SPEED_CHART_MODE.UPLOAD;
            }

            var value = (double)(chartMode == SPEED_CHART_MODE.DOWNLOAD ? DLSpeed : UPSpeed);

            if (CurrentSpeedChartMode != null && CurrentSpeedChartMode != chartMode) {
                FillSwitchPoint(chartMode);
            }

            if (chartMode == SPEED_CHART_MODE.DOWNLOAD) {
                DLSpeedChartValues.Add(value);
                UPSpeedChartValues.Add(null);
            } else {
                DLSpeedChartValues.Add(null);
                UPSpeedChartValues.Add(value);
            }

            CurrentSpeedChartMode = chartMode;
            RemoveOldChartValues();
            UpdateYAxisScale();
        }

        private void FillSwitchPoint(SPEED_CHART_MODE Mode)
        {
            var lastIndex = DLSpeedChartValues.Count - 1;
            if (lastIndex < 0) {
                return;
            }

            if (Mode == SPEED_CHART_MODE.DOWNLOAD) {
                DLSpeedChartValues[lastIndex] = UPSpeedChartValues[lastIndex];
            } else {
                UPSpeedChartValues[lastIndex] = DLSpeedChartValues[lastIndex];
            }
        }

        private void RemoveOldChartValues()
        {
            while (DLSpeedChartValues.Count > MAX_SPEED_CHART_SAMPLES) {
                DLSpeedChartValues.RemoveAt(0);
                UPSpeedChartValues.RemoveAt(0);
            }
        }

        private void UpdateYAxisScale()
        {
            double maxValue = 0;
            foreach (var v in DLSpeedChartValues)
                if (v.HasValue && v.Value > maxValue)
                    maxValue = v.Value;
            foreach (var v in UPSpeedChartValues)
                if (v.HasValue && v.Value > maxValue)
                    maxValue = v.Value;

            SpeedChartYAxisTopValue = GetAxisCeiling(maxValue);
            SpeedChartYAxis.MaxLimit = SpeedChartYAxisTopValue;
            SpeedChartYAxis.MinStep = SpeedChartYAxisTopValue;
            SpeedChartTopLabelVisible = maxValue > 0;
            SpeedChartTopLabel = SpeedChartTopLabelVisible ? (Shared.Utils.Converters.GetSIDataSize((ulong)maxValue) + "/s") : String.Empty;
        }

        private static double GetAxisCeiling(double In)
        {
            if (In <= 0) {
                return 1;
            }

            var exp = Math.Floor(Math.Log10(In));
            var magnitude = Math.Pow(10, exp);
            var normalized = In / magnitude;

            var clamped = normalized switch {
                <= 1 => 1,
                <= 2 => 2,
                <= 5 => 5,
                _    => 10
            };

            return clamped * magnitude;
        }
    }
}
