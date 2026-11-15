using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core;

using Serilog;

namespace RTSharp.Views.Util;

public static class WindowPlacement
{
    public static readonly AttachedProperty<bool> RememberProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("Remember", typeof(WindowPlacement));

    public static readonly AttachedProperty<string?> PageProperty = AvaloniaProperty.RegisterAttached<Window, string?>("Page", typeof(WindowPlacement), defaultBindingMode: BindingMode.TwoWay);

    public static bool GetRemember(Window element) => element.GetValue(RememberProperty);
    public static void SetRemember(Window element, bool value) => element.SetValue(RememberProperty, value);

    public static string? GetPage(Window element) => element.GetValue(PageProperty);
    public static void SetPage(Window element, string? value) => element.SetValue(PageProperty, value);

    static WindowPlacement()
    {
        RememberProperty.Changed.AddClassHandler<Window>((window, e) => {
            if (e.NewValue is true && !Design.IsDesignMode)
                Attach(window);
        });
    }

    private static void Attach(Window Window)
    {
        var key = Window.GetType().Name;
        string? page = null;

        void restore()
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var uiState = scope.ServiceProvider.GetRequiredService<Config>().UIState.Value;

            if (Window.CanResize && Window.SizeToContent == SizeToContent.Manual)
                uiState.RestoreWindowSize(Window);
            uiState.RestoreWindowPosition(Window);
            uiState.WindowPages.TryGetValue(key, out page);
        }

        if (Window.IsInitialized)
            restore();
        else
            Window.Initialized += (_, _) => restore();

        Window.Opened += (_, _) => {
            if (page != null)
                Window.SetCurrentValue(PageProperty, page);
        };

        Window.Closing += async (_, _) => {
            try {
                using var scope = Core.ServiceProvider.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<Config>();
                var uiState = config.UIState.Value;

                var changed = uiState.StoreWindowPosition(Window);
                if (Window.CanResize)
                    changed |= uiState.StoreWindowSize(Window);

                var currentPage = Window.GetValue(PageProperty);
                if (currentPage != null && (!uiState.WindowPages.TryGetValue(key, out var previousPage) || previousPage != currentPage)) {
                    uiState.WindowPages[key] = currentPage;
                    changed = true;
                }

                if (changed)
                    await config.Rewrite();
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to save {Window} placement", key);
            }
        };
    }
}
