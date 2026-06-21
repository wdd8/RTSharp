using Avalonia;
using Avalonia.Controls;

namespace RTSharp.Views.Util;

public static class ToolWindow
{
    public static readonly AttachedProperty<string?> TitleProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Title", typeof(ToolWindow));

    public static readonly AttachedProperty<bool> CanResizeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("CanResize", typeof(ToolWindow), defaultValue: true);

    public static readonly AttachedProperty<SizeToContent> SizeToContentProperty =
        AvaloniaProperty.RegisterAttached<Control, SizeToContent>("SizeToContent", typeof(ToolWindow), defaultValue: SizeToContent.Manual);

    public static readonly AttachedProperty<WindowIcon?> IconProperty =
        AvaloniaProperty.RegisterAttached<Control, WindowIcon?>("Icon", typeof(ToolWindow));

    public static string? GetTitle(Control element) => element.GetValue(TitleProperty);
    public static void SetTitle(Control element, string? value) => element.SetValue(TitleProperty, value);

    public static bool GetCanResize(Control element) => element.GetValue(CanResizeProperty);
    public static void SetCanResize(Control element, bool value) => element.SetValue(CanResizeProperty, value);

    public static SizeToContent GetSizeToContent(Control element) => element.GetValue(SizeToContentProperty);
    public static void SetSizeToContent(Control element, SizeToContent value) => element.SetValue(SizeToContentProperty, value);

    public static WindowIcon? GetIcon(Control element) => element.GetValue(IconProperty);
    public static void SetIcon(Control element, WindowIcon? value) => element.SetValue(IconProperty, value);
}
