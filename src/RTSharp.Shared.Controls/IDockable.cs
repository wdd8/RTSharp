using Avalonia.Media;

namespace RTSharp.Shared.Controls
{
    public interface IDockable
    {
        public string HeaderName { get; }

        public Geometry? Icon { get; }
    }
}
