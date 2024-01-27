using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Utils
{
	public static class FlagsMapper
	{
		public static TDst Map<TSrc, TDst>(TSrc In, Func<TSrc, TDst> FxMap)
			where TSrc : struct, Enum
			where TDst : struct, Enum
		{
			int ret = 0;
			if (!Enum.TryParse<TSrc>("Max", out var srcMax)) {
				throw new ArgumentException("Source enum does not have a max value", nameof(TSrc));
			}

			for (var x = 1;x <= CastTo<int>.From(srcMax);x *= 2) {
				ret |= In.HasFlag(CastTo<TSrc>.From(x)) ? CastTo<int>.From(FxMap(CastTo<TSrc>.From(x))) : 0;
			}
			return CastTo<TDst>.From(ret);
		}

		public static string MapConcat<TSrc>(TSrc In, Func<TSrc, string> FxMap, string JoiningString = null)
			where TSrc : struct, Enum
		{
			if (!Enum.TryParse<TSrc>("Max", out var srcMax)) {
				throw new ArgumentException("Source enum does not have a max value", nameof(TSrc));
			}

			var ret = new StringBuilder();
			for (var x = 1;x <= CastTo<int>.From(srcMax);x *= 2) {
				if (In.HasFlag(CastTo<TSrc>.From(x)))
					ret.Append(FxMap(CastTo<TSrc>.From(x)) + (JoiningString ?? ""));
			}
			return ret.ToString()[..^(JoiningString?.Length ?? 0)];
		}
	}
}
