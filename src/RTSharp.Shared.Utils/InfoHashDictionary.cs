namespace RTSharp.Shared.Utils
{
    public class InfoHashDictionary<T> : Dictionary<byte[], T>
    {
        public InfoHashDictionary() : base(ByteArrayComparer.Default)
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
