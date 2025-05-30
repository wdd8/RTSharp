using System.Collections;

namespace RTSharp.Shared.Utils
{
    public class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        private static ByteArrayComparer _default;

        public static ByteArrayComparer Default {
            get {
                if (_default == null) {
                    _default = new ByteArrayComparer();
                }

                return _default;
            }
        }

        public bool Equals(byte[] obj1, byte[] obj2)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(obj1, obj2);
        }

        public int GetHashCode(byte[] obj)
        {
            return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
        }
    }

    public class ByteArrayGuidComparer : IEqualityComparer<(byte[], Guid)>
    {
        private static ByteArrayGuidComparer _default;

        public static ByteArrayGuidComparer Default {
            get {
                if (_default == null) {
                    _default = new ByteArrayGuidComparer();
                }

                return _default;
            }
        }

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