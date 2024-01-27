using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Utils
{
	public static class UInt128Extensions
	{
		public static (ulong High, ulong Low) ToHighLow(this UInt128 In)
		{
			ulong low = (ulong)(In & 0xFFFFFFFF_FFFFFFFFUL);
			ulong high = (ulong)((In >> 64) & 0xFFFFFFFF_FFFFFFFFUL);

			return (high, low);
		}
	}
}
