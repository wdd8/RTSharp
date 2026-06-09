using System;
using System.IO;

namespace RTSharp.Core.Services.Cache;

public class Utils
{
    public static string MakeConnectionString(string name) => "Data Source='" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RTSharp.Shared.Abstractions.Consts.USER_DATA_PATH, name + ".sqlite3") + "'";
}
