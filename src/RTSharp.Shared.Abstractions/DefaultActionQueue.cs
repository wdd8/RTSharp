﻿using Avalonia;

namespace RTSharp.Shared.Abstractions
{
    public abstract class DefaultActionQueue : IActionQueue
    {
        public abstract StyledElement Display { get; }

        private List<ActionQueueAction> _actions = new();
        public IReadOnlyCollection<ActionQueueAction> Actions => _actions;

        public abstract void RenderActionQueue(IEnumerable<ActionQueueAction> Actions);

        public abstract void ActionCreated(ActionQueueAction Action);

        public abstract void ActionRun(ActionQueueAction Action);

        public abstract void ActionCompleted(ActionQueueAction Action);

        public abstract void ActionErrored(ActionQueueAction Action);

        public abstract void ActionExpired(ActionQueueAction Action);

        private void TrackAction(ActionQueueAction Action, bool Child)
        {
            ActionCreated(Action);
            if (!Child) {
                _actions.Add(Action);
            }
            RenderActionQueue(Actions);
            Action.ProgressChanged += (sender, e) => {
                RenderActionQueue(Actions);
            };

            foreach (var child in Action.ChildActions) {
                TrackAction(child, true);
            }

            Action.OnRun(task => {
                RenderActionQueue(Actions);
                ActionRun(Action);
            });

            void queueActionExpiration(ActionQueueAction Action)
            {
                bool allCompleted(ActionQueueAction action)
                {
                    foreach (var child in action.ChildActions) {
                        if (!allCompleted(child)) {
                            return false;
                        }
                    }

                    return action.IsCompleted;
                }

                while (Action.Parent != null)
                    Action = Action.Parent;

                if (!allCompleted(Action))
                    return;

                _ = Task.Delay(5000).ContinueWith(_ => {
                    _actions.Remove(Action);
                    ActionExpired(Action);
                    RenderActionQueue(Actions);
                });
            }

            Action.OnFail((ex, task) => {
                RenderActionQueue(Actions);
                ActionErrored(Action);
                queueActionExpiration(Action);
            });
            Action.AfterRun((result, task) => {
                RenderActionQueue(Actions);
                ActionCompleted(Action);
                queueActionExpiration(Action);
            });
        }

        public void AddAction(ActionQueueAction Action) => TrackAction(Action, false);
    }
}
