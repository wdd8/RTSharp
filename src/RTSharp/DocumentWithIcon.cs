using Dock.Controls.DeferredContentControl;
using Dock.Model.Avalonia.Controls;

using RTSharp.Shared.Controls;

namespace RTSharp;

public class DocumentWithIcon : Document, IDeferredContentPresentation
{
    public required BuiltInIcons Icon { get; set; }

    /// <summary>
    /// Deferred content is built after the window shows, at background priority, behind any other UI work
    /// </summary>
    public bool DeferContentPresentation { get; init; } = true;
}
