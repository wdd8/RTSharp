using System.Collections.Concurrent;
using System.Collections.Immutable;
using Serilog;

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
        public T? GetResult() => RunningTask.IsCompleted ? (T?)RunningTask.Result : default;

        internal ActionQueueAction(string Name, RUN_MODE RunMode, Func<Task<T>> Task, ActionQueueAction<T>? Parent, Progress<(float, string)>? Progress) : base(Guid.NewGuid(), Name, RunMode, async () => { var res = await Task(); return res; }, Parent, Progress)
        {
        }

        internal ActionQueueAction(ActionQueueAction In) : base(Guid.NewGuid(), In.Name, In.RunMode, In.FxCreateTask, In.Parent, In.Progress)
        {
        }

        public ActionQueueAction<TChild> CreateChild<TChild>(string Name, RUN_MODE RunMode, Func<ActionQueueAction<T>, Task<TChild>> Task)
        {
            async Task<object?> task(ActionQueueAction parent)
            {
                var child = await Task((ActionQueueAction<T>)parent);
                return child;
            }

            return base.CreateChild<TChild>(Name, RunMode, task);
        }

        public ActionQueueAction CreateChild(string Name, RUN_MODE RunMode, Func<ActionQueueAction<T>, ValueTask> Task)
        {
            async Task<object?> task(ActionQueueAction parent)
            {
                await Task((ActionQueueAction<T>)parent);
                return null;
            }

            return base.CreateChild(Name, RunMode, task);
        }

        public new async Task<T?> RunAction() => (T?)await base.RunAction();

        public void BindProgress(Progress<(float, string)> Progress) => base.BindProgress(Progress);
    }

    public class ActionQueueAction
    {
        public ActionQueueAction? Parent { get; private set; }

        public Guid Id { get; init; }

        public string Name { get; init; }

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        public ACTION_STATE State { get; set; } = ACTION_STATE.WAITING;

        public RUN_MODE? RunMode { get; private set; }

        public Func<Task<object?>> FxCreateTask { get; }

        public Task<object?> RunningTask { get; private set; }

        public float ProgressDone { get; set; }

        public string ProgressString { get; set; }

        public event EventHandler<(float, string)>? ProgressChanged;

        internal Progress<(float, string)>? Progress;

        private ActionQueueAction()
        {
        }

        protected ActionQueueAction(Guid Id, string Name, RUN_MODE? RunMode, Func<Task<object?>>? FxCreateTask, ActionQueueAction? Parent, Progress<(float, string)>? Progress = null)
        {
            this.Id = Id;
            this.Name = Name;
            this.FxCreateTask = FxCreateTask;
            this.Parent = Parent;
            this.RunMode = RunMode;
            if (Progress != null) {
                BindProgress(Progress);
            }
        }

        public bool IsCompleted => State == ACTION_STATE.CANCELLED || State == ACTION_STATE.FAILED || State == ACTION_STATE.DONE;

        public void BindProgress(Progress<(float, string)> Progress)
        {
            this.Progress = Progress;
            this.Progress.ProgressChanged += Progress_ProgressChanged;
        }

        private void Progress_ProgressChanged(object? sender, (float, string) e)
        {
            ProgressDone = e.Item1;
            ProgressString = e.Item2;

            ProgressChanged?.Invoke(sender, e);
        }


        public static ActionQueueAction<T> New<T>(string Name, Func<Task<T>> FxCreateTask, Progress<(float, string)>? Progress = null) => new(Name, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, FxCreateTask, null, Progress);

        public static ActionQueueAction New(string Name, Func<Task<object?>> FxCreateTask, Progress<(float, string)>? Progress = null) => new(Guid.NewGuid(), Name, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, FxCreateTask, null, Progress);

        public static ActionQueueAction New(string Name, Func<ValueTask> FxCreateTask, Progress<(float, string)>? Progress = null) => new(Guid.NewGuid(), Name, RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, async () => { await FxCreateTask(); return null; }, null, Progress);

        public static ActionQueueAction NewCosmetic(Guid Id, string Name)
        {
            var ret = new ActionQueueAction(Id, Name, null, null, null);
            return ret;
        }

        public ActionQueueAction<TChild> CreateChild<TChild>(string Name, RUN_MODE RunMode, Func<ActionQueueAction, ValueTask> FxCreateTask)
        {
            var ret = new ActionQueueAction<TChild>(new ActionQueueAction(Guid.NewGuid(), Name, RunMode, async () => { await FxCreateTask(this); return null!; }, this));
            _childActions.Add(ret);
            return ret;
        }

        public ActionQueueAction<TChild> CreateChild<TChild>(string Name, RUN_MODE RunMode, Func<ActionQueueAction, Task<object?>> FxCreateTask)
        {
            var ret = new ActionQueueAction<TChild>(new ActionQueueAction(Guid.NewGuid(), Name, RunMode, () => FxCreateTask(this), this));
            _childActions.Add(ret);
            return ret;
        }

        public ActionQueueAction CreateChild(string Name, RUN_MODE RunMode, Func<ActionQueueAction, Task<object?>> FxCreateTask)
        {
            var ret = new ActionQueueAction(Guid.NewGuid(), Name, RunMode, () => FxCreateTask(this), this);
            _childActions.Add(ret);
            return ret;
        }

        public ActionQueueAction CreateChild(string Name, RUN_MODE RunMode, Func<ActionQueueAction, ValueTask> FxCreateTask)
        {
            var ret = new ActionQueueAction(Guid.NewGuid(), Name, RunMode, async () => { await FxCreateTask(this); return null!; }, this);
            _childActions.Add(ret);
            return this;
        }

        public ActionQueueAction CreateCosmeticChild(Guid Id, string Name)
        {
            var ret = new ActionQueueAction(Id, Name, null, null, null);
            _childActions.Add(ret);
            return ret;
        }

        private readonly List<Action<Task<object?>>> OnRunFxs = new();
        public void OnRun(Action<Task<object?>> Fx)
        {
            OnRunFxs.Add(Fx);
        }

        private readonly List<Action<object?, Task<object?>>> AfterRunFxs = new();
        public void AfterRun(Action<object?, Task<object?>> Fx)
        {
            AfterRunFxs.Add(Fx);
        }

        private readonly List<Action<Exception, Task<object?>>> OnFailFxs = new();
        public void OnFail(Action<Exception, Task<object?>> Fx)
        {
            OnFailFxs.Add(Fx);
        }

        internal Task<object?> RunAction()
        {
            this.State = ACTION_STATE.RUNNING;
            RunningTask = FxCreateTask();
            foreach (var fx in OnRunFxs)
                fx(RunningTask);

            RunningTask.ContinueWith(task => {
                this.State = ACTION_STATE.DONE;

                foreach (var fx in AfterRunFxs)
                    fx(task.Result, task);
                
                foreach (var childAction in _childActions.Where(x => x.RunMode == RUN_MODE.DEPENDS_ON_PARENT)) {
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

            foreach (var childAction in _childActions.Where(x => x.RunMode == RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT)) {
                childAction.RunAction();
            }

            return RunningTask;
        }

        protected ConcurrentBag<ActionQueueAction> _childActions { get; } = new();

        public ImmutableArray<ActionQueueAction> ChildActions => _childActions.ToImmutableArray();
    }
}
