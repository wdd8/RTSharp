using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

namespace RTSharp.Daemon.Utils
{
    public static class ConfigExtensions
    {
        public static (string Public, string Private) GetCertificates(this IConfigurationRoot In)
        {
            var section = In.GetSection("Certificate");
            return (section.GetValue<string>("PublicPem")!, section.GetValue<string>("PrivatePem")!);
        }

        public static string[] GetListenAddresses(this IConfigurationRoot In)
        {
            return In.GetSection("ListenAddress").Get<string[]>()!;
        }

        public static string[] GetAllowedClientThumbprints(this IConfigurationRoot In)
        {
            return In.GetSection("AllowedClients").Get<string[]>()!;
        }
    }
}
