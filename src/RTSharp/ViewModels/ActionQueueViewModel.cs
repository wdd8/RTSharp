using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;

using Dock.Model.Mvvm.Controls;

using RTSharp.Core;
using RTSharp.Models;

namespace RTSharp.ViewModels
{
    public class ActionQueueViewModel : Document, IDocumentWithIcon
    {
        public ObservableCollection<ActionQueueEntry> ActionQueue => (ObservableCollection<ActionQueueEntry>)Context!;

        public ObservableCollection<StyledElement> QueueDisplay => new ObservableCollection<StyledElement>(ActionQueue.Select(x => x.Queue.Display));

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-bolt");
    }
}
