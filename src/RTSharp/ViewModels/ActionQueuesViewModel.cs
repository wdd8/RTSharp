using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using NP.UniDockService;

using RTSharp.Core;
using RTSharp.Models;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ViewModels;

public class DockActionQueuesViewModel : DockItemViewModel<ActionQueuesViewModel> { }

public class ActionQueuesViewModel : ObservableObject, IDockable
{
    public ObservableCollection<ActionQueueEntry> ActionQueue => Core.ActionQueue.ActionQueues;

    public ObservableCollection<StyledElement> QueueDisplay => new ObservableCollection<StyledElement>(ActionQueue.Select(x => x.Queue.Display));

    public Geometry Icon { get; } = FontAwesomeIcons.Get("fa-solid fa-bolt");

    public string HeaderName => "Action queue";
}
