using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace RTSharp.Shared.Utils;

// as? xs? kt? mt? so? x.pe?
public record MagnetUri(Uri Uri, byte[]? V1InfoHash, byte[]? V2InfoHash, string? DisplayName, int? FileLength, string[]? Trackers, string[]? WebSeeds);

public class MagnetParser
{
    public static bool TryParse(string In, [NotNullWhen(true)] out MagnetUri? Result)
    {
        if (!Uri.TryCreate(In, UriKind.Absolute, out var uri)) {
            Result = null;
            return false;
        }

        var query = HttpUtility.ParseQueryString(uri.Query);

        var exactTopic = query.GetValues("xt");

        var v1InfoHash = exactTopic?.FirstOrDefault(x => x.StartsWith("urn:btih:"))?[9..];
        var v2InfoHash = exactTopic?.FirstOrDefault(x => x.StartsWith("urn:btmh:"))?[9..];
        var displayName = query.Get("dn");
        var fileLength = query.Get("xl");
        var trackers = query.GetValues("tr");
        var webSeeds = query.GetValues("ws");

        Result = new MagnetUri(
            uri,
            v1InfoHash != null ? Convert.FromHexString(v1InfoHash) : null,
            v2InfoHash != null ? Convert.FromHexString(v2InfoHash) : null,
            displayName,
            fileLength != null ? Int32.Parse(fileLength) : null,
            trackers,
            webSeeds
        );
        return true;
    }
}
