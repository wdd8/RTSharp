using Serilog;

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace RTSharp.Shared.Abstractions
{
    public enum ACTION_STATE
    {
        WAITING,
        RUNNING,
        CANCELLED,
        FAILED,
        DONE
    }

    public enum RUN_MODE
    {
        PARALLEL_DONT_WAIT_ON_PARENT,
        DEPENDS_ON_PARENT
    }

    public class ActionQueueAction<T> : ActionQueueAction
    {
        public T? GetResult() => RunningTask?.IsCompleted == true ? (T?)RunningTask.Result : default;

        internal ActionQueueAction(Guid Id, string Name, RUN_MODE RunMode, Func<ActionQueueAction<T>, Task<T>> Task, ActionQueueAction<T>? Parent) : base(Id, Name, RunMode, async (task) => { var res = await Task((ActionQueueAction<T>)task); return res; }, Parent)
        {
        }

        internal ActionQueueAction(ActionQueueAction In) : base(In.Id, In.Name, In.RunMode, In.FxCreateTask, In.Parent)
        {
        }

        public ActionQueueAction<TChild> CreateChild<TChild>(string Name, RUN_MODE RunMode, Func<ActionQueueAction<T>, ActionQueueAction<TChild>, Task<TChild>> Task)
        {
            async Task<TChild> task(ActionQueueAction child, ActionQueueAction parent)
            {
                var ret = await Task((ActionQueueAction<T>)parent, (ActionQueueAction<TChild>)child);
                return ret;
            }

            ActionQueueAction<TChild> child = null!;
            child = base.CreateChild<TChild>(Name, RunMode, task);
            return child;
        }

        public ActionQueueAction CreateChild(string Name, RUN_MODE RunMode, Func<ActionQueueAction<T>, ActionQueueAction, ValueTask> Task)
        {
            async Task<object?> task(ActionQueueAction parent, ActionQueueAction child)
            {
                await Task((ActionQueueAction<T>)parent, child);
                return null;
            }

            return base.CreateChild(Name, RunMode, task);
        }

        public new async Task<T?> RunAction() => (T?)await base.RunAction();
    }

    public record ProgressChangedEvArgs(ACTION_STATE State, float Percentage, string Message);

    public class ActionQueueAction
    {
        public ActionQueueAction? Parent { get; private set; }

        public Guid Id { get; init; }

        public string Name { get; set; }

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        public ACTION_STATE State { get; private set; } = ACTION_STATE.WAITING;

        public RUN_MODE? RunMode { get; private set; }

        public Func<ActionQueueAction, Task<object?>>? FxCreateTask { get; }

        public Task<object?>? RunningTask { get; private set; }

        public float ProgressDone { get; private set; }

        public string ProgressString { get; private set; }

        public event EventHandler<ProgressChangedEvArgs>? ProgressChanged;

        public ConcurrentBag<ActionQueueAction> ChildActions { get; } = [];

        private ActionQueueAction()
        {
        }

        protected ActionQueueAction(Guid Id, string Name, RUN_MODE? RunMode, Func<ActionQueueAction, Task<object?>>? FxCreateTask, ActionQueueAction? Parent)
        {
            this.Id = Id;
            this.Name = Name;
            this.FxCreateTask = FxCreateTask;
            this.Parent = Parent;
            this.RunMode = RunMode;
        }

        public bool IsCompleted => State == ACTION_STATE.CANCELLED || State == ACTION_STATE.FAILED || State == ACTION_STATE.DONE;

        public void ChangeProgress(ACTION_STATE State, float Percentage, string Message)
        {
            if (IsCompleted)
                return;

            ProgressDone = Percentage;
            ProgressString = Message;
            this.State = State;

            ProgressChanged?.Invoke(this, new(State, Percentage, Message));
        }

        public void ChangeProgress(float Percentage, string Message) => ChangeProgress(State, Percentage, Message);

        public void ChangeProgress(ACTION_STATE State) => ChangeProgress(State, ProgressDone, ProgressString);

        public static ActionQueueAction New(Guid Id, string Name, Func<ActionQueueAction, Task> FxCreateTask) =>
            new(Id, Name, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, async (task) => { await FxCreateTask(task); return null; }, null);

        public static ActionQueueAction New(string Name, Func<ActionQueueAction, Task> FxCreateTask) =>
            New(Guid.NewGuid(), Name, FxCreateTask);

        public static ActionQueueAction<T> New<T>(Guid Id, string Name, Func<ActionQueueAction<T>, Task<T>> FxCreateTask) =>
            new(Id, Name, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (task) => FxCreateTask(task), null);

        public static ActionQueueAction<T> New<T>(string Name, Func<ActionQueueAction<T>, Task<T>> FxCreateTask) =>
            New(Guid.NewGuid(), Name, FxCreateTask);

        public ActionQueueAction<TChild> CreateChild<TChild>(string Name, RUN_MODE RunMode, Func<ActionQueueAction, ActionQueueAction<TChild>, Task<TChild>> FxCreateTask) =>
            CreateChild(Guid.NewGuid(), Name, RunMode, FxCreateTask);

        public ActionQueueAction CreateChild(string Name, RUN_MODE RunMode, Func<ActionQueueAction, ActionQueueAction, Task> FxCreateTask) =>
            CreateChild(Guid.NewGuid(), Name, RunMode, FxCreateTask);

        public ActionQueueAction<TChild> CreateChild<TChild>(Guid Id, string Name, RUN_MODE RunMode, Func<ActionQueueAction, ActionQueueAction<TChild>, Task<TChild>> FxCreateTask)
        {
            var ret = new ActionQueueAction<TChild>(new ActionQueueAction(Id, Name, RunMode, async (task) => await FxCreateTask(this, (ActionQueueAction<TChild>)task), this));
            ChildActions.Add(ret);
            return ret;
        }

        public ActionQueueAction CreateChild(Guid Id, string Name, RUN_MODE RunMode, Func<ActionQueueAction, ActionQueueAction, Task> FxCreateTask)
        {
            var ret = new ActionQueueAction(Id, Name, RunMode, async (task) => { await FxCreateTask(this, task); return null; }, this);
            ChildActions.Add(ret);
            return ret;
        }

        internal readonly List<Action<Task<object?>>> OnRunFxs = new();
        public void OnRun(Action<Task<object?>> Fx)
        {
            OnRunFxs.Add(Fx);
        }

        internal readonly List<Action<object?, Task<object?>?>> OnDoneFxs = new();
        public void OnDone(Action<object?, Task<object?>?> Fx)
        {
            OnDoneFxs.Add(Fx);
        }

        internal readonly List<Action<Exception?, Task<object?>?>> OnFailFxs = new();
        public void OnFail(Action<Exception?, Task<object?>?> Fx)
        {
            OnFailFxs.Add(Fx);
        }

        public Task<object?> RunAction()
        {
            this.State = ACTION_STATE.RUNNING;
            RunningTask = FxCreateTask!(this);
            foreach (var fx in OnRunFxs)
                fx(RunningTask);

            RunningTask.ContinueWith(task => {
                this.State = ACTION_STATE.DONE;

                foreach (var fx in OnDoneFxs)
                    fx(task.Result, task);
                
                foreach (var childAction in ChildActions.Where(x => x.RunMode == RUN_MODE.DEPENDS_ON_PARENT)) {
                    childAction.RunAction();
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            RunningTask.ContinueWith(task => {
                this.State = ACTION_STATE.FAILED;

                foreach (var fx in OnFailFxs)
                    fx(task.Exception!, task);
                
                Log.Logger.Error(task.Exception, "Action failed");
            }, TaskContinuationOptions.OnlyOnFaulted);
            RunningTask.ContinueWith(task => {
                this.State = ACTION_STATE.CANCELLED;

                foreach (var fx in OnFailFxs)
                    fx(task.Exception!, task);
                
            }, TaskContinuationOptions.OnlyOnCanceled);

            foreach (var childAction in ChildActions.Where(x => x.RunMode == RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT)) {
                childAction.RunAction();
            }

            return RunningTask;
        }

        public int GetChildActionCount() => GetChildActionCount(this);

        private static int GetChildActionCount(ActionQueueAction action)
        {
            var ret = 0;
            foreach (var child in action.ChildActions) {
                ret += 1 + GetChildActionCount(child);
            }

            return ret;
        }

        public ActionQueueAction GetRootAction()
        {
            var action = this;

            while (action.Parent != null)
                action = action.Parent;

            return action;
        }
    }
}
