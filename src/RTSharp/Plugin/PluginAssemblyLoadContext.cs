using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace RTSharp.Plugin
{
    public class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver Resolver;

        public PluginAssemblyLoadContext(string assemblyPath) : base(isCollectible: true)
        {
            Resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (AssemblyLoadContext.Default.Assemblies.Any(a => a.FullName == name.FullName))
                return null;

            var assemblyPath = Resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = Resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null) {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
