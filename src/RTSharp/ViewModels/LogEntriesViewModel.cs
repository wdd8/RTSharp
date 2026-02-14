using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using NP.UniDockService;

using RTSharp.Core;
using RTSharp.Models;
using RTSharp.Shared.Controls;

namespace RTSharp.ViewModels
{
    public class DockLogEntriesViewModel : DockItemViewModel<LogEntriesViewModel> { }

    public partial class LogEntriesViewModel : ObservableObject, IDockable, IContextPopulatedNotifyable
    {
        public ObservableCollection<LogEntry> LogEntries => Core.LogWindowSink.LogEntries;

        public Action? ScrollToBottom;

        public Func<string, Task>? SetClipboardAsync;


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

        public void OnContextPopulated()
        {
            LogEntries.CollectionChanged += EvLogEntriesChanged;
        }

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-calendar-days");

        public string HeaderName => "Log";
    }

    public static class ExampleLogEntriesViewModel
    {
        public static LogEntriesViewModel ViewModel { get; } = new LogEntriesViewModel();
    }
}
