using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Cache
{
    public class Utils
    {
        public static string MakeConnectionString(string name) => "Data Source='" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".sqlite3") + "'";
    }
}
