using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RTSharp.Shared.Controls.Views;

public class BuiltInPathIcon : PathIcon
{
    public static readonly StyledProperty<BuiltInIcons?> ValueProperty =
        AvaloniaProperty.Register<BuiltInPathIcon, BuiltInIcons?>(nameof(Value));

    public BuiltInIcons? Value {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private Bitmap? Image;

    protected override Type StyleKeyOverride => typeof(PathIcon);

    static BuiltInPathIcon()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInPathIcon>((o, e) => o.OnValueChanged());
    }

    private void OnValueChanged()
    {
        Data = null;
        Image = null;

        if (Value is { } icon) {
            if (BuiltInIcon.IsSvg(icon))
                Data = BuiltInIcon.GetGeometry(icon);
            else
                Image = BuiltInIcon.Get(icon);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext Context)
    {
        base.Render(Context);

        if (Image != null)
            Context.DrawImage(Image, new Rect(Bounds.Size));
    }
}
