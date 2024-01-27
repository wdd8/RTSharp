using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
	public static class ConfigExtensions
	{
		public static string GetServerUri(this IConfigurationRoot In)
		{
			return In.GetSection("Server:Uri").Value;
		}

		public static string GetServerFileTransferUri(this IConfigurationRoot In)
		{
			return In.GetSection("Server:FileTransferUri").Value;
		}

		public static TimeSpan GetPollInterval(this IConfigurationRoot In)
		{
			return In.GetSection("Server").GetValue<TimeSpan>("PollInterval");
		}

		public static string? GetTrustedServerThumbprint(this IConfigurationRoot In)
		{
			return In.GetSection("Server").GetValue<string?>("TrustedThumbprint");
		}

		public static bool VerifyNative(this IConfigurationRoot In)
		{
			return In.GetSection("Server").GetValue<bool?>("VerifyNative") ?? true;
		}
	}
}
