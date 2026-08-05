using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace RTSharp.Shared.Utils;

// as? xs? kt? mt? so? x.pe?
public record MagnetUri(Uri Uri, byte[]? V1InfoHash, byte[]? V2InfoHash, string? DisplayName, long? FileLength, string[]? Trackers, string[]? WebSeeds);

public class MagnetParser
{
    public static bool TryParse(string In, [NotNullWhen(true)] out MagnetUri? Result)
    {
        Result = null;

        if (!Uri.TryCreate(In, UriKind.Absolute, out var uri) || uri.Scheme != "magnet")
            return false;

        var query = HttpUtility.ParseQueryString(uri.Query);

        var exactTopic = query.GetValues("xt");

        byte[]? v1InfoHash = null;
        var v1Raw = exactTopic?.FirstOrDefault(x => x.StartsWith("urn:btih:"))?[9..];
        if (v1Raw != null) {
            var v1 = new byte[InfoHash.V1Length];
            if (v1Raw.Length == InfoHash.V1Length * 2) {
                if (Convert.FromHexString(v1Raw, v1, out _, out _) == System.Buffers.OperationStatus.Done)
                    v1InfoHash = v1;
            } else if (v1Raw.Length == InfoHash.V1Length * 8 / 5) {
                if (TryFromBase32(v1Raw, v1))
                    v1InfoHash = v1;
            }
        }

        byte[]? v2InfoHash = null;
        var v2Raw = exactTopic?.FirstOrDefault(x => x.StartsWith("urn:btmh:"))?[9..];
        if (v2Raw?.Length == (2 + InfoHash.V2Length) * 2) {
            Span<byte> multihash = stackalloc byte[2 + InfoHash.V2Length];
            if (Convert.FromHexString(v2Raw, multihash, out _, out _) == System.Buffers.OperationStatus.Done && multihash[0] == 0x12 && multihash[1] == InfoHash.V2Length)
                v2InfoHash = multihash[2..].ToArray();
        }

        if (v1InfoHash == null && v2InfoHash == null)
            return false;

        var displayName = query.Get("dn");
        var fileLength = query.Get("xl");
        var trackers = query.GetValues("tr");
        var webSeeds = query.GetValues("ws");

        Result = new MagnetUri(
            uri,
            v1InfoHash,
            v2InfoHash,
            displayName,
            fileLength != null && Int64.TryParse(fileLength, out var parsedFileLength) ? parsedFileLength : null,
            trackers,
            webSeeds
        );
        return true;
    }

    private static bool TryFromBase32(ReadOnlySpan<char> In, Span<byte> Out)
    {
        var buffer = 0u;
        var bits = 0;
        var pos = 0;

        foreach (var chr in In) {
            uint value;
            if (chr >= 'A' && chr <= 'Z')
                value = (uint)(chr - 'A');
            else if (chr >= 'a' && chr <= 'z')
                value = (uint)(chr - 'a');
            else if (chr >= '2' && chr <= '7')
                value = (uint)(chr - '2' + 26);
            else
                return false;

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8) {
                bits -= 8;
                if (pos == Out.Length)
                    return false;
                Out[pos++] = (byte)(buffer >> bits);
            }
        }

        return pos == Out.Length;
    }
}
