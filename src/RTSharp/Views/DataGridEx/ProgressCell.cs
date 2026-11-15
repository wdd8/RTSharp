using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace RTSharp.Views.DataGridEx;

public class ProgressCell : Control
{
    private static readonly Color DefaultFillColor = Color.FromRgb(76, 148, 255);
    private static readonly IBrush DefaultTrackBrush = new SolidColorBrush(Color.FromRgb(46, 52, 64));
    private static readonly IBrush DefaultFillBrush = new SolidColorBrush(DefaultFillColor);
    private static readonly IBrush DefaultTextBrush = Brushes.White;

    private static Color GradientColor;
    private static IImmutableBrush? GradientBrush;

    private float _value;
    private FormattedText? FormattedText;
    private string? Text;
    private Size TextSize;
    private Size FormattedTextSize;

    public static readonly DirectProperty<ProgressCell, float> ValueProperty =
        AvaloniaProperty.RegisterDirect<ProgressCell, float>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v);

    public static readonly DirectProperty<ProgressCell, bool> ShowSequentialMarkerProperty =
        AvaloniaProperty.RegisterDirect<ProgressCell, bool>(
            nameof(ShowSequentialMarker),
            o => o.ShowSequentialMarker,
            (o, v) => o.ShowSequentialMarker = v);

    public static readonly DirectProperty<ProgressCell, bool> IsHashingProperty =
        AvaloniaProperty.RegisterDirect<ProgressCell, bool>(
            nameof(IsHashing),
            o => o.IsHashing,
            (o, v) => o.IsHashing = v);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressCell, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<ProgressCell, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> HashingTrackBrushProperty =
        AvaloniaProperty.Register<ProgressCell, IBrush?>(nameof(HashingTrackBrush));

    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<ProgressCell>();

    public static readonly AttachedProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<ProgressCell>();

    public static readonly AttachedProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<ProgressCell>();

    public static readonly AttachedProperty<FontStyle> FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner<ProgressCell>();

    public static readonly AttachedProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<ProgressCell>();

    public static readonly AttachedProperty<FontStretch> FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner<ProgressCell>();

    public float Value {
        get => _value;
        set {
            if (Math.Abs(_value - value) < 0.005f) {
                return;
            }

            SetAndRaise(ValueProperty, ref _value, value);
            FormattedText = null;
            InvalidateVisual();
        }
    }

    public bool ShowSequentialMarker {
        get;
        set => SetAndRaise(ShowSequentialMarkerProperty, ref field, value);
    }

    public bool IsHashing {
        get;
        set => SetAndRaise(IsHashingProperty, ref field, value);
    }

    public IBrush? TrackBrush {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FillBrush {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? HashingTrackBrush {
        get => GetValue(HashingTrackBrushProperty);
        set => SetValue(HashingTrackBrushProperty, value);
    }

    public IBrush? Foreground {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    static ProgressCell()
    {
        AffectsRender<ProgressCell>(
            ValueProperty,
            ShowSequentialMarkerProperty,
            IsHashingProperty,
            TrackBrushProperty,
            FillBrushProperty,
            HashingTrackBrushProperty,
            ForegroundProperty,
            FontFamilyProperty,
            FontSizeProperty,
            FontStyleProperty,
            FontWeightProperty,
            FontStretchProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 18);

    const double CORNER_RADIUS = 3.0;

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) {
            return;
        }

        var trackRect = new Rect(0, 0, bounds.Width, bounds.Height);
        var percent = Math.Clamp(Value, 0, 100);
        var fillWidth = trackRect.Width * percent / 100;

        var trackBrush = IsHashing ? (HashingTrackBrush ?? TrackBrush ?? DefaultTrackBrush) : (TrackBrush ?? DefaultTrackBrush);
        context.DrawRectangle(trackBrush, null, trackRect, CORNER_RADIUS);
        if (Value == -1) {
            context.DrawRectangle(GetGradientBrush(), null, trackRect, CORNER_RADIUS);
        } else if (fillWidth > 0) {
            using (context.PushClip(new RoundedRect(trackRect, CORNER_RADIUS))) {
                context.DrawRectangle(FillBrush ?? DefaultFillBrush, null, new Rect(0, 0, fillWidth, trackRect.Height));
            }
        }

        var text = GetFormattedText(bounds.Size, out var textSize);
        var point = new Point(
            Math.Max(0, (bounds.Width - textSize.Width) / 2),
            Math.Max(0, (bounds.Height - textSize.Height) / 2));
        context.DrawText(text, point);

        if (ShowSequentialMarker && bounds.Width >= 8) {
            DrawSequentialMarker(context, bounds);
        }
    }

    private IBrush GetGradientBrush()
    {
        if (GradientBrush != null) {
            return GradientBrush;
        }

        var color = (FillBrush as ISolidColorBrush)?.Color ?? DefaultFillColor;
        return GradientBrush = new LinearGradientBrush {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(6, 6, RelativeUnit.Absolute),
            SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops = {
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0),
                new GradientStop(color, 1)
            }
        }.ToImmutable();
    }

    private void DrawSequentialMarker(DrawingContext context, Rect bounds)
    {
        var pen = new Pen(Foreground ?? DefaultTextBrush, 1);

        var origin = bounds.Width / 2;
        const double centerY = 2.5;
        const double halfLen = 3.5;
        const double arrowArm = 2.0;
        var tip = new Point(origin + halfLen, centerY);

        context.DrawLine(pen, new Point(origin - halfLen, centerY), tip);
        context.DrawLine(pen, new Point(tip.X - arrowArm, centerY - arrowArm), tip);
        context.DrawLine(pen, new Point(tip.X - arrowArm, centerY + arrowArm), tip);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ForegroundProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty) {
            FormattedText = null;
        }
    }

    private FormattedText GetFormattedText(Size size, out Size textSize)
    {
        string text;
        if (Value != -1)
            text = Value.ToString("0.00'%'", CultureInfo.CurrentCulture);
        else
            text = "?";
        if (FormattedText != null && Text == text && TextSize == size) {
            textSize = FormattedTextSize;
            return FormattedText;
        }

        Text = text;
        TextSize = size;
        FormattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground ?? DefaultTextBrush)
        {
            MaxTextWidth = size.Width,
            MaxTextHeight = size.Height
        };
        textSize = FormattedTextSize = new Size(FormattedText.Width, FormattedText.Height);

        return FormattedText;
    }
}
