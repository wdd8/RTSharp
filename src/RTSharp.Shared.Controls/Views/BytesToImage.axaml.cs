using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using System.Runtime.InteropServices;

using Color = Avalonia.Media.Color;

namespace RTSharp.Shared.Controls.Views;

public partial class BytesToImage : Control
{
    private WriteableBitmap? CachedBitmap;
    private int CachedWidth;
    private int CachedHeight;

    public static readonly DirectProperty<BytesToImage, byte[]> BytesProperty =
        AvaloniaProperty.RegisterDirect<BytesToImage, byte[]>(
            nameof(Bytes),
            o => o.Bytes,
            (o, v) => o.Bytes = v);

    public byte[] Bytes {
        get;
        set {
            if (SetAndRaise(BytesProperty, ref field, value))
                InvalidateCache();
        }
    } = [];

    static readonly Color ThemeColor;
    const int SQUARE_SIZE = 8;
    const float THEME_VARIATION = 1f;

    static BytesToImage()
    {
        AffectsRender<BytesToImage>(BytesProperty);

        if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
            var colorSetEx = GetImmersiveColorFromColorSetEx((uint)GetImmersiveUserColorSetPreference(false, false), GetImmersiveColorTypeFromName(Marshal.StringToHGlobalUni("ImmersiveStartSelectionBackground")), false, 0);

            ThemeColor = Color.FromArgb((byte)((0xFF000000 & colorSetEx) >> 24), (byte)(0x000000FF & colorSetEx), (byte)((0x0000FF00 & colorSetEx) >> 8), (byte)((0x00FF0000 & colorSetEx) >> 16));
        } else {
            ThemeColor = Color.FromArgb(255, 127, 127, 127);
        }
    }

    public override void Render(DrawingContext Ctx)
    {
        if (Bytes == null || Bytes.Length == 0 || Height == 0)
            return;

        int destW = Math.Max(1, (int)Width);
        int destH = Math.Max(1, (int)Height);

        if (CachedBitmap is null || CachedWidth != destW || CachedHeight != destH) {
            CachedBitmap?.Dispose();
            CachedBitmap = BuildBitmap(destW, destH);
            CachedWidth = destW;
            CachedHeight = destH;
        }

        Ctx.DrawImage(CachedBitmap, new Rect(0, 0, destW, destH), new Rect(0, 0, Width, Height));
    }

    private unsafe WriteableBitmap BuildBitmap(int destW, int destH)
    {
        var bmp = new WriteableBitmap(new PixelSize(destW, destH), new Vector(96, 96), PixelFormat.Bgra8888);
        var ints = MemoryMarshal.Cast<byte, int>(Bytes);
        if (ints.Length == 0)
            return bmp;

        const int srcW = SQUARE_SIZE;
        const int srcH = SQUARE_SIZE;

        using var locked = bmp.Lock();
        var baseAddr = locked.Address;
        int bpp = locked.Format.BitsPerPixel / 8;

        int cur = ints[0];

        for (int x = 0; x < srcW; x++) {
            for (int y = 0; y < srcH; y++) {
                byte r = (byte)((cur & 0xFF000000) >> 24);
                byte g = (byte)((cur & 0x00FF0000) >> 16);
                byte b = (byte)((cur & 0x0000FF00) >> 8);
                byte a = (byte)((cur & 0x000000FF) >> 0);

                int color =
                    ((byte)Math.Abs(unchecked(ThemeColor.A + ((a - 127) / THEME_VARIATION))) << 24) |
                    ((byte)Math.Abs(unchecked(ThemeColor.R + ((r - 127) / THEME_VARIATION))) << 16) |
                    ((byte)Math.Abs(unchecked(ThemeColor.G + ((g - 127) / THEME_VARIATION))) << 8) |
                    ((byte)Math.Abs(unchecked(ThemeColor.B + ((b - 127) / THEME_VARIATION))) << 0);

                cur += ints[(x + y * srcW) % ints.Length];
                cur = (int)(((long)cur * 48271) % 0x7fffffff);

                int x0 = (int)Math.Floor((double)x * destW / srcW);
                int x1 = (int)Math.Floor((double)(x + 1) * destW / srcW) - 1;
                int y0 = (int)Math.Floor((double)y * destH / srcH);
                int y1 = (int)Math.Floor((double)(y + 1) * destH / srcH) - 1;

                for (int yy = y0; yy <= y1; yy++) {
                    var p = baseAddr + ((nint)yy * destW + x0) * bpp;
                    for (int xx = x0; xx <= x1; xx++) {
                        *(int*)p = color;
                        p += bpp;
                    }
                }
            }
        }

        return bmp;
    }

    private void InvalidateCache()
    {
        CachedBitmap?.Dispose();
        CachedBitmap = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        InvalidateCache();
    }

    [LibraryImport("uxtheme.dll", EntryPoint = "#94", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetImmersiveColorSetCount();

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetImmersiveColorFromColorSetEx(
        uint dwImmersiveColorSet,
        uint dwImmersiveColorType,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreHighContrast,
        uint dwHighContrastCacheMode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#96", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetImmersiveColorTypeFromName(
        nint name);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetImmersiveUserColorSetPreference(
        [MarshalAs(UnmanagedType.Bool)] bool bForceCheckRegistry,
        [MarshalAs(UnmanagedType.Bool)] bool bSkipCheckOnFail);

    [LibraryImport("uxtheme.dll", EntryPoint = "#100", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetImmersiveColorNamedTypeByIndex(
        uint dwIndex);
}
