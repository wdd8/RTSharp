using System;
using Microsoft.Extensions.DependencyInjection;

namespace RTSharp.Core
{
	public static class ServiceProvider
	{
		public static IServiceProvider _provider;

		public static IServiceScope CreateScope() => _provider.CreateScope();
	}
}
