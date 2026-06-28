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

    static BuiltInGifImage()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInGifImage>((o, e) => o.OnValueChanged());
    }

    private void OnValueChanged()
    {
        Source = Value is { } icon ? BuiltInIcon.GetGif(icon) : null;
    }
}
