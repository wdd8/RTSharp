using Avalonia;
using Avalonia.Controls;

namespace RTSharp.Shared.Controls.Views;

public class BuiltInIconImage : Image
{
    public static readonly StyledProperty<BuiltInIcons?> ValueProperty =
        AvaloniaProperty.Register<BuiltInIconImage, BuiltInIcons?>(nameof(Value));

    public BuiltInIcons? Value {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static BuiltInIconImage()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInIconImage>((o, e) => o.OnValueChanged());
    }

    private void OnValueChanged()
    {
        Source = Value is { } icon ? BuiltInIcon.Get(icon) : null;
    }
}
