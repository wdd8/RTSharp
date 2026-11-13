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

    private int Generation;

    static BuiltInIconImage()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInIconImage>((o, e) => o.OnValueChanged());
    }

    public BuiltInIconImage()
    {
        ResourcesChanged += (_, _) => {
            if (Generation != BuiltInIcon.Generation)
                OnValueChanged();
        };
    }

    private void OnValueChanged()
    {
        Generation = BuiltInIcon.Generation;
        Source = Value is { } icon ? BuiltInIcon.Get(icon) : null;
    }
}
