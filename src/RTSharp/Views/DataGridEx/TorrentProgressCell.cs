using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace RTSharp.Views.DataGridEx;

public class TorrentProgressCell : Control
{
    private static readonly IBrush DefaultTrackBrush = new SolidColorBrush(Color.FromRgb(46, 52, 64));
    private static readonly IBrush DefaultFillBrush = new SolidColorBrush(Color.FromRgb(76, 148, 255));
    private static readonly IBrush DefaultTextBrush = Brushes.White;

    private float _value;
    private FormattedText? FormattedText;
    private string? Text;
    private Size TextSize;
    private Size FormattedTextSize;

    public static readonly DirectProperty<TorrentProgressCell, float> ValueProperty =
        AvaloniaProperty.RegisterDirect<TorrentProgressCell, float>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<TorrentProgressCell, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<TorrentProgressCell, IBrush?>(nameof(FillBrush));

    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<TorrentProgressCell>();

    public static readonly AttachedProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<TorrentProgressCell>();

    public static readonly AttachedProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<TorrentProgressCell>();

    public static readonly AttachedProperty<FontStyle> FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner<TorrentProgressCell>();

    public static readonly AttachedProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<TorrentProgressCell>();

    public static readonly AttachedProperty<FontStretch> FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner<TorrentProgressCell>();

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

    public IBrush? TrackBrush {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FillBrush {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
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

    static TorrentProgressCell()
    {
        AffectsRender<TorrentProgressCell>(
            ValueProperty,
            TrackBrushProperty,
            FillBrushProperty,
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

        context.DrawRectangle(TrackBrush ?? DefaultTrackBrush, null, trackRect, CORNER_RADIUS);
        if (fillWidth > 0) {
            using (context.PushClip(new RoundedRect(trackRect, CORNER_RADIUS))) {
                context.DrawRectangle(FillBrush ?? DefaultFillBrush, null, new Rect(0, 0, fillWidth, trackRect.Height));
            }
        }

        var text = GetFormattedText(bounds.Size, out var textSize);
        var point = new Point(
            Math.Max(0, (bounds.Width - textSize.Width) / 2),
            Math.Max(0, (bounds.Height - textSize.Height) / 2));
        context.DrawText(text, point);
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
        var text = Value.ToString("0.00", CultureInfo.CurrentCulture) + " %";
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
