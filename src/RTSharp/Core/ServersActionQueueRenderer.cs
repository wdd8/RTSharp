using Avalonia;
using Avalonia.Threading;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Core
{
    public class ServersActionQueueRenderer : DefaultActionQueueRenderer
    {
        public override StyledElement Display { get; }

        public List<ActionQueueAction> _actions = new();

        private Views.ServerActionQueue ActionQueueView;
        private ViewModels.ServerActionQueueViewModel ActionQueueVm;

        public ServersActionQueueRenderer(string ServerName)
        {
            Dispatcher.UIThread.Invoke(() => {
                ActionQueueView = new Views.ServerActionQueue() {
                    DataContext = ActionQueueVm = new ViewModels.ServerActionQueueViewModel() {
                        DisplayName = $"{ServerName}"
                    }
                };
            });
            Display = ActionQueueView;
        }

        public async Task TrackServerActions(IDaemonService DaemonService)
        {
            static void updateAction(ActionQueueAction action, ScriptProgressState state)
            {
                action.ProgressString = state.StateData ?? "";
                action.ProgressDone = state.Progress ?? 0;
                action.State = state.State switch {
                    TASK_STATE.WAITING => ACTION_STATE.WAITING,
                    TASK_STATE.RUNNING => ACTION_STATE.RUNNING,
                    TASK_STATE.DONE => ACTION_STATE.DONE,
                    TASK_STATE.FAILED => ACTION_STATE.FAILED,
                    _ => ACTION_STATE.CANCELLED
                };
            }

            while (true) {
                var channel = DaemonService.StreamScriptsStatus(default);
                await foreach (var state in channel.ReadAllAsync()) {
                    var action = FindAction(state.Id);
                    if (action != null) {

                    } else {
                        static ActionQueueAction create(ActionQueueAction parent, ScriptProgressState[] states)
                        {
                            foreach (var childState in states) {
                                var child = parent.CreateCosmeticChild(childState.Id, childState.Text);
                                updateAction(child, childState);
                                if (childState.Chain != null) {
                                    create(child, childState.Chain);
                                }
                            }

                            return parent;
                        }
                        var rootAction = ActionQueueAction.NewCosmetic(state.Id, state.Text);
                        updateAction(rootAction, state);
                        AddAction(create(rootAction, [state]));
                    }
                }
            }
        }

        private string RenderActionQueue(IEnumerable<ActionQueueAction> Actions, StringBuilder Builder, int Identation = 0)
        {
            foreach (var action in Actions) {
                var running = action.State switch {
                    ACTION_STATE.WAITING => "[waiting]",
                    ACTION_STATE.RUNNING => "[running]",
                    ACTION_STATE.CANCELLED => "[cancelled]",
                    ACTION_STATE.FAILED => "[failed]",
                    ACTION_STATE.DONE => "[done]",
                    _ => throw new ArgumentOutOfRangeException()
                };
                Builder.AppendLine(new string(Enumerable.Repeat('\t', Identation).ToArray()) + " " + running + " " + action.Name);
                if (action.ProgressDone > 0) {
                    Builder.AppendLine(new string(Enumerable.Repeat('\t', Identation).ToArray()) + $" {Math.Round(action.ProgressDone, 2)}% {action.ProgressString}");
                }

                if (action.ChildActions.Any()) {
                    RenderActionQueue(action.ChildActions, Builder, Identation + 1);
                }
            }

            return Builder.ToString();
        }

        public override void RenderActionQueue(IEnumerable<ActionQueueAction> Actions)
        {
            ActionQueueVm.ActionQueueString = RenderActionQueue(Actions, new StringBuilder());
        }

        public override void ActionCreated(ActionQueueAction Action) => ActionQueueVm.ActionsInQueue++;
        public override void ActionRun(ActionQueueAction Action) { }
        public override void ActionCompleted(ActionQueueAction Action) => ActionQueueVm.ActionsInQueue--;
        public override void ActionErrored(ActionQueueAction Action)
        {
            ActionQueueVm.ErroredActions++;
            ActionQueueVm.ActionsInQueue--;
        }
        public override void ActionExpired(ActionQueueAction Action) { }
    }
}