using RTSharp.Shared.Utils;

using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace RTSharp.Daemon.Services;

/// <summary>
/// Global persisted cache of v1 (SHA-1) <-> v2 (SHA-256) info-hash relation. Lets
/// v1-only backends like rtorrent or transmission report torrents under their rtsHash hash
/// (full 32-byte v2 when hybrid/v2, else the 20-byte v1) without re-reading every .torrent
/// on each poll/startup.
/// </summary>
public sealed class V1ToV2InfoHashCache
{
    private const string CacheFileName = "infohash-cache.bin";

    private const byte FlagV1Only = 0;
    private const byte FlagHybrid = 1;

    private const int RecordSizeV1Only = 1 + InfoHash.V1Length;
    private const int RecordSizeHybrid = RecordSizeV1Only + InfoHash.V2Length;

    private readonly record struct Key20(ulong A, ulong B, uint C)
    {
        public static Key20 From(ReadOnlySpan<byte> Hash) => new(
            BinaryPrimitives.ReadUInt64LittleEndian(Hash),
            BinaryPrimitives.ReadUInt64LittleEndian(Hash[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(Hash[16..]));

        public override int GetHashCode() => unchecked((int)A);
    }

    private readonly record struct Key32(ulong A, ulong B, ulong C, ulong D)
    {
        public static Key32 From(ReadOnlySpan<byte> Hash) => new(
            BinaryPrimitives.ReadUInt64LittleEndian(Hash),
            BinaryPrimitives.ReadUInt64LittleEndian(Hash[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(Hash[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(Hash[24..]));

        public override int GetHashCode() => unchecked((int)A);
    }

    // value null => known v1-only
    private readonly ConcurrentDictionary<Key20, byte[]?> V1ToV2 = new();
    private readonly ConcurrentDictionary<Key32, byte[]> V2ToV1 = new(); // hybrids only

    private readonly string CacheFilePath;
    private readonly ILogger<V1ToV2InfoHashCache> Logger;
    private readonly Lock FileLock = new();
    private FileStream? AppendStream;

    public V1ToV2InfoHashCache(IHostEnvironment Env, ILogger<V1ToV2InfoHashCache> Logger)
    {
        this.Logger = Logger;
        CacheFilePath = Path.Combine(Env.ContentRootPath, CacheFileName);

        Load();
    }

    /// <summary>
    /// Resolves the rtsHash hash for a v1 hash: the full v2 hash when the torrent is
    /// hybrid, the v1 hash when it is known v1-only, or false when the v1 hash has
    /// never been seen.
    /// </summary>
    public bool TryGetRTSHash(byte[] V1, out byte[] RTSHash)
    {
        if (V1.Length == InfoHash.V1Length && V1ToV2.TryGetValue(Key20.From(V1), out var v2)) {
            RTSHash = v2 ?? V1;
            return true;
        }

        RTSHash = null!;
        return false;
    }

    /// <summary>
    /// Translates a rtsHash hash to the underlying v1 hash. Identity for a 20-byte
    /// hash; looks up the mapped v1 for a 32-byte hash; null when the v2 hash is
    /// unknown.
    /// </summary>
    public byte[]? ToV1(ReadOnlySpan<byte> RTSHash)
    {
        if (!InfoHash.IsV2(RTSHash))
            return RTSHash.ToArray();

        return V2ToV1.TryGetValue(Key32.From(RTSHash), out var v1) ? v1 : null;
    }

    /// <summary>True when this v1 hash has been seen before (hybrid or known v1-only).</summary>
    public bool Contains(byte[] V1) =>
        V1.Length == InfoHash.V1Length && V1ToV2.ContainsKey(Key20.From(V1));

    /// <summary>
    /// Records a v1 <-> v2 relation (<paramref name="V2"/> null = known v1-only)
    /// </summary>
    public bool Add(byte[] V1, byte[]? V2)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(V1.Length, InfoHash.V1Length, nameof(V1));
        if (V2 != null)
            ArgumentOutOfRangeException.ThrowIfNotEqual(V2.Length, InfoHash.V2Length, nameof(V2));

        var key = Key20.From(V1);

        if (!V1ToV2.TryAdd(key, V2)) {
            if (V2 == null || !V1ToV2.TryUpdate(key, V2, null))
                return false;
        }

        if (V2 != null)
            V2ToV1.TryAdd(Key32.From(V2), V1);

        Persist(V1, V2);
        return true;
    }

    private void Persist(byte[] V1, byte[]? V2)
    {
        // [1 byte flag][20 bytes v1](+ [32 bytes v2] when flag == FlagHybrid)

        Span<byte> record = stackalloc byte[RecordSizeHybrid];
        record[0] = V2 != null ? FlagHybrid : FlagV1Only;
        V1.CopyTo(record[1..]);
        var length = RecordSizeV1Only;
        if (V2 != null) {
            V2.CopyTo(record[length..]);
            length = RecordSizeHybrid;
        }

        lock (FileLock) {
            try {
                AppendStream ??= new FileStream(CacheFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                AppendStream.Write(record[..length]);
                AppendStream.Flush();
            } catch (Exception ex) {
                Logger.LogWarning(ex, "Failed to persist infohash cache entry");
            }
        }
    }

    private void Load()
    {
        if (!File.Exists(CacheFilePath))
            return;

        try {
            using var fs = new FileStream(CacheFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var buf = new byte[checked((int)fs.Length)];
            fs.ReadExactly(buf);

            var good = 0;
            while (good < buf.Length) {
                var size = buf[good] switch {
                    FlagV1Only => RecordSizeV1Only,
                    FlagHybrid => RecordSizeHybrid,
                    _ => -1,
                };
                if (size < 0) {
                    Logger.LogWarning("Corrupt infohash cache record at offset {Offset}; truncating", good);
                    break;
                }
                if (good + size > buf.Length)
                    break; // truncated trailing record

                good += size;
            }

            var loaded = 0;
            for (var pos = 0; pos < good; loaded++) {
                var v1Span = buf.AsSpan(pos + 1, InfoHash.V1Length);
                if (buf[pos] == FlagV1Only) {
                    V1ToV2.TryAdd(Key20.From(v1Span), null);
                    pos += RecordSizeV1Only;
                } else {
                    var v1 = v1Span.ToArray();
                    var v2 = buf.AsSpan(pos + RecordSizeV1Only, InfoHash.V2Length).ToArray();
                    V1ToV2[Key20.From(v1)] = v2;
                    V2ToV1[Key32.From(v2)] = v1;
                    pos += RecordSizeHybrid;
                }
            }

            if (good < buf.Length)
                fs.SetLength(good); // drop the torn/corrupt trailing bytes

            Logger.LogDebug("Loaded {Count} infohash cache entries", loaded);
        } catch (Exception ex) {
            Logger.LogWarning(ex, "Failed to load infohash cache. Starting empty");
            V1ToV2.Clear();
            V2ToV1.Clear();
        }
    }
}
