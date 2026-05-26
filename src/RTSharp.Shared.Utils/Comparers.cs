using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.Shared.Utils
{
    public class ByteArrayComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public static ByteArrayComparer Default => field ??= new ByteArrayComparer();

        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();

        public bool Equals(byte[]? obj1, byte[]? obj2)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(obj1, obj2);
        }

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode(byte[] obj)
        {
            var h = new HashCode();
            h.AddBytes(obj);
            return h.ToHashCode();
        }

        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            var h = new HashCode();
            h.AddBytes(alternate);
            return h.ToHashCode();
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

    public class ByteArrayGuidComparer : IEqualityComparer<(byte[], Guid)>
    {
        public static ByteArrayGuidComparer Default => field ??= new ByteArrayGuidComparer();

        public bool Equals((byte[], Guid) obj1, (byte[], Guid) obj2)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(obj1, obj2);
        }

        public int GetHashCode((byte[], Guid) obj)
        {
            return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
        }
    }
}