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

    private ImageBrush? Mask;

    private int Generation;

    protected override Type StyleKeyOverride => typeof(PathIcon);

    static BuiltInPathIcon()
    {
        ValueProperty.Changed.AddClassHandler<BuiltInPathIcon>((o, e) => o.OnValueChanged());
        AffectsRender<BuiltInPathIcon>(ForegroundProperty);
    }

    public BuiltInPathIcon()
    {
        ResourcesChanged += (_, _) => {
            if (Generation != BuiltInIcon.Generation)
                OnValueChanged();
        };
    }

    private void OnValueChanged()
    {
        Generation = BuiltInIcon.Generation;
        Data = null;
        Image = null;
        Mask = null;

        if (Value is { } icon) {
            if (BuiltInIcon.IsSvg(icon))
                Data = BuiltInIcon.GetGeometry(icon);
            else if (BuiltInIcon.IsMask(icon))
                Mask = new ImageBrush(BuiltInIcon.Get(icon));
            else
                Image = BuiltInIcon.Get(icon);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext Context)
    {
        base.Render(Context);

        var bounds = new Rect(Bounds.Size);

        if (Image != null)
            Context.DrawImage(Image, bounds);

        if (Mask != null && Foreground != null) {
            using (Context.PushOpacityMask(Mask, bounds))
                Context.FillRectangle(Foreground, bounds);
        }
    }
}
