using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;

using CommunityToolkit.Mvvm.Input;

using Dock.Model.Mvvm.Controls;

using RTSharp.Core;
using RTSharp.Models;

namespace RTSharp.ViewModels
{
    public partial class LogEntriesViewModel : Document, IContextPopulatedNotifyable, IDocumentWithIcon
    {
        public ObservableCollection<LogEntry> LogEntries => (ObservableCollection<LogEntry>)Context!;

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
    }

    public static class ExampleLogEntriesViewModel
    {
        public static LogEntriesViewModel ViewModel { get; } = new LogEntriesViewModel() {
            Context = new Models.ExampleLogEntries()
        };
    }
}
