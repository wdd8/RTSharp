using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Controls;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.Core;

public class ServersActionQueueRenderer(string ServerName, IDaemonService DaemonService) : DefaultActionQueueRenderer(ServerName, BuiltInIcon.Get(BuiltInIcons.SERVER))
{
    private ConcurrentDictionary<Guid, TaskCompletionSource> ActionsTasks = new();

    public async Task TrackServerActions()
    {
        while (true) {
            try {
                var channel = DaemonService.StreamScriptsStatus(default);
                await foreach (var update in channel.ReadAllAsync()) {
                    if (update.FullUpdate) {
                        ApplyFullUpdate(update);
                    } else {
                        ApplyPartialUpdate(update);
                    }
                }
            } finally {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private void ApplyFullUpdate(ScriptSessionStateUpdate update)
    {
        var sessionIds = update.Sessions.Select(x => x.Id).ToHashSet();

        foreach (var session in update.Sessions) {
            var rootAction = FindAction(session.Id);
            if (rootAction == null) {
                var tcs = ActionsTasks[session.Progress.Id] = new TaskCompletionSource();
                rootAction = ActionQueueAction.New(session.Id, session.Name, _ => tcs.Task);
                AddAction(rootAction);
                rootAction.RunAction();
                ChangeProgress(rootAction, session.Progress);
                rootAction.Name = session.Name;
                PopulateChain(rootAction, session.Progress.Chain ?? []);
            } else {
                UpdateAction(rootAction, session.Progress);
            }
        }
    }

    private void ApplyPartialUpdate(ScriptSessionStateUpdate update)
    {
        if (update.State == null) {
            return;
        }

        var action = FindAction(update.State.Id);
        if (action != null) {
            UpdateAction(action, update.State);
        } else {
            if (update.ParentStateId == null) {
                return;
            }

            var parent = FindAction(update.ParentStateId.Value);
            if (parent == null) {
                return;
            }

            var tcs = ActionsTasks[update.State.Id] = new TaskCompletionSource();
            var child = parent.CreateChild(update.State.Id, update.State.Text, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (_, _) => tcs.Task);
            child.RunAction();
            UpdateAction(child, update.State);
        }
    }

    public override void ActionExpired(ActionQueueAction Action)
    {
        _ = DaemonService.RemoveScriptSession(Action.Id);
    }

    private void ChangeProgress(ActionQueueAction action, ScriptProgressState state)
    {
        TaskCompletionSource? getTcs()
        {
            if (!ActionsTasks.TryGetValue(state.Id, out var tcs))
                return null;
            if (tcs.Task.IsCompleted)
                return null;

            return tcs;
        }

        if (state.State == TASK_STATE.DONE) {
            getTcs()?.SetResult();
        } else if (state.State == TASK_STATE.FAILED) {
            getTcs()?.SetException(new Exception(state.Text));
        }

        action.ChangeProgress(state.State switch {
            TASK_STATE.WAITING => ACTION_STATE.WAITING,
            TASK_STATE.RUNNING => ACTION_STATE.RUNNING,
            TASK_STATE.DONE => ACTION_STATE.DONE,
            TASK_STATE.FAILED => ACTION_STATE.FAILED,
            _ => ACTION_STATE.CANCELLED
        }, state.Progress ?? 0, state.Text);
    }

    private void PopulateChain(ActionQueueAction parent, ScriptProgressState[] states)
    {
        foreach (var childState in states) {
            var tcs = ActionsTasks[childState.Id] = new TaskCompletionSource();
            var child = parent.CreateChild(childState.Id, childState.Text, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (_, _) => tcs.Task);
            ChangeProgress(child, childState);

            if (childState.Chain != null) {
                PopulateChain(child, childState.Chain);
            }
        }
    }

    private void UpdateAction(ActionQueueAction action, ScriptProgressState state)
    {
        ChangeProgress(action, state);

        if (state.Chain == null) {
            return;
        }

        foreach (var childState in state.Chain) {
            var child = FindAction(childState.Id);
            if (child == null) {
                var tcs = ActionsTasks[childState.Id] = new TaskCompletionSource();
                child = action.CreateChild(childState.Id, childState.Text, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (_, _) => tcs.Task);
                child.RunAction();
            }

            UpdateAction(child, childState);
        }
    }
}
