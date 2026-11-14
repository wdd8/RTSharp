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

        if (Value != null && BuiltInIcon.IsSvg(Value.Value))
            Data = BuiltInIcon.GetGeometry(Value.Value);

        InvalidateArrange();
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size FinalSize)
    {
        var size = base.ArrangeOverride(FinalSize);

        if (Value != null && Data == null) {
            var bitmap = BuiltInIcon.Get(Value.Value, (int)Math.Ceiling(Math.Max(size.Width, size.Height) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)));

            if (BuiltInIcon.IsMask(Value.Value)) {
                if (Mask?.Source != bitmap) {
                    Mask = new ImageBrush(bitmap);
                    InvalidateVisual();
                }
            } else if (Image != bitmap) {
                Image = bitmap;
                InvalidateVisual();
            }
        }

        return size;
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
