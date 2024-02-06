using Avalonia;

namespace RTSharp.Shared.Abstractions
{
    public interface IActionQueue
    {
        public StyledElement Display { get; }

        public IReadOnlyCollection<ActionQueueAction> Actions { get; }

        public void AddAction(ActionQueueAction Action);

        public Task<object?> RunAction(ActionQueueAction Action)
        {
            if (Action.Parent != null)
                throw new InvalidOperationException("Trying to run child action");
            AddAction(Action);
            return Action.RunAction();
        }

        public Task<T?> RunAction<T>(ActionQueueAction<T> Action)
        {
            if (Action.Parent != null)
                throw new InvalidOperationException("Trying to run child action");

            AddAction(Action);
            return Action.RunAction();
        }
    }
}
