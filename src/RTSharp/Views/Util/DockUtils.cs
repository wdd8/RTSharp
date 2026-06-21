using Avalonia.Controls;

using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

using System;

namespace RTSharp.Views.Util;

public class DockUtils
{
    private const double DEFAULT_WIDTH = 400;
    private const double DEFAULT_HEIGHT = 300;

    private static ToolDock? WalkUntilToolDock(IDock dock)
    {
        foreach (var dockable in dock.VisibleDockables!) {
            if (dockable is ToolDock toolDock)
                return toolDock;

            if (dockable is IDock innerDock) {
                var innerToolDock = WalkUntilToolDock(innerDock);
                if (innerToolDock != null)
                    return innerToolDock;
            }
        }

        return null;
    }

    public static void SpawnToolWindow(UserControl View)
    {
        var initialWidth = !Double.IsNaN(View.Width) ? View.Width : (View.MinWidth > 0 ? View.MinWidth : DEFAULT_WIDTH);
        var initialHeight = !Double.IsNaN(View.Height) ? View.Height : (View.MinHeight > 0 ? View.MinHeight : DEFAULT_HEIGHT);
        var minX = View.MinWidth;
        var minY = View.MinHeight;
        var maxX = View.MaxWidth;
        var maxY = View.MaxHeight;

        var sizeToContent = ToolWindow.GetSizeToContent(View);
        var canResize = ToolWindow.GetCanResize(View);
        var icon = ToolWindow.GetIcon(View);

        View.Width = Double.NaN;
        View.Height = Double.NaN;

        var tool = new Tool {
            Id = Guid.NewGuid().ToString(),
            Title = ToolWindow.GetTitle(View) ?? String.Empty,
            Content = new Func<IServiceProvider, object>(_ => View)
        };

        var toolDock = WalkUntilToolDock(App.MainWindowViewModel.RootDock);
        if (toolDock == null)
            return;

        App.MainWindowViewModel.DockFactory.AddDockable(toolDock, tool);
        App.MainWindowViewModel.DockFactory.SetActiveDockable(tool);
        tool.SetVisibleBounds(App.MainWindow.Position.X + 25, App.MainWindow.Position.Y + 25, initialWidth, initialHeight);
        tool.SetPointerScreenPosition(App.MainWindow.Position.X + 25, App.MainWindow.Position.Y + 25);
        App.MainWindowViewModel.DockFactory.FloatDockable(tool, new DockWindowOptions {
            OwnerMode = DockWindowOwnerMode.RootWindow
        });
        App.MainWindowViewModel.DockFactory.SetActiveDockable(tool);

        IDockable? owner = tool.Owner;
        while (owner != null && owner is not IRootDock)
            owner = owner.Owner;

        if ((owner as IRootDock)?.Window?.Host is Window hostWindow) {
            hostWindow.CanResize = canResize;
            hostWindow.SizeToContent = sizeToContent;

            if (sizeToContent is SizeToContent.Manual or SizeToContent.Height)
                hostWindow.Width = initialWidth;
            if (sizeToContent is SizeToContent.Manual or SizeToContent.Width)
                hostWindow.Height = initialHeight;

            if (minX > 0)
                hostWindow.MinWidth = minX;
            if (minY > 0)
                hostWindow.MinHeight = minY;
            if (!Double.IsInfinity(maxX))
                hostWindow.MaxWidth = maxX;
            if (!Double.IsInfinity(maxY))
                hostWindow.MaxHeight = maxY;

            if (icon != null)
                hostWindow.Icon = icon;
        }
    }
}
