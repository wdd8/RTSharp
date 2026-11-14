using Avalonia;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Sdcb.SimdCodecs.Ico;

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Xml.Linq;

namespace RTSharp.Shared.Controls;

public enum BuiltInIcons
{
    RTSHARP,
    CRASH,
    COMPUTER_KEY,
    DID_YOU_KNOW,
    DYNAMITE,
    KEYS,
    SERVER,
    CERTIFICATE,
    LINK_OK,
    LINK_BROKEN,
    PLANE,
    SEARCHING,

    SERIOUS_EXCLAMATION,
    SERIOUS_INFO,
    SERIOUS_QUESTION,
    SERIOUS_X,

    BLOCK,
    EXCLAMATION,
    INFO,
    OK,
    OK_EXCLAMATION,
    OK_WAIT,
    WAIT,
    QUESTION,
    X,
    X_EXCLAMATION,

    FILTER,
    START,
    PAUSE,
    STOP,
    SETTINGS,
    TORRENT_LISTING,
    ACTION_QUEUE,
    LOG,
    ARROW_RIGHT,
    REFRESH,
    NAVIGATE,
    NEW_FOLDER,
    COPY,
    REGENERATE,
    DELETE,
    DOWNLOAD,
    UPLOAD,
    WEB,
    BEHAVIOR,
    PREFERENCES,
    GENERAL,
    ADVANCED,
    CACHING,
    LOOK,
    NETWORK,
    BANDWIDTH,
    SPEED,
    QUEUE,
    BITTORRENT,
    FEED,

    STATE_DOWNLOADING,
    STATE_SEEDING,
    STATE_COMPLETE,
    STATE_PAUSED,
    STATE_STOPPED,
    STATE_HASHING,
    STATE_MOVING,
    STATE_ERRORED,
    STATE_ACTIVE,
    STATE_ERRORED_OVERLAY,
    STATE_ACTIVE_OVERLAY
}

public static class BuiltInIcon
{
    private static readonly ConcurrentDictionary<(BuiltInIcons Icon, int Frame), Bitmap> Cache = new();

    private static readonly ConcurrentDictionary<BuiltInIcons, (byte[] Data, IcoInfo Info)> IcoCache = new();

    private static readonly ConcurrentDictionary<BuiltInIcons, Geometry> GeometryCache = new();

    private static FrozenDictionary<BuiltInIcons, string> Icons = new Dictionary<BuiltInIcons, string>() {
        { BuiltInIcons.RTSHARP, "rtsharp" },
        { BuiltInIcons.CRASH, "crash" },
        { BuiltInIcons.COMPUTER_KEY, "computer_key" },
        { BuiltInIcons.DID_YOU_KNOW, "did_you_know" },
        { BuiltInIcons.DYNAMITE, "dynamite" },
        { BuiltInIcons.KEYS, "keys" },
        { BuiltInIcons.SERVER, "server" },
        { BuiltInIcons.CERTIFICATE, "certificate" },
        { BuiltInIcons.LINK_OK, "link_ok" },
        { BuiltInIcons.LINK_BROKEN, "link_broken" },
        { BuiltInIcons.PLANE, "plane" },
        { BuiltInIcons.SEARCHING, "searching" },

        { BuiltInIcons.SERIOUS_EXCLAMATION, "serious_exclamation" },
        { BuiltInIcons.SERIOUS_INFO, "serious_info" },
        { BuiltInIcons.SERIOUS_QUESTION, "serious_question" },
        { BuiltInIcons.SERIOUS_X, "serious_X" },

        { BuiltInIcons.BLOCK, "block" },
        { BuiltInIcons.EXCLAMATION, "exclamation" },
        { BuiltInIcons.INFO, "info" },
        { BuiltInIcons.OK, "ok" },
        { BuiltInIcons.OK_EXCLAMATION, "ok_exclamation" },
        { BuiltInIcons.OK_WAIT, "ok_wait" },
        { BuiltInIcons.WAIT, "wait" },
        { BuiltInIcons.QUESTION, "question" },
        { BuiltInIcons.X, "X" },
        { BuiltInIcons.X_EXCLAMATION, "X_exclamation" },

        { BuiltInIcons.FILTER, "filter" },
        { BuiltInIcons.START, "start" },
        { BuiltInIcons.PAUSE, "pause" },
        { BuiltInIcons.STOP, "stop" },
        { BuiltInIcons.SETTINGS, "settings" },
        { BuiltInIcons.TORRENT_LISTING, "torrent_listing" },
        { BuiltInIcons.ACTION_QUEUE, "action_queue" },
        { BuiltInIcons.LOG, "log" },
        { BuiltInIcons.ARROW_RIGHT, "arrow_right" },
        { BuiltInIcons.REFRESH, "refresh" },
        { BuiltInIcons.NAVIGATE, "navigate" },
        { BuiltInIcons.NEW_FOLDER, "new_folder" },
        { BuiltInIcons.COPY, "copy" },
        { BuiltInIcons.REGENERATE, "regenerate" },
        { BuiltInIcons.DELETE, "delete" },
        { BuiltInIcons.DOWNLOAD, "download" },
        { BuiltInIcons.UPLOAD, "upload" },
        { BuiltInIcons.WEB, "web" },
        { BuiltInIcons.BEHAVIOR, "behavior" },
        { BuiltInIcons.PREFERENCES, "preferences" },
        { BuiltInIcons.GENERAL, "general" },
        { BuiltInIcons.ADVANCED, "advanced" },
        { BuiltInIcons.CACHING, "caching" },
        { BuiltInIcons.LOOK, "look" },
        { BuiltInIcons.NETWORK, "network" },
        { BuiltInIcons.BANDWIDTH, "bandwidth" },
        { BuiltInIcons.SPEED, "speed" },
        { BuiltInIcons.QUEUE, "queue" },
        { BuiltInIcons.BITTORRENT, "bittorrent" },
        { BuiltInIcons.FEED, "feed" },

        { BuiltInIcons.STATE_DOWNLOADING, "state_downloading" },
        { BuiltInIcons.STATE_SEEDING, "state_seeding" },
        { BuiltInIcons.STATE_COMPLETE, "state_complete" },
        { BuiltInIcons.STATE_PAUSED, "state_paused" },
        { BuiltInIcons.STATE_STOPPED, "state_stopped" },
        { BuiltInIcons.STATE_HASHING, "state_hashing" },
        { BuiltInIcons.STATE_MOVING, "state_moving" },
        { BuiltInIcons.STATE_ERRORED, "state_errored" },
        { BuiltInIcons.STATE_ACTIVE, "state_active" },
        { BuiltInIcons.STATE_ERRORED_OVERLAY, "state_errored_overlay" },
        { BuiltInIcons.STATE_ACTIVE_OVERLAY, "state_active_overlay" },
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<BuiltInIcons, BuiltInIcons> Overlays = new Dictionary<BuiltInIcons, BuiltInIcons>() {
        { BuiltInIcons.STATE_ERRORED_OVERLAY, BuiltInIcons.STATE_ERRORED },
        { BuiltInIcons.STATE_ACTIVE_OVERLAY, BuiltInIcons.STATE_ACTIVE },
    }.ToFrozenDictionary();

    private const string MaskSuffix = ".mask";

    private static FrozenDictionary<string, Uri>? Files;

    /// <summary>
    /// Changes each time the icons are cleared, so a control can tell its icon came from an earlier theme
    /// </summary>
    public static int Generation { get; private set; }

    /// <summary>
    /// Drops the loaded icons, for the next ones to come from the theme in use
    /// </summary>
    public static void Clear()
    {
        Files = null;
        Cache.Clear();
        IcoCache.Clear();
        GeometryCache.Clear();
        Generation++;
    }

    public static string FileName(BuiltInIcons Icon) => Icons[Icon];

    private static Uri Find(BuiltInIcons Icon)
    {
        var files = Files ??= AssetLoader
            .GetAssets(new Uri("avares://RTSharp.Theme/Icons/"), null)
            .ToFrozenDictionary(x => {
                var name = Path.GetFileNameWithoutExtension(x.AbsolutePath);
                return name.EndsWith(MaskSuffix, StringComparison.Ordinal) ? name[..^MaskSuffix.Length] : name;
            });

        return files.TryGetValue(Icons[Icon], out var uri) ? uri : throw new FileNotFoundException($"The theme has no icon {Icons[Icon]} in its Icons folder");
    }

    public static Bitmap Get(BuiltInIcons Icon, int Size = Int32.MaxValue)
    {
        var uri = Find(Icon);
        if (!uri.AbsolutePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) {
            return Cache.GetOrAdd((Icon, 0), _ => {
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            });
        }

        var (data, info) = IcoCache.GetOrAdd(Icon, _ => {
            using var stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            return (bytes, IcoDecoder.ReadInfo(bytes));
        });

        var (index, frame) = info.Entries
            .Index()
            .OrderBy(x => x.Item.Width < Size)
            .ThenBy(x => x.Item.Width < Size ? -x.Item.Width : x.Item.Width)
            .ThenByDescending(x => x.Item.BitsPerPixel)
            .First();

        return Cache.GetOrAdd((Icon, index), _ => {
            var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock()) {
                unsafe {
                    IcoDecoder.DecodeTo(data, index, new Span<byte>((void*)buffer.Address, buffer.RowBytes * frame.Height), Sdcb.SimdCodecs.Abstractions.PixelFormat.Rgba32, buffer.RowBytes);
                }
            }
            return bitmap;
        });
    }

    public static IGifSource GetGif(BuiltInIcons Icon) => GifStreamSource.FromUri(Find(Icon));

    public static bool IsSvg(BuiltInIcons Icon) => Find(Icon).AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    public static bool IsMask(BuiltInIcons Icon) => Path.GetFileNameWithoutExtension(Find(Icon).AbsolutePath).EndsWith(MaskSuffix, StringComparison.Ordinal);

    public static Geometry GetGeometry(BuiltInIcons Icon) => GeometryCache.GetOrAdd(Icon, icon => {
        XDocument svg;
        using (var stream = AssetLoader.Open(Find(icon)))
            svg = XDocument.Load(stream);

        var paths = svg.Descendants(svg.Root!.Name.Namespace + "path").ToList();
        if (paths.Count == 0)
            throw new InvalidOperationException($"{Find(icon)} contains no path");

        // SVG fills nonzero unless set to evenodd, while Avalonia path data is even-odd unless it starts with F1
        var fillRule = paths.Any(x => (string?)x.Attribute("fill-rule") == "evenodd") ? "F0" : "F1";
        return Geometry.Parse($"{fillRule} {String.Join(' ', paths.Select(x => (string?)x.Attribute("d")))}");
    });
}
