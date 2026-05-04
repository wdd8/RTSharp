using Dock.Model.Avalonia;
using Dock.Model.Core;

namespace RTSharp.Core;

public class RTSharpDockFactory : Factory
{
    public override void CloseDockable(IDockable dockable)
    {
        /*
         * Avalonia models don't work that well in Dock.Avalonia, for some reason
         * Command/CommandProperty don't get cleared when dockable window is closed, so
         * it causes a null ref exception in FactoryBase.CloseDockable because
         * PART_CloseButton binds to ActiveDockable which doesn't exist at the time the
         * command fires. In MVVM/Rx command gets cleared.
         */
        if (dockable is null)
            return;

        base.CloseDockable(dockable);
    }
}
