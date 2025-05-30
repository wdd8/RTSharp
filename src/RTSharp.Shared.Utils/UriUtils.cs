using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Utils
{
    public static class UriUtils
    {
        public static string GetDomainForTracker(string In)
        {
            if (In == null || !Uri.TryCreate(In, UriKind.Absolute, out var uri))
                return "";

            return String.IsNullOrEmpty(uri.Host) ? uri.AbsoluteUri : uri.Host;
        }
    }
}
