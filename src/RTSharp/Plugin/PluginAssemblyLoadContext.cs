using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace RTSharp.Plugin
{
    [RequiresUnreferencedCode("Plugins")]
    public class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver Resolver;

        public PluginAssemblyLoadContext(string assemblyPath) : base(isCollectible: true)
        {
            Resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            var assemblyPath = Resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);

            // Not a plugin-private dependency: return null so the runtime resolves it from
            // the host's Default ALC. Since the host is published untrimmed, every framework
            // and shared assembly is present there with its full surface, keeping a single
            // Type identity across the host and every plugin.
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
