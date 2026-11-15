using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Labs.Gif;
using Avalonia.Media;

namespace RTSharp.Shared.Controls.Views;

public class BuiltInIconImage : Control
{
    public static readonly StyledProperty<BuiltInIcons?> ValueProperty =
        AvaloniaProperty.Register<BuiltInIconImage, BuiltInIcons?>(nameof(Value));

    public static readonly StyledProperty<Stretch> StretchProperty =
        Image.StretchProperty.AddOwner<BuiltInIconImage>();

    public static readonly StyledProperty<StretchDirection> StretchDirectionProperty =
        Image.StretchDirectionProperty.AddOwner<BuiltInIconImage>();

    public BuiltInIcons? Value {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Stretch Stretch {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public StretchDirection StretchDirection {
        get => GetValue(StretchDirectionProperty);
        set => SetValue(StretchDirectionProperty, value);
    }

    private Control? Child;

    private int Generation;

    static BuiltInIconImage()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInIconImage>((o, e) => o.OnValueChanged());
        TextElement.ForegroundProperty.Changed.AddClassHandler<BuiltInIconImage>((o, e) => o.InvalidateMeasure());
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

        if (Child != null) {
            VisualChildren.Remove(Child);
            LogicalChildren.Remove(Child);
        }

        Child = Value is not { } icon ? null : BuiltInIcon.IsGif(icon)
            ? new GifImage {
                Source = BuiltInIcon.GetGif(icon),
                [!GifImage.StretchProperty] = this[!StretchProperty],
                [!GifImage.StretchDirectionProperty] = this[!StretchDirectionProperty]
            }
            : new Image {
                [!Image.StretchProperty] = this[!StretchProperty],
                [!Image.StretchDirectionProperty] = this[!StretchDirectionProperty]
            };

        if (Child != null) {
            VisualChildren.Add(Child);
            LogicalChildren.Add(Child);
        }

        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size AvailableSize)
    {
        if (Child is Image image && Value is { } icon) {
            var fit = Math.Min(AvailableSize.Width, AvailableSize.Height);
            image.Source = BuiltInIcon.Get(icon, Double.IsInfinity(fit) ? Int32.MaxValue : (int)Math.Ceiling(fit * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)), GetValue(TextElement.ForegroundProperty));
        }

        Child?.Measure(AvailableSize);
        return Child?.DesiredSize ?? default;
    }

    protected override Size ArrangeOverride(Size FinalSize)
    {
        Child?.Arrange(new Rect(FinalSize));
        return FinalSize;
    }
}
