using System.Security.Cryptography;

namespace RTSharp.Shared.Utils;

/// <summary>
/// Helpers for RTSHash (RT# native) info hashes. RTSHash is the full 32-byte v2
/// (SHA-256) hash when the torrent has one, otherwise the 20-byte v1 (SHA-1) hash.
/// </summary>
public static class InfoHash
{
    /// <summary>Length of a v1 (SHA-1) info hash, in bytes.</summary>
    public const int V1Length = SHA1.HashSizeInBytes;

    /// <summary>Length of a v2 (SHA-256) info hash, in bytes.</summary>
    public const int V2Length = SHA256.HashSizeInBytes;

    /// <summary>True when <paramref name="hash"/> is a full 32-byte v2 hash.</summary>
    public static bool IsV2(ReadOnlySpan<byte> hash) => hash.Length == V2Length;

    /// <summary>
    /// First 20 bytes of a hash: truncates a v2 hash to the identifier some backends key v2 torrents by;
    /// identity (copy) for a v1 hash, so it maps any rtsHash to such a backend's native key.
    /// </summary>
    public static byte[] TruncateV2(ReadOnlySpan<byte> hash) => hash[..V1Length].ToArray();
}
