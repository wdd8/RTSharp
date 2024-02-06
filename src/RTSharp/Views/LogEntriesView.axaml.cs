using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using RTSharp.Models;
using RTSharp.ViewModels;
using Serilog.Events;
using System.Threading.Tasks;
using RTSharp.Shared.Controls;

namespace RTSharp.Views
{
    public partial class LogEntriesView : VmUserControl<LogEntriesViewModel>
    {
        public LogEntriesView()
        {
            InitializeComponent();

            BindViewModelActions(vm => {
                vm!.ScrollToBottom = EvScrollToBottom;
                vm!.SetClipboardAsync = EvSetClipboard;
            }, vm => {
                vm!.ScrollToBottom = null;
                vm!.SetClipboardAsync = null;
            });
        }

        private readonly ISolidColorBrush DarkYellow = new ImmutableSolidColorBrush(Color.FromUInt32(0xFF9B870C));

        private void EvLoadingRow(object sender, DataGridRowEventArgs e)
        {
            var dataObject = (LogEntry)e.Row.DataContext!;
            switch (dataObject.LogLevel) {
                case LogEventLevel.Verbose:
                    e.Row.Background = Brushes.DarkGray;
                    break;
                case LogEventLevel.Debug:
                    e.Row.Background = Brushes.Gray;
                    break;
                case LogEventLevel.Information:
                    e.Row.Background = Brushes.Black;
                    break;
                case LogEventLevel.Warning:
                    e.Row.Background = DarkYellow;
                    break;
                case LogEventLevel.Error:
                    e.Row.Background = Brushes.Red;
                    break;
                case LogEventLevel.Fatal:
                    e.Row.Background = Brushes.DarkRed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void EvScrollToBottom()
        {
            if (!MainGrid.IsFocused && ViewModel!.LogEntries.Count > 2) {
                MainGrid.ScrollIntoView(ViewModel!.LogEntries[^2], null);
            }
        }

        private void EvGotFocus(object sender, GotFocusEventArgs e)
        {

        }

        private void EvLostFocus(object sender, RoutedEventArgs e)
        {

        }

        public async Task EvSetClipboard(string Input)
        {
            var clipboardObj = App.MainWindow.Clipboard;
            if (clipboardObj != null)
                await clipboardObj.SetTextAsync(Input);
        }
    }
}
