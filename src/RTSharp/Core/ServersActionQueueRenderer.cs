using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Controls;

using System;
using System.Collections.Concurrent;
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
                    ApplyUpdate(update);
                }
            } finally {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private void ApplyUpdate(ScriptSessionUpdate In)
    {
        var action = FindAction(In.State.Id);
        if (action != null) {
            if (In.ParentStateId == null && In.SessionName != null) {
                action.Name = In.SessionName;
            }

            UpdateAction(action, In.State);
            return;
        }

        if (In.ParentStateId == null) {
            var sessionName = In.SessionName ?? In.State.Text;
            var tcs = ActionsTasks[In.State.Id] = new TaskCompletionSource();
            var rootAction = ActionQueueAction.New(In.SessionId, sessionName, _ => tcs.Task);
            AddAction(rootAction);
            rootAction.RunAction();
            UpdateAction(rootAction, In.State);
            return;
        }

        var parent = FindAction(In.ParentStateId.Value);
        if (parent == null) {
            return;
        }

        var childTcs = ActionsTasks[In.State.Id] = new TaskCompletionSource();
        var child = parent.CreateChild(In.State.Id, In.State.Text, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (_, _) => childTcs.Task);
        child.RunAction();
        UpdateAction(child, In.State);
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
