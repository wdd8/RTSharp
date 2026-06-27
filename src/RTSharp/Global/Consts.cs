using System.Reflection;
using RTSharp.Shared.Abstractions;

namespace RTSharp.Global
{
    public class Consts
    {
        static Consts()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version!;
            Version = new Version($"{v.Major}.{v.Minor}.{v.Build}", (uint)v.Major, (uint)v.Minor, (uint)v.Build);
        }

        public static Version Version { get; }
    }
}
