using System;
using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Core
{
    public static class ServiceProvider
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        internal static IServiceProvider Root;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public static IServiceScope CreateScope() => Root.CreateScope();
    }
}
