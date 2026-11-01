using BencodeNET.Objects;

using System.Net;

namespace RTSharp.Shared.Utils;

public static partial class BencodeUtils
{
    private static void SetOrRemove(BDictionary Dict, string Key, IBObject? Value)
    {
        if (Value == null)
            Dict.Remove(Key);
        else
            Dict[Key] = Value;
    }

    private static string[] GetStringList(IBObject? Value) => Value switch {
        BString s => [ s.ToString() ],
        BList l => [.. l.AsStrings()],
        _ => []
    };

    /// <summary>
    /// Torrent display name
    /// </summary>
    public static string GetName(BDictionary Info) =>
        Info.Get<BString>("name")?.ToString() ??
        Info.Get<BString>("name.utf-8")?.ToString() ??
        throw new InvalidDataException("Torrent has no \"name\"");

    /// <summary>
    /// Sets the info "name", keeping "name.utf-8" in sync when it exists. Changes the info hash.
    /// </summary>
    public static void SetName(BDictionary Info, string Name)
    {
        Info["name"] = new BString(Name);

        if (Info.ContainsKey("name.utf-8"))
            Info["name.utf-8"] = new BString(Name);
    }

    /// <summary>
    /// The piece length (bytes per piece).
    /// </summary>
    public static long GetPieceLength(BDictionary Info) =>
        Info.Get<BNumber>("piece length")?.Value ??
        throw new InvalidDataException("Torrent has no \"piece length\"");

    /// <summary>
    /// The raw concatenated v1 SHA-1 piece hashes ("pieces"), or <c>null</c> when the
    /// info dictionary carries no v1 metadata (a v2-only torrent).
    /// </summary>
    public static byte[]? GetPieces(BDictionary Info) =>
        Info.Get<BString>("pieces")?.Value.ToArray();

    /// <summary>
    /// The optional free-text "comment" from the .torrent root, or <c>null</c> if absent.
    /// </summary>
    public static string? GetComment(BDictionary BTorrent) =>
        BTorrent.Get<BString>("comment")?.ToString() ??
        BTorrent.Get<BString>("comment.utf-8")?.ToString();

    /// <summary>
    /// Sets "comment", keeping "comment.utf-8" in sync when it exists
    /// </summary>
    public static void SetComment(BDictionary BTorrent, string? Comment)
    {
        var value = String.IsNullOrEmpty(Comment) ? null : new BString(Comment);
        SetOrRemove(BTorrent, "comment", value);

        if (BTorrent.ContainsKey("comment.utf-8"))
            SetOrRemove(BTorrent, "comment.utf-8", value);
    }

    /// <summary>
    /// Tracker tiers from "announce-list", falling back to a single tier of "announce"
    /// </summary>
    public static string[][] GetAnnounceTiers(BDictionary BTorrent)
    {
        var announceList = BTorrent.Get<BList>("announce-list");
        if (announceList != null) {
            var tiers = announceList.OfType<BList>().Select(t => t.AsStrings().ToArray()).Where(t => t.Length != 0).ToArray();
            if (tiers.Length != 0)
                return tiers;
        }

        var announce = BTorrent.Get<BString>("announce")?.ToString();
        return String.IsNullOrEmpty(announce) ? [] : [ [ announce ] ];
    }

    /// <summary>
    /// Writes "announce" (first URL) and "announce-list"
    /// </summary>
    public static void SetAnnounceTiers(BDictionary BTorrent, IReadOnlyList<string[]> Tiers)
    {
        var nonEmpty = Tiers.Where(t => t.Length != 0).ToArray();
        if (nonEmpty.Length == 0) {
            BTorrent.Remove("announce");
            BTorrent.Remove("announce-list");
            return;
        }

        BTorrent["announce"] = new BString(nonEmpty[0][0]);

        var announceList = new BList();
        foreach (var tier in nonEmpty)
            announceList.Value.Add(new BList(tier));
        BTorrent["announce-list"] = announceList;
    }

    /// <summary>
    /// BEP 19 web seeds from "url-list" (a single string or a list)
    /// </summary>
    public static string[] GetWebSeeds(BDictionary BTorrent) => GetStringList(BTorrent.Get<IBObject>("url-list"));

    public static void SetWebSeeds(BDictionary BTorrent, IReadOnlyList<string> Urls) =>
        SetOrRemove(BTorrent, "url-list", Urls.Count == 0 ? null : new BList(Urls));

    /// <summary>
    /// BEP 17 HTTP seeds from "httpseeds"
    /// </summary>
    public static string[] GetHttpSeeds(BDictionary BTorrent) => GetStringList(BTorrent.Get<IBObject>("httpseeds"));

    public static void SetHttpSeeds(BDictionary BTorrent, IReadOnlyList<string> Urls) =>
        SetOrRemove(BTorrent, "httpseeds", Urls.Count == 0 ? null : new BList(Urls));

    /// <summary>
    /// BEP 5 DHT bootstrap nodes from "nodes"
    /// </summary>
    public static IPEndPoint[] GetNodes(BDictionary BTorrent) =>
        [.. BTorrent.Get<BList>("nodes")?
            .OfType<BList>()
            .Where(x => x.Count == 2 && x[0] is BString && x[1] is BNumber { Value: > 0 and <= 65535 })
            .Select(x => IPAddress.TryParse(((BString)x[0]).ToString(), out var address) ? new IPEndPoint(address, (int)((BNumber)x[1]).Value) : null)
            .OfType<IPEndPoint>()
        ?? []];

    public static void SetNodes(BDictionary BTorrent, IReadOnlyList<IPEndPoint> Nodes)
    {
        if (Nodes.Count == 0) {
            BTorrent.Remove("nodes");
            return;
        }

        var list = new BList();
        foreach (var node in Nodes)
            list.Value.Add(new BList(new IBObject[] { new BString(node.Address.ToString()), new BNumber(node.Port) }));
        BTorrent["nodes"] = list;
    }

    public static string? GetCreatedBy(BDictionary BTorrent) =>
        BTorrent.Get<BString>("created by")?.ToString();

    public static void SetCreatedBy(BDictionary BTorrent, string? CreatedBy) =>
        SetOrRemove(BTorrent, "created by", String.IsNullOrEmpty(CreatedBy) ? null : new BString(CreatedBy));

    /// <returns>UTC</returns>
    public static DateTime? GetCreationDate(BDictionary BTorrent) =>
        BTorrent.Get<BNumber>("creation date") is { } n ? DateTimeOffset.FromUnixTimeSeconds(n.Value).UtcDateTime : null;

    /// <param name="CreationDate">Interpreted as UTC</param>
    public static void SetCreationDate(BDictionary BTorrent, DateTime? CreationDate) =>
        SetOrRemove(BTorrent, "creation date", CreationDate == null ? null : new BNumber(new DateTimeOffset(DateTime.SpecifyKind(CreationDate.Value, DateTimeKind.Utc)).ToUnixTimeSeconds()));

    public static string? GetEncoding(BDictionary BTorrent) =>
        BTorrent.Get<BString>("encoding")?.ToString();

    public static void SetEncoding(BDictionary BTorrent, string? Encoding) =>
        SetOrRemove(BTorrent, "encoding", String.IsNullOrEmpty(Encoding) ? null : new BString(Encoding));

    public static string? GetSource(BDictionary Info) =>
        Info.Get<BString>("source")?.ToString();

    /// <summary>
    /// Sets the info "source" tag. Changes the info hash.
    /// </summary>
    public static void SetSource(BDictionary Info, string? Source) =>
        SetOrRemove(Info, "source", String.IsNullOrEmpty(Source) ? null : new BString(Source));

    public static bool IsPrivate(BDictionary Info) =>
        Info.Get<BNumber>("private")?.Value == 1;

    /// <summary>
    /// Sets the info "private" flag. Changes the info hash.
    /// </summary>
    public static void SetPrivate(BDictionary Info, bool Private) =>
        SetOrRemove(Info, "private", Private ? new BNumber(1) : null);
}
