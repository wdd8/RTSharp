using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.ViewModels;

public partial class DataProviderLogsViewModel : ObservableViewModel
{
    private const int HISTORY_LINES = 200;
    private const int MAX_ENTRIES = 5000;

    private readonly CancellationTokenSource Cts = new();

    public string DisplayName { get; }

    public ObservableCollection<DataProviderDaemonLogEntry> LogEntries { get; } = new();

    public DataProviderLogsViewModel(RTSharpDataProvider DataProvider)
    {
        DisplayName = DataProvider.Config.Plugin.Name;

        AddDisposable(Disposable.Create(Cts.Cancel));

        _ = Stream(DataProvider);
    }

    private async Task Stream(RTSharpDataProvider DataProvider)
    {
        try {
            var reader = DataProvider.Instance.StreamLogs(HISTORY_LINES, Cts.Token);

            await foreach (var entry in reader.ReadAllAsync(Cts.Token)) {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    LogEntries.Add(entry);
                    while (LogEntries.Count > MAX_ENTRIES)
                        LogEntries.RemoveAt(0);
                });
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            await Dispatcher.UIThread.InvokeAsync(() =>
                LogEntries.Add(new DataProviderDaemonLogEntry(DateTime.Now, LogSeverity.Error, "Log stream ended: " + ex.Message)));
        }
    }

    [RelayCommand]
    private void Clear() => LogEntries.Clear();
}
