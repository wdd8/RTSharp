using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
    FLAT_QUESTION_MARK,
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
    SERIOUS2_INFO,
    SERIOUS2_X,

    VISTA_BLOCK,
    VISTA_EXCLAMATION,
    VISTA_INFO,
    VISTA_OK,
    VISTA_OK_EXCLAMATION,
    VISTA_OK_WAIT,
    VISTA_WAIT,
    VISTA_X,
    VISTA_X_EXCLAMATION,

    WIN10_EXCLAMATION,
    WIN10_EXCLAMATION_SHIELD,
    WIN10_INFO,
    WIN10_OK_SHIELD,
    WIN10_QUESTION_SHIELD,
    WIN10_X,
    WIN10_X_SHIELD,

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
    UPDATE_INTERVAL
}

public static class BuiltInIcon
{
    private static readonly ConcurrentDictionary<BuiltInIcons, Bitmap> Cache = new();

    private static readonly ConcurrentDictionary<BuiltInIcons, Geometry> GeometryCache = new();

    private static FrozenDictionary<BuiltInIcons, string> Icons = new Dictionary<BuiltInIcons, string>() {
        { BuiltInIcons.RTSHARP, "rtsharp" },
        { BuiltInIcons.CRASH, "crash" },
        { BuiltInIcons.COMPUTER_KEY, "computer_key" },
        { BuiltInIcons.DID_YOU_KNOW, "did_you_know" },
        { BuiltInIcons.DYNAMITE, "dynamite" },
        { BuiltInIcons.FLAT_QUESTION_MARK, "flat_question_mark" },
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
        { BuiltInIcons.SERIOUS2_INFO, "serious2_info" },
        { BuiltInIcons.SERIOUS2_X, "serious2_X" },

        { BuiltInIcons.VISTA_BLOCK, "vista_block" },
        { BuiltInIcons.VISTA_EXCLAMATION, "vista_exclamation" },
        { BuiltInIcons.VISTA_INFO, "vista_info" },
        { BuiltInIcons.VISTA_OK, "vista_ok" },
        { BuiltInIcons.VISTA_OK_EXCLAMATION, "vista_ok_exclamation" },
        { BuiltInIcons.VISTA_OK_WAIT, "vista_ok_wait" },
        { BuiltInIcons.VISTA_WAIT, "vista_wait" },
        { BuiltInIcons.VISTA_X, "vista_X" },
        { BuiltInIcons.VISTA_X_EXCLAMATION, "vista_X_exclamation" },

        { BuiltInIcons.WIN10_EXCLAMATION, "win10_exclamation" },
        { BuiltInIcons.WIN10_EXCLAMATION_SHIELD, "win10_exclamation_shield" },
        { BuiltInIcons.WIN10_INFO, "win10_info" },
        { BuiltInIcons.WIN10_OK_SHIELD, "win10_ok_shield" },
        { BuiltInIcons.WIN10_QUESTION_SHIELD, "win10_question_shield" },
        { BuiltInIcons.WIN10_X, "win10_X" },
        { BuiltInIcons.WIN10_X_SHIELD, "win10_X_shield" },

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
        { BuiltInIcons.UPDATE_INTERVAL, "update_interval" },
    }.ToFrozenDictionary();

    private static readonly Lazy<FrozenDictionary<string, Uri>> Files = new(() =>
        AssetLoader
            .GetAssets(new Uri("avares://RTSharp.Theme/Icons/"), null)
            .ToFrozenDictionary(x => Path.GetFileNameWithoutExtension(x.AbsolutePath))
    );

    private static Uri Find(BuiltInIcons Icon) =>
        Files.Value.TryGetValue(Icons[Icon], out var uri) ? uri : throw new FileNotFoundException($"The theme has no icon {Icons[Icon]} in its Icons folder");

    public static Bitmap Get(BuiltInIcons Icon) => Cache.GetOrAdd(Icon, icon => new Bitmap(AssetLoader.Open(Find(icon))));

    public static IGifSource GetGif(BuiltInIcons Icon) => GifStreamSource.FromUri(Find(Icon));

    public static bool IsSvg(BuiltInIcons Icon) => Find(Icon).AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    public static IImage GetImage(BuiltInIcons Icon)
    {
        if (!IsSvg(Icon))
            return Get(Icon);

        var app = Application.Current!;
        return new DrawingImage() {
            Drawing = new GeometryDrawing() {
                Geometry = GetGeometry(Icon),
                Brush = (IBrush?)app.FindResource(app.ActualThemeVariant, "SystemControlForegroundBaseHighBrush")
            }
        };
    }

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
