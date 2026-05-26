using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using System;
using System.Drawing;
using System.Runtime.InteropServices;

using Color = Avalonia.Media.Color;

namespace RTSharp.Shared.Controls.Views;

public partial class BytesToImage : UserControl
{
    private byte[] _bytes = [];
    private int[] _ints = [];

    /// <summary>
    /// Defines the <see cref="Bytes"/> property.
    /// </summary>
    public static readonly DirectProperty<BytesToImage, byte[]> BytesProperty =
        AvaloniaProperty.RegisterDirect<BytesToImage, byte[]>(
            nameof(Bytes),
            o => o.Bytes,
            (o, v) => o.Bytes = v);

    /// <summary>
    /// Gets or sets bytes
    /// </summary>
    public byte[] Bytes {
        get { return _bytes; }
        set {
            _ints = MemoryMarshal.Cast<byte, int>(value).ToArray();
            SetAndRaise(BytesProperty, ref _bytes, value);
        }
    }

    static Color ThemeColor;
    const int SQUARE_SIZE = 8;
    const float THEME_VARIATION = 1f;

    public override unsafe void Render(DrawingContext Ctx)
    {
        if (_bytes == null || _bytes.Length == 0 || Double.IsNaN(Height) || Height == 0)
            return;

        int srcW = SQUARE_SIZE;
        int srcH = SQUARE_SIZE;

        int destW = Math.Max(1, (int)Width);
        int destH = Math.Max(1, (int)Height);

        using var destBitmap = new WriteableBitmap(new PixelSize(destW, destH), new Vector(96, 96), PixelFormat.Bgra8888);

        using (var locked = destBitmap.Lock()) {
            unsafe {
                var baseAddr = locked.Address;
                int bpp = locked.Format.BitsPerPixel / 8;

                if (_ints == null || _ints.Length == 0)
                    return;

                int cur = _ints[0];

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

                        cur += _ints[(x + y * srcW) % _ints.Length];
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
            }
        }

        Ctx.DrawImage(destBitmap, new Rect(0, 0, destW, destH), new Rect(0, 0, Width, Height));
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

    public BytesToImage()
    {
        InitializeComponent();
    }
}