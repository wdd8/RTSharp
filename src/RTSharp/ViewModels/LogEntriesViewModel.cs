using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.Input;

using RTSharp.Models;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ViewModels;

public partial class LogEntriesViewModel : ObservableViewModel
{
    public ObservableCollection<LogEntry> LogEntries => Core.LogWindowSink.LogEntries;

    public Action? ScrollToBottom;

    public Func<string, Task>? SetClipboardAsync;

    private bool ContextPopulated;

    [RelayCommand]
    public async Task CopyException(IList In)
    {
        var logEntries = In.Cast<LogEntry>().ToArray();

        if (logEntries.Length != 1 || SetClipboardAsync == null)
            return;

        await SetClipboardAsync(logEntries.First().Exception!.ToString());
    }

    private void EvLogEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (ScrollToBottom != null)
            ScrollToBottom();
    }

    public override void OnContextPopulated()
    {
        if (ContextPopulated)
            return;

        ContextPopulated = true;
        LogEntries.CollectionChanged += EvLogEntriesChanged;
        AddDisposable(Disposable.Create(() => LogEntries.CollectionChanged -= EvLogEntriesChanged));
    }
}

public static class ExampleLogEntriesViewModel
{
    public static LogEntriesViewModel ViewModel { get; } = new LogEntriesViewModel();
}
