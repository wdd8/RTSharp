using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Model.Core;

using RTSharp.ViewModels.Options;
using RTSharp.ViewModels.TorrentListing;
using RTSharp.Views;
using RTSharp.Views.Options;
using RTSharp.Views.Tools;

namespace RTSharp.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public IFactory factory;

        [ObservableProperty]
        public IDock layout;

        [ObservableProperty]
        public TorrentListingViewModel torrentListing;

        [ObservableProperty]
        public string currentView;

        public ObservableCollection<MenuItem> MenuItems { get; } = new();

        public ObservableCollection<Plugin.PluginInstance> Plugins => Plugin.Plugins.LoadedPlugins;

        public ICommand CmdOptionsClick { get; }

        public Func<PluginsViewModel, Task> ShowPluginsDialog { get; set; }

        public Func<ServersWindowViewModel, Task> ShowServersDialog { get; set; }

        [ObservableProperty]
        public string stringFilter;

        public MainWindowViewModel()
        {
            Factory = new MainDockFactory();
            Layout = Factory.CreateLayout()!;
            if (Layout is { }) {
                Factory?.InitLayout(Layout);
            }
        }

        [RelayCommand]
        public async Task AboutFrameworkClick()
        {
            var wnd = new AboutAvaloniaDialog();
            await wnd.ShowDialog(App.MainWindow);
        }

        [RelayCommand]
        public void OptionsClick()
        {
            var optionsWindow = new OptionsWindow() {
                DataContext = new()
            };
            optionsWindow.Show();
        }

        [RelayCommand]
        public void AboutClick()
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Show();
        }

        [RelayCommand]
        public void AddTorrentClick()
        {
            var addTorrentWindow = new AddTorrentWindow() {
                ViewModel = new()
            };
            addTorrentWindow.Show();
        }

        [RelayCommand]
        public void PluginsClick()
        {
            var vm = new PluginsViewModel();
            ShowPluginsDialog(vm);
        }

        [RelayCommand]
        public void ServersClick()
        {
            var vm = new ServersWindowViewModel();
            ShowServersDialog(vm);
        }

        [RelayCommand]
        public void TorrentCreatorClick()
        {
            var torrentCreatorWindow = new TorrentCreatorWindow() {
                ViewModel = new()
            };
            torrentCreatorWindow.Show();
        }

        public void CloseLayout()
        {
            if (Layout is IDock dock) {
                if (dock.Close.CanExecute(null)) {
                    dock.Close.Execute(null);
                }
            }
        }
    }
}
