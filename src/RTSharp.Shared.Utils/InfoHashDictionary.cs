﻿using System.Collections.Concurrent;

namespace RTSharp.Shared.Utils
{
    public class InfoHashDictionary<T> : Dictionary<byte[], T>
    {
        public InfoHashDictionary() : base(ByteArrayComparer.Default)
        {
        }
    }

    public class ConcurrentInfoHashDictionary<T> : ConcurrentDictionary<byte[], T>
    {
        public ConcurrentInfoHashDictionary() : base(ByteArrayComparer.Default)
        {
        }
    }

    public class ConcurrentInfoHashOwnerDictionary<T> : ConcurrentDictionary<(byte[], Guid), T>
    {
        public ConcurrentInfoHashOwnerDictionary() : base(ByteArrayGuidComparer.Default)
        {
        }
    }

    public static class InfoHashDictionaryExtensions
    {
        public static InfoHashDictionary<T> ToInfoHashDictionary<T>(this IEnumerable<T> source, Func<T, byte[]> infoHashSelector)
        {
            var ret = new InfoHashDictionary<T>();

            foreach (var el in source) {
                ret.Add(infoHashSelector(el), el);
            }

            return ret;
        }

        public static InfoHashDictionary<TTarget> ToInfoHashDictionary<T, TTarget>(this IEnumerable<T> source, Func<T, byte[]> infoHashSelector, Func<T, TTarget> elSelector)
        {
            var ret = new InfoHashDictionary<TTarget>();

            foreach (var el in source) {
                ret.Add(infoHashSelector(el), elSelector(el));
            }

            return ret;
        }
    }
}
