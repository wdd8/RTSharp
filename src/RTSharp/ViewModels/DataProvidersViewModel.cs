using Avalonia.Media;

using RTSharp.Core;
using RTSharp.Plugin;

using System.Threading;
using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;

namespace RTSharp.ViewModels
{
    public class DataProvidersViewModel : Document, IDocumentWithIcon
    {
        public ObservableCollection<DataProvider> Items => (ObservableCollection<DataProvider>)Context!;

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-network-wired");

        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public DataProvidersViewModel()
        {
        }

        public override bool OnClose()
        {
            base.OnClose();
            Cancellation.Cancel();

            return true;
        }
    }
}
