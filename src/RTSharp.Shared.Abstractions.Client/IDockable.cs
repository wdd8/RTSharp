using Avalonia.Media;

namespace RTSharp.Shared.Abstractions.Client;

public interface IDockable
{
    public string HeaderName { get; }

    public Geometry? Icon { get; }
}
