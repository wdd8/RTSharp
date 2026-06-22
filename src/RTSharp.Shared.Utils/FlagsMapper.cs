using System;
using System.Text;

namespace RTSharp.Shared.Utils
{
    public static class FlagsMapper
    {
        private static class MaxValue<TSrc> where TSrc : struct, Enum
        {
            public static readonly int Value = Enum.TryParse<TSrc>("Max", out var srcMax) ? CastTo<int>.From(srcMax) : throw new ArgumentException("Source enum does not have a max value", nameof(TSrc));
        }

        public static TDst Map<TSrc, TDst>(TSrc In, Func<TSrc, TDst> FxMap)
            where TSrc : struct, Enum
            where TDst : struct, Enum
        {
            int ret = 0;
            int max = MaxValue<TSrc>.Value;

            for (var x = 1;x <= max;x *= 2) {
                ret |= In.HasFlag(CastTo<TSrc>.From(x)) ? CastTo<int>.From(FxMap(CastTo<TSrc>.From(x))) : 0;
            }
            return CastTo<TDst>.From(ret);
        }

        public static string MapConcat<TSrc>(TSrc In, Func<TSrc, string> FxMap, string? JoiningString = null)
            where TSrc : struct, Enum
        {
            int max = MaxValue<TSrc>.Value;

            var ret = new StringBuilder();
            var joiner = JoiningString ?? "";
            bool first = true;
            for (var x = 1;x <= max;x *= 2) {
                if (In.HasFlag(CastTo<TSrc>.From(x))) {
                    if (!first)
                        ret.Append(joiner);
                    ret.Append(FxMap(CastTo<TSrc>.From(x)));
                    first = false;
                }
            }
            return ret.ToString();
        }
    }
}
