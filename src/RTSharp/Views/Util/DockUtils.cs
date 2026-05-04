using Avalonia.Controls;

using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;

using System;

namespace RTSharp.Views.Util;

public class DockUtils
{
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

    public static void SpawnToolWindow(UserControl View, string Title)
    {
        var tool = new Tool {
            Id = Guid.NewGuid().ToString(),
            Title = Title,
            Content = new Func<IServiceProvider, object>(_ => {
                return View;
            })
        };

        var toolDock = WalkUntilToolDock(App.MainWindowViewModel.RootDock);
        if (toolDock is null)
            return;

        App.MainWindowViewModel.DockFactory.AddDockable(toolDock, tool);
        App.MainWindowViewModel.DockFactory.SetActiveDockable(tool);
        tool.SetVisibleBounds(App.MainWindow.Position.X + 25, App.MainWindow.Position.Y + 25, Double.IsNaN(View.Width) ? View.MinWidth : View.Width, Double.IsNaN(View.Height) ? View.MinHeight : View.Height);
        tool.SetPointerScreenPosition(App.MainWindow.Position.X + 25, App.MainWindow.Position.Y + 25);
        App.MainWindowViewModel.DockFactory.FloatDockable(tool, new DockWindowOptions {
            OwnerMode = DockWindowOwnerMode.RootWindow
        });
        App.MainWindowViewModel.DockFactory.SetActiveDockable(tool);
    }
}
