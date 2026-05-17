using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using RTSharp.Models;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ViewModels;

public partial class ActionQueuesViewModel : ObservableObject
{
    public SourceList<ActionQueueEntry> ActionQueue => Core.ActionQueue.ActionQueues;

    [ObservableProperty]
    public partial ReadOnlyObservableCollection<IActionQueueRenderer> QueueRenderers { get; set; }

    public ActionQueuesViewModel()
    {
        ActionQueue.Connect().ObserveOn(AvaloniaSynchronizationContext.Current).Transform(x => x.Queue).Bind(out var observable).Subscribe();
        QueueRenderers = observable;
    }
}
