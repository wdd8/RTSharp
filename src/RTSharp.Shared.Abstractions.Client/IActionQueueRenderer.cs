using Avalonia;

using RTSharp.Shared.Abstractions;

namespace RTSharp.Shared.Abstractions.Client;

public interface IActionQueueRenderer
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
