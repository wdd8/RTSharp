using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RTSharp.Core
{
    public static class StringCache
    {
        public sealed class WeakReferenceStringEqualityComparer : IEqualityComparer<WeakReference<string>>, IAlternateEqualityComparer<string, WeakReference<string>>
        {
            public static IEqualityComparer<WeakReference<string>> Default { get; } = new WeakReferenceStringEqualityComparer();

            public WeakReference<string> Create(string alternate) => new WeakReference<string>(alternate);
            public bool Equals(string alternate, WeakReference<string> other)
            {
                if (other.TryGetTarget(out var target)) {
                    return alternate == target;
                }

                return false;
            }

            public bool Equals(WeakReference<string> x, WeakReference<string> y) => x == y;
            public int GetHashCode(string alternate) => alternate.GetHashCode();
            public int GetHashCode([DisallowNull] WeakReference<string> obj)
            {
                if (obj.TryGetTarget(out var target)) {
                    return target.GetHashCode();
                }

                return 0;
            }
        }

        private static HashSet<WeakReference<string>> Cache = new(WeakReferenceStringEqualityComparer.Default);
        private static HashSet<WeakReference<string>>.AlternateLookup<string> Lookup = Cache.GetAlternateLookup<string>();

        public static string Reuse(string In)
        {
            if (Lookup.TryGetValue(In, out var ret)) {
                if (ret.TryGetTarget(out var rett))
                    return rett;

                ret.SetTarget(In);
                return In;
            }

            Cache.Add(new WeakReference<string>(In));
            return In;
        }
    }
}
