using Avalonia.Media;

using RTSharp.Core;
using RTSharp.Plugin;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NP.Ava.UniDockService;
using RTSharp.Shared.Controls;

namespace RTSharp.ViewModels
{
    public class DockDataProvidersViewModel : DockItemViewModel<DataProvidersViewModel> { }

    public class DataProvidersViewModel : ObservableObject, IDockable
    {
        public ObservableCollection<DataProvider> Items => Plugin.Plugins.DataProviders;

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-network-wired");

        public string HeaderName => "Data providers";

        public DataProvidersViewModel()
        {
        }

        // TODO: cancel refreshing on close
    }
}
