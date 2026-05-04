using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;


using RTSharp.Core;
using RTSharp.Models;

namespace RTSharp.ViewModels;

public class ActionQueuesViewModel : ObservableObject
{
    public ObservableCollection<ActionQueueEntry> ActionQueue => Core.ActionQueue.ActionQueues;

    public ObservableCollection<StyledElement> QueueDisplay => new ObservableCollection<StyledElement>(ActionQueue.Select(x => x.Queue.Display));
}
