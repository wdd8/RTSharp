using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

using RTSharp.Shared.Abstractions.Client.ViewModels;
using RTSharp.Shared.Abstractions.Client.Views;

namespace RTSharp.Shared.Abstractions.Client;

public class DefaultActionQueueRenderer : IActionQueueRenderer
{
    private readonly List<ActionQueueAction> _actions = [];
    private readonly Lock ActionsLock = new();

    public IReadOnlyCollection<ActionQueueAction> Actions {
        get {
            lock (ActionsLock) {
                return [.. _actions];
            }
        }
    }

    public virtual StyledElement Display => CreateDisplay();

    protected DefaultActionQueueViewModel ActionQueueVm { get; private set; }

    public DefaultActionQueueRenderer(string DisplayName, IImage? Icon = null)
    {
        ActionQueueVm = new DefaultActionQueueViewModel {
            DisplayName = DisplayName,
            Icon = Icon
        };
    }

    public virtual StyledElement CreateDisplay()
    {
        return new DefaultActionQueueView {
            DataContext = ActionQueueVm
        };
    }

    private static void FlattenActions(IEnumerable<ActionQueueAction> actions, List<DefaultActionQueueActionViewModel> result, int depth)
    {
        foreach (var action in actions) {
            result.Add(new DefaultActionQueueActionViewModel {
                Name = action.Name,
                State = action.State,
                ProgressDone = action.ProgressDone,
                ProgressString = action.ProgressString ?? "",
                Depth = depth
            });
            if (action.ChildActions.Any())
                FlattenActions(action.ChildActions, result, depth + 1);
        }
    }

    public virtual void RenderActionQueue(params IEnumerable<ActionQueueAction> Actions)
    {
        var list = new List<DefaultActionQueueActionViewModel>();
        FlattenActions(Actions, list, 0);
        Dispatcher.UIThread.Post(() => {
            ActionQueueVm.Actions.Clear();
            foreach (var item in list)
                ActionQueueVm.Actions.Add(item);
        });
    }

    public virtual void ActionCreated(ActionQueueAction Action) { }
    public virtual void ActionRun(ActionQueueAction Action) { }
    public virtual void ActionCompleted(ActionQueueAction Action) { }
    public virtual void ActionErrored(ActionQueueAction Action) { }
    public virtual void ActionExpired(ActionQueueAction Action) { }

    private void TrackAction(ActionQueueAction Action)
    {
        ActionCreated(Action);
        ActionQueueVm!.ActionsInQueue++;

        RenderActionQueue(Actions);
        Action.ProgressChanged += (sender, e) => {
            RenderActionQueue(Actions);
        };

        foreach (var child in Action.ChildActions) {
            TrackAction(child);
        }

        Action.OnRun(task => {
            RenderActionQueue(Actions);
            ActionRun(Action);
        });

        void queueActionExpiration(ActionQueueAction Action)
        {
            Action = Action.GetRootAction();

            if (!Action.IsCompleted)
                return;

            var delay = Math.Min(120, 8 + Action.GetChildActionCount() * 4);

            _ = Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ => {
                lock (ActionsLock) {
                    _actions.Remove(Action);
                }
                ActionExpired(Action);
                RenderActionQueue(_actions);
            });
        }

        Action.OnFail((ex, task) => {
            RenderActionQueue(Actions);
            ActionQueueVm.ErroredActions++;
            ActionQueueVm.ActionsInQueue--;
            queueActionExpiration(Action);
            ActionErrored(Action);
        });
        Action.OnDone((result, task) => {
            RenderActionQueue(Actions);
            ActionQueueVm!.ActionsInQueue--;
            queueActionExpiration(Action);
            ActionCompleted(Action);
        });
    }

    public void AddAction(ActionQueueAction Action)
    {
        lock (ActionsLock) {
            _actions.Add(Action);
        }
        TrackAction(Action);
    }

    private static ActionQueueAction? FindAction(IEnumerable<ActionQueueAction> Actions, Guid Id)
    {
        foreach (var action in Actions) {
            if (action.Id == Id)
                return action;
            var found = FindAction(action.ChildActions, Id);
            if (found != null)
                return found;
        }

        return null;
    }

    public ActionQueueAction? FindAction(Guid Id) => FindAction(_actions, Id);
}
