using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using RTSharp.Plugin;

using LiveChartsCore;
using LiveChartsCore.Defaults;
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

        private readonly DispatcherTimer? SpeedChartTimer;

        public ObservableCollection<DataProviderItemViewModel> Items { get; } = new();

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa7-solid fa7-network-wired");

        public string HeaderName => "Data providers";

        public DataProvidersViewModel()
        {
            using (var scope = Core.ServiceProvider.CreateScope()) {
                var config = scope.ServiceProvider.GetRequiredService<Config>();
                SpeedChartEnabled = config.Look.Value.DataProviders.SpeedChartEnabled;
            }

            AddDisposable(Plugins.DataProviders
                .Connect()
                .AutoRefreshOnObservable(x => x.State)
                .AutoRefreshOnObservable(x => x.AttachedDaemonService.Latency)
                .Subscribe((Action<IChangeSet<RTSharpDataProvider>>)(x => {
                    foreach (var change in x) {
                        var item = Items.FirstOrDefault(i => i.DataProvider.InstanceId == change.Item.Current.InstanceId);
                        if (item == default) {
                            var model = new Models.DataProvider {
                                DisplayName = change.Item.Current.Config.Plugin.Name,
                                InstanceId = change.Item.Current.InstanceId,
                                State = change.Item.Current.State.Value,
                                Latency = Shared.Utils.Converters.ToAgoString(change.Item.Current.AttachedDaemonService.Latency.Value),
                                TotalDLSpeed = 0,
                                TotalUPSpeed = 0,
                                ActiveTorrentCount = 0
                            };

                            Items.Add(new DataProviderItemViewModel(model, change.Item.Current, SpeedChartEnabled));
                        } else {
                            if (change.Reason == ListChangeReason.Remove) {
                                Items.Remove(item);
                                item.Dispose();
                                continue;
                            }

                            item.DataProvider.State = change.Item.Current.State.Value;
                            if (item.DataProvider.State != Shared.Abstractions.DataProviderState.INACTIVE)
                                item.DataProvider.Latency = Shared.Utils.Converters.ToAgoString(change.Item.Current.AttachedDaemonService.Latency.Value);
                            else
                                item.DataProvider.Latency = "?";
                        }
                    }
                })));

            TorrentPolling.Torrents.Changed += TorrentPolling_TorrentBatchChange;
            AddDisposable(Disposable.Create(() => TorrentPolling.Torrents.Changed -= TorrentPolling_TorrentBatchChange));
            AddDisposable(Disposable.Create(() => {
                foreach (var item in Items)
                    item.Dispose();
            }));

            if (SpeedChartEnabled) {
                SpeedChartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                SpeedChartTimer.Tick += (_, _) => {
                    if (!Items.Any(i => i.DataProvider.State != Shared.Abstractions.DataProviderState.INACTIVE))
                        return;

                    var now = DateTime.Now;
                    foreach (var item in Items)
                        item.AdvanceXAxisWindow(now);
                };
                SpeedChartTimer.Start();
                AddDisposable(Disposable.Create(() => SpeedChartTimer.Stop()));
            }
        }

        private void TorrentPolling_TorrentBatchChange(object? sender, TorrentStoreChangeSet e)
        {
            var vm = Items.FirstOrDefault(x => x.DataProvider.InstanceId == e.DataProvider.Host.InstanceId);

            Debug.Assert(vm != null);
            if (vm == null)
                return;

            vm.ApplyTorrentsChanges(e);
        }
    }

    public partial class DataProviderItemViewModel : ObservableViewModel
    {
        private enum SPEED_CHART_MODE
        {
            DOWNLOAD,
            UPLOAD
        }

        private static readonly TimeSpan SPEED_CHART_WINDOW = TimeSpan.FromMinutes(2);

        private readonly record struct TorrentStats(ulong DLSpeed, ulong UPSpeed, bool Active);
        private readonly Dictionary<Models.Torrent, TorrentStats> PreviousTorrentStats = new(ReferenceEqualityComparer.Instance);

        private ulong DLSpeed;
        private ulong UPSpeed;
        private uint ActiveTorrentCount;

        private SPEED_CHART_MODE? CurrentSpeedChartMode;

        private readonly Axis SpeedChartXAxis;

        private readonly Axis SpeedChartYAxis;

        private double SpeedChartYAxisTopValue = 1;

        public Models.DataProvider DataProvider { get; }

        public RTSharpDataProvider DpInstance { get; }

        public IBrush PluginColor { get; }

        public bool CanShowLogs => DpInstance.Instance.Capabilities.GetLogs;

        [RelayCommand]
        private void ShowLogs()
        {
            var view = new Views.DataProviderLogsView {
                DataContext = new DataProviderLogsViewModel(DpInstance)
            };
            Views.Util.DockUtils.SpawnToolWindow(view);
        }

        public ObservableCollection<DateTimePoint> DLSpeedChartValues { get; } = new();

        public ObservableCollection<DateTimePoint> UPSpeedChartValues { get; } = new();

        public ISeries[] SpeedChartSeries { get; }

        public ICartesianAxis[] SpeedChartXAxes { get; }

        public ICartesianAxis[] SpeedChartYAxes { get; }

        public bool SpeedChartEnabled { get; }

        [ObservableProperty]
        public partial string SpeedChartTopLabel { get; set; } = String.Empty;

        [ObservableProperty]
        public partial bool SpeedChartTopLabelVisible { get; set; }

        public DataProviderItemViewModel(Models.DataProvider Dp, RTSharpDataProvider DpInstance, bool SpeedChartEnabled)
        {
            DataProvider = Dp;
            this.DpInstance = DpInstance;
            this.SpeedChartEnabled = SpeedChartEnabled;
            PluginColor = new SolidColorBrush(Color.Parse(DpInstance.Config.Plugin.Color ?? "#000000"));

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
                new LineSeries<DateTimePoint> {
                    Name = "DL",
                    Values = DLSpeedChartValues,
                    Fill = dlSpeedFill,
                    Stroke = dlSpeedPaint,
                    GeometrySize = 0,
                    LineSmoothness = 1,
                    AnimationsSpeed = TimeSpan.FromMilliseconds(120),
                    DataPadding = new LvcPoint(0, 0)
                },
                new LineSeries<DateTimePoint> {
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

            var now = DateTime.Now;

            SpeedChartXAxis = new Axis {
                IsVisible = false,
                UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                MinLimit = (now - SPEED_CHART_WINDOW).Ticks,
                MaxLimit = now.Ticks,
                SeparatorsPaint = axisPaint,
                Padding = new Padding(0)
            };

            SpeedChartXAxes = [SpeedChartXAxis];

            SpeedChartYAxis = new Axis {
                IsVisible = false,
                Position = AxisPosition.End,
                MinLimit = 0,
                MaxLimit = SpeedChartYAxisTopValue,
                MinStep = SpeedChartYAxisTopValue,
                ForceStepToMin = true,
                ShowSeparatorLines = false,
                SeparatorsPaint = axisPaint,
                Padding = new Padding(0)
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
                AddSpeedChartSample(DLSpeed, UPSpeed, DateTime.Now);
            }
        }

#if DEBUG
        private void VerifyAgainstSnapshot()
        {
            var expectedDLSpeed = 0UL;
            var expectedUPSpeed = 0UL;
            var expectedActiveCount = 0U;

            foreach (var t in TorrentPolling.Torrents.GetSnapshot()) {
                if (t.DataOwner.Host.InstanceId != DataProvider.InstanceId)
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

        public void AddSpeedChartSample(ulong DLSpeed, ulong UPSpeed, DateTime When)
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
                DLSpeedChartValues.Add(new DateTimePoint(When, value));
                UPSpeedChartValues.Add(new DateTimePoint(When, null));
            } else {
                DLSpeedChartValues.Add(new DateTimePoint(When, null));
                UPSpeedChartValues.Add(new DateTimePoint(When, value));
            }

            CurrentSpeedChartMode = chartMode;
            RemoveOldChartValues(When);
            UpdateYAxisScale();
        }

        private void FillSwitchPoint(SPEED_CHART_MODE Mode)
        {
            var lastIndex = DLSpeedChartValues.Count - 1;
            if (lastIndex < 0) {
                return;
            }

            if (Mode == SPEED_CHART_MODE.DOWNLOAD) {
                DLSpeedChartValues[lastIndex].Value = UPSpeedChartValues[lastIndex].Value;
            } else {
                UPSpeedChartValues[lastIndex].Value = DLSpeedChartValues[lastIndex].Value;
            }
        }

        public void AdvanceXAxisWindow(DateTime When)
        {
            SpeedChartXAxis.MaxLimit = When.Ticks;
            SpeedChartXAxis.MinLimit = (When - SPEED_CHART_WINDOW).Ticks;
            RemoveOldChartValues(When);
        }

        private void RemoveOldChartValues(DateTime When)
        {
            var cutoff = (When - SPEED_CHART_WINDOW).Ticks;

            while (DLSpeedChartValues.Count > 1 && DLSpeedChartValues[1].DateTime.Ticks < cutoff) {
                DLSpeedChartValues.RemoveAt(0);
                UPSpeedChartValues.RemoveAt(0);
            }
        }

        private void UpdateYAxisScale()
        {
            double maxValue = 0;
            foreach (var v in DLSpeedChartValues)
                if (v.Value > maxValue)
                    maxValue = v.Value.Value;
            foreach (var v in UPSpeedChartValues)
                if (v.Value > maxValue)
                    maxValue = v.Value.Value;

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
