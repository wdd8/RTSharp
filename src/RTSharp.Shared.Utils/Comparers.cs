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
            if (ReferenceEquals(obj1, obj2))
                return true;
            if (obj1 is null || obj2 is null)
                return false;
            return obj1.AsSpan().SequenceEqual(obj2);
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
            return obj1.Item1.AsSpan().SequenceEqual(obj2.Item1) && obj1.Item2.Equals(obj2.Item2);
        }

        public int GetHashCode((byte[], Guid) obj)
        {
            return HashCode.Combine(obj.Item1, obj.Item2);
        }
    }
}