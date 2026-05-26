using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

using IniParser;

using Xdg.Directories;

namespace RTSharp.Core.Services;

public static partial class FileIconService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<Bitmap?> GetIconAsync(string fileName, bool isDirectory)
    {
        string key = isDirectory ? "\0folder" : Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(key)) key = "\0file";
        return _cache.GetOrAdd(key, _ =>
            new Lazy<Task<Bitmap?>>(() =>
                LoadIcon(fileName, isDirectory), LazyThreadSafetyMode.ExecutionAndPublication
            )
        ).Value;
    }

    private static async Task<Bitmap?> LoadIcon(string fileName, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
            return GetWindowsIcon(fileName, isDirectory);
        if (OperatingSystem.IsLinux())
            return await GetLinuxIcon(fileName, isDirectory);

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static WriteableBitmap? GetWindowsIcon(string fileName, bool isDirectory)
    {
        var ext = Path.GetExtension(fileName);
        var fileInfo = new SHFILEINFOW();

        var ret = SHGetFileInfo(isDirectory ? "a" : $"a{ext}",
            isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL,
            ref fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

        if (ret == 0 || fileInfo.hIcon == IntPtr.Zero)
            return null;

        var bitmap = HIconToWriteableBitmap(fileInfo.hIcon);
        DestroyIcon(fileInfo.hIcon);
        return bitmap;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe WriteableBitmap? HIconToWriteableBitmap(IntPtr hIcon)
    {
        const int SIZE = 16;
        var bmi = new BITMAPINFOHEADER {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = SIZE, biHeight = -SIZE,
            biPlanes = 1, biBitCount = 32, biCompression = 0
        };

        IntPtr hDC = CreateCompatibleDC(IntPtr.Zero);
        if (hDC == IntPtr.Zero)
            return null;

        IntPtr hBmp = CreateDIBSection(hDC, ref bmi, 0, out var ppvBits, IntPtr.Zero, 0);
        if (hBmp == IntPtr.Zero) {
            DeleteDC(hDC);
            return null;
        }

        IntPtr hOld = SelectObject(hDC, hBmp);
        DrawIconEx(hDC, 0, 0, hIcon, SIZE, SIZE, 0, IntPtr.Zero, DI_NORMAL);
        var wb = new WriteableBitmap(new PixelSize(SIZE, SIZE), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);

        using (var fb = wb.Lock())
            Buffer.MemoryCopy((void*)ppvBits, (void*)fb.Address, SIZE * SIZE * 4, SIZE * SIZE * 4);

        SelectObject(hDC, hOld);
        DeleteObject(hBmp);
        DeleteDC(hDC);
        return wb;
    }

    private static readonly Lazy<Dictionary<string, string>> MimeGlobs = new(LoadMimeGlobs, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Task<Bitmap?> GetLinuxIcon(string fileName, bool isDirectory)
    {
        string iconName;
        string category;
        if (isDirectory) {
            iconName = "folder";
            category = "places";
        } else {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!MimeGlobs.Value.TryGetValue(ext, out var mimeType))
                return Task.FromResult<Bitmap?>(null);

            iconName = mimeType.Replace('/', '-');
            category = "mimetypes";
        }

        return LoadIconFromTheme(iconName, category);
    }

    private static async Task<Bitmap?> LoadIconFromTheme(string iconName, string category)
    {
        var theme = await GetKdeIconTheme() ?? "breeze";
        var dataDirs = BaseDirectory.DataDirs.Prepend(BaseDirectory.DataHome);

        // KDE-style layout: <theme>/<category>/<size>/<icon>
        foreach (var dir in dataDirs) {
            var themeDir = Path.Combine(dir, "icons", theme);

            foreach (var size in new[] { "16", "22", "24" }) {
                var bmp = TryLoadIconFile(Path.Combine(themeDir, category, size, iconName));
                if (bmp != null)
                    return bmp;
            }
        }

        // Freedesktop hicolor fallback: <theme>/<size>x<size>/<category>/<icon>
        foreach (var dir in dataDirs) {
            var hicolor = Path.Combine(dir, "icons", "hicolor");

            foreach (var size in new[] { "16", "22", "24" }) {
                var bmp = TryLoadIconFile(Path.Combine(hicolor, $"{size}x{size}", category, iconName));
                if (bmp != null)
                    return bmp;
            }
        }

        return null;
    }

    private static Bitmap? TryLoadIconFile(string pathWithoutExt)
    {
        var svg = pathWithoutExt + ".svg";
        if (File.Exists(svg)) {
            var bmp = LoadSvg(svg, 16);
            if (bmp != null)
                return bmp;
        }

        var png = pathWithoutExt + ".png";
        if (File.Exists(png)) {
            try {
                return new Bitmap(png);
            } catch { }
        }

        return null;
    }

    private static unsafe WriteableBitmap? LoadSvg(string path, int size)
    {
        try {
            using var svg = new Svg.Skia.SKSvg();
            var picture = svg.Load(path);
            if (picture == null)
                return null;

            var r = picture.CullRect;
            float scale = size / MathF.Max(r.Width, r.Height);
            int w = Math.Max(1, (int)MathF.Round(r.Width * scale));
            int h = Math.Max(1, (int)MathF.Round(r.Height * scale));

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var skBmp = new SKBitmap(info);
            using var canvas = new SKCanvas(skBmp);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);

            var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
            using (var fb = wb.Lock()) {
                var span = skBmp.GetPixelSpan();
                fixed (byte* src = span)
                    Buffer.MemoryCopy(src, (void*)fb.Address, span.Length, span.Length);
            }

            return wb;
        } catch {
            return null;
        }
    }

    private static Dictionary<string, string> LoadMimeGlobs()
    {
        var result = new Dictionary<string, string>();

        foreach (var dir in new[] { BaseDirectory.DataHome }.Concat(BaseDirectory.DataDirs)) {
            // globs2 has priority: weight:mime/type:*.ext
            // globs is fallback: mime/type:*.ext

            foreach (var (file, hasWeight) in new[] { ("globs2", true), ("globs", false) }) {
                var path = Path.Combine(dir, "mime", file);
                if (!File.Exists(path))
                    continue;

                foreach (var line in File.ReadLines(path)) {
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    var parts = line.Split(':');

                    string mime;
                    string glob;
                    if (hasWeight && parts.Length >= 3) {
                        mime = parts[1];
                        glob = parts[2];
                    } else if (!hasWeight && parts.Length >= 2) {
                        mime = parts[0];
                        glob = parts[1];
                    } else
                        continue;

                    if (!glob.StartsWith("*.") || glob.IndexOfAny(['*', '?', '/'], 1) >= 0)
                        continue;

                    result.TryAdd(glob[1..], mime); // ".ext" as key
                }
                break;
            }
        }
        return result;
    }

    private static async Task<string?> GetKdeIconTheme()
    {
        var cfg = Path.Combine(BaseDirectory.ConfigHome, "kdeglobals");
        if (!File.Exists(cfg))
            return null;

#pragma warning disable CS0618 // Type or member is obsolete
        var theme = new StringIniParser().ParseString(await File.ReadAllTextAsync(cfg))["Icons"]["Theme"];
#pragma warning restore CS0618 // Type or member is obsolete
        return string.IsNullOrEmpty(theme) ? null : theme;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed ushort szDisplayName[260];
        public fixed ushort szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint DI_NORMAL = 0x3;

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbSizeFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr ho);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(IntPtr hdc, int xLeft, int yLeft, IntPtr hIcon, int cxWidth, int cyHeight, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
}
