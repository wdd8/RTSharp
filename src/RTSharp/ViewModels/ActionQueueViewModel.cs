using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using NP.Ava.UniDockService;

using RTSharp.Core;
using RTSharp.Models;
using RTSharp.Shared.Controls;

namespace RTSharp.ViewModels
{
    public class DockActionQueueViewModel : DockItemViewModel<ActionQueueViewModel> { }

    public class ActionQueueViewModel : ObservableObject, IDockable
    {
        public ObservableCollection<ActionQueueEntry> ActionQueue => Core.ActionQueue.ActionQueues;

        public ObservableCollection<StyledElement> QueueDisplay => new ObservableCollection<StyledElement>(ActionQueue.Select(x => x.Queue.Display));

        public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-bolt");

        public string HeaderName => "Action queue";
    }
}
