using Avalonia;
using Avalonia.Labs.Gif;

namespace RTSharp.Shared.Controls.Views;

public class BuiltInGifImage : GifImage
{
    public static readonly StyledProperty<BuiltInIcons?> ValueProperty =
        AvaloniaProperty.Register<BuiltInGifImage, BuiltInIcons?>(nameof(Value));

    public BuiltInIcons? Value {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private int Generation;

    static BuiltInGifImage()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInGifImage>((o, e) => o.OnValueChanged());
    }

    public BuiltInGifImage()
    {
        ResourcesChanged += (_, _) => {
            if (Generation != BuiltInIcon.Generation)
                OnValueChanged();
        };
    }

    private void OnValueChanged()
    {
        Generation = BuiltInIcon.Generation;
        Source = Value is { } icon ? BuiltInIcon.GetGif(icon) : null;
    }
}
