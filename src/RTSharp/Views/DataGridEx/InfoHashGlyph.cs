using System;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RTSharp.Views.DataGridEx;

public sealed partial class InfoHashGlyph : Control
{
    private const int SquareSize = 8;
    private const float ThemeVariation = 1f;

    private static readonly Color ThemeColor = GetThemeColor();

    private WriteableBitmap? Bitmap;
    private PixelSize BitmapSize;
    private bool ResetPending = true;

    public static readonly DirectProperty<InfoHashGlyph, byte[]?> BytesProperty =
        AvaloniaProperty.RegisterDirect<InfoHashGlyph, byte[]?>(
            nameof(Bytes),
            o => o.Bytes,
            (o, v) => o.Bytes = v);

    private byte[]? _bytes;
    public byte[]? Bytes {
        get => _bytes;
        set {
            SetAndRaise(BytesProperty, ref _bytes, value);
            ResetPending = true;
            InvalidateVisual();
        }
    }

    static InfoHashGlyph()
    {
        AffectsRender<InfoHashGlyph>(BytesProperty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Bitmap?.Dispose();
        Bitmap = null;
    }

    public override unsafe void Render(DrawingContext context)
    {
        if (_bytes?.Length < 4 || Bounds.Width <= 0 || Bounds.Height <= 0) {
            return;
        }

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
        var size = new PixelSize(width, height);

        if (Bitmap == null || BitmapSize != size) {
            Bitmap?.Dispose();
            Bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888);
            BitmapSize = size;
            ResetPending = true;
        }

        if (ResetPending) {
            FillBitmap(Bitmap);
            ResetPending = false;
        }

        context.DrawImage(Bitmap, new Rect(0, 0, width, height), Bounds);
    }

    private unsafe void FillBitmap(WriteableBitmap bitmap)
    {
        var words = MemoryMarshal.Cast<byte, int>(_bytes.AsSpan());
        if (words.Length == 0) {
            return;
        }

        using var locked = bitmap.Lock();
        var baseAddress = locked.Address;
        var bytesPerPixel = locked.Format.BitsPerPixel / 8;
        var width = BitmapSize.Width;
        var height = BitmapSize.Height;
        var current = words[0];

        for (var x = 0; x < SquareSize; x++) {
            for (var y = 0; y < SquareSize; y++) {
                var r = (byte)((current & 0xFF000000) >> 24);
                var g = (byte)((current & 0x00FF0000) >> 16);
                var b = (byte)((current & 0x0000FF00) >> 8);
                var a = (byte)(current & 0x000000FF);

                var color =
                    ((byte)Math.Abs(unchecked(ThemeColor.A + ((a - 127) / ThemeVariation))) << 24) |
                    ((byte)Math.Abs(unchecked(ThemeColor.R + ((r - 127) / ThemeVariation))) << 16) |
                    ((byte)Math.Abs(unchecked(ThemeColor.G + ((g - 127) / ThemeVariation))) << 8) |
                    ((byte)Math.Abs(unchecked(ThemeColor.B + ((b - 127) / ThemeVariation))));

                current += words[(x + y * SquareSize) % words.Length];
                current = (int)(((long)current * 48271) % 0x7fffffff);

                var x0 = (int)Math.Floor((double)x * width / SquareSize);
                var x1 = (int)Math.Floor((double)(x + 1) * width / SquareSize) - 1;
                var y0 = (int)Math.Floor((double)y * height / SquareSize);
                var y1 = (int)Math.Floor((double)(y + 1) * height / SquareSize) - 1;

                for (var yy = y0; yy <= y1; yy++) {
                    var pixel = baseAddress + ((nint)yy * width + x0) * bytesPerPixel;
                    for (var xx = x0; xx <= x1; xx++) {
                        *(int*)pixel = color;
                        pixel += bytesPerPixel;
                    }
                }
            }
        }
    }

    private static Color GetThemeColor()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
            return Color.FromArgb(255, 127, 127, 127);
        }

        var name = Marshal.StringToHGlobalUni("ImmersiveStartSelectionBackground");
        try {
            var colorSetEx = GetImmersiveColorFromColorSetEx(
                GetImmersiveUserColorSetPreference(false, false),
                GetImmersiveColorTypeFromName(name),
                false,
                0);

            return Color.FromArgb(
                (byte)((0xFF000000 & colorSetEx) >> 24),
                (byte)(0x000000FF & colorSetEx),
                (byte)((0x0000FF00 & colorSetEx) >> 8),
                (byte)((0x00FF0000 & colorSetEx) >> 16));
        } finally {
            Marshal.FreeHGlobal(name);
        }
    }

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetImmersiveColorFromColorSetEx(
        uint dwImmersiveColorSet,
        uint dwImmersiveColorType,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreHighContrast,
        uint dwHighContrastCacheMode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#96", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetImmersiveColorTypeFromName(nint name);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetImmersiveUserColorSetPreference(
        [MarshalAs(UnmanagedType.Bool)] bool bForceCheckRegistry,
        [MarshalAs(UnmanagedType.Bool)] bool bSkipCheckOnFail);
}
