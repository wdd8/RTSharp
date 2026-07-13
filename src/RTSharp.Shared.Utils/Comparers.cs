using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.Shared.Utils
{
    public class HashBytesComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public static HashBytesComparer Default => field ??= new HashBytesComparer();

        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();

        public bool Equals(byte[]? obj1, byte[]? obj2)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;
            if (obj1 is null || obj2 is null)
                return false;
            return obj1.AsSpan().SequenceEqual(obj2);
        }

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode(byte[] obj) => GetHashCode(obj.AsSpan());

        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            if (alternate.Length >= sizeof(ulong)) {
                var h = unchecked(
                    BinaryPrimitives.ReadUInt64LittleEndian(alternate) *
                    0x9E3779B97F4A7C15 +
                    BinaryPrimitives.ReadUInt64LittleEndian(alternate[^sizeof(ulong)..])
                ); // ???
                return unchecked((int)(h ^ (h >> 32))); // mix
            }

            var hc = new HashCode();
            hc.AddBytes(alternate);
            return hc.ToHashCode();
        }
    }

    public class StringArrayComparer : IEqualityComparer<string[]>
    {
        public static StringArrayComparer Default => field ??= new StringArrayComparer();

        public bool Equals(string[]? x, string[]? y) => x.SequenceEqual(y);
        public int GetHashCode([DisallowNull] string[] obj)
        {
            var h = new HashCode();
            foreach (var s in obj) {
                h.Add(s);
            }
            return h.ToHashCode();
        }
    }

    public class HashBytesGuidComparer : IEqualityComparer<(byte[], Guid)>
    {
        public static HashBytesGuidComparer Default => field ??= new HashBytesGuidComparer();

        public bool Equals((byte[], Guid) obj1, (byte[], Guid) obj2)
        {
            return obj1.Item1.AsSpan().SequenceEqual(obj2.Item1) && obj1.Item2.Equals(obj2.Item2);
        }

        public int GetHashCode((byte[], Guid) obj)
        {
            // Hash the array's contents (matching Equals), not its reference.
            return HashCode.Combine(HashBytesComparer.Default.GetHashCode(obj.Item1), obj.Item2);
        }
    }
}