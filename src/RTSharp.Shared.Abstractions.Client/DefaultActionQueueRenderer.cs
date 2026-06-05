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
                Id = action.Id,
                Name = action.Name,
                Depth = depth,
                State = action.State,
                ProgressDone = action.ProgressDone,
                ProgressString = action.ProgressString ?? ""
            });
            if (action.ChildActions.Any())
                FlattenActions(action.ChildActions, result, depth + 1);
        }
    }

    public virtual void RenderActionQueue(params IEnumerable<ActionQueueAction> Actions)
    {
        var desired = new List<DefaultActionQueueActionViewModel>();
        FlattenActions(Actions, desired, 0);
        Dispatcher.UIThread.Post(() => SyncWithVm(desired));
    }

    private void SyncWithVm(List<DefaultActionQueueActionViewModel> Actual)
    {
        var existingActions = ActionQueueVm.Actions;

        var actualIds = new HashSet<Guid>(Actual.Count);
        foreach (var x in Actual)
            actualIds.Add(x.Id);

        // Deletes
        for (int x = existingActions.Count - 1; x >= 0; x--) {
            if (!actualIds.Contains(existingActions[x].Id))
                existingActions.RemoveAt(x);
        }

        var byId = new Dictionary<Guid, DefaultActionQueueActionViewModel>(existingActions.Count);
        foreach (var x in existingActions)
            byId[x.Id] = x;

        // Updates and inserts
        for (int x = 0; x < Actual.Count; x++) {
            var item = Actual[x];

            if (byId.TryGetValue(item.Id, out var existing)) {
                int currentIndex = existingActions.IndexOf(existing);
                if (currentIndex != x)
                    existingActions.Move(currentIndex, x);

                existing.State = item.State;
                existing.ProgressDone = item.ProgressDone;
                existing.ProgressString = item.ProgressString;
            } else {
                existingActions.Insert(x, item);
                byId[item.Id] = item;
            }
        }
    }

    public virtual void ActionCreated(ActionQueueAction Action) { }
    public virtual void ActionRun(ActionQueueAction Action) { }
    public virtual void ActionCompleted(ActionQueueAction Action) { }
    public virtual void ActionErrored(ActionQueueAction Action) { }
    public virtual void ActionExpired(ActionQueueAction Action) { }

    private void TrackAction(ActionQueueAction Action)
    {
        ActionCreated(Action);

        lock (ActionsLock) {
            ActionQueueVm!.ActionsInQueue++;
            RenderActionQueue(Actions);
        }
        Action.ProgressChanged += (sender, e) => {
            lock (ActionsLock) {
                RenderActionQueue(Actions);
            }
        };

        foreach (var child in Action.ChildActions) {
            TrackAction(child);
        }

        Action.OnRun(task => {
            lock (ActionsLock) {
                RenderActionQueue(Actions);
            }
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
                lock (ActionsLock) {
                    RenderActionQueue(_actions);
                }
            });
        }

        Action.OnFail((ex, task) => {
            lock (ActionsLock) {
                RenderActionQueue(Actions);
                ActionQueueVm.ErroredActions++;
                ActionQueueVm.ActionsInQueue--;
            }
            queueActionExpiration(Action);
            ActionErrored(Action);
        });
        Action.OnDone((result, task) => {
            lock (ActionsLock) {
                RenderActionQueue(Actions);
                ActionQueueVm!.ActionsInQueue--;
            }
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
