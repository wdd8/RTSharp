using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Utils
{
	public static class UriUtils
	{
		public static string GetDomainForTracker(Uri In)
		{
			if (In == null)
				return "";

			return String.IsNullOrEmpty(In.Host) ? In.AbsoluteUri : In.Host;
		}
	}
}
