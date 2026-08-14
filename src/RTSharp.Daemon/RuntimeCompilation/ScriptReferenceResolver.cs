using Microsoft.CodeAnalysis;

using System.Diagnostics;
using System.Reflection;

namespace RTSharp.Daemon.RuntimeCompilation
{
    public static class ScriptReferenceResolver
    {
        public static IReadOnlyList<MetadataReference> References { get; }

        static ScriptReferenceResolver()
        {
            // Include dynamic keyword
            _ = (dynamic)1 + 1;

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in GetProbeDirectories()) {
                foreach (var path in Directory.EnumerateFiles(dir, "*.dll")) {
                    var name = Path.GetFileNameWithoutExtension(path);

                    if (byName.ContainsKey(name) || !IsManagedAssembly(path))
                        continue;

                    byName[name] = path;
                }
            }

            References = [ ..byName.Values.Select(x => AssemblyMetadata.CreateFromFile(x).GetReference()) ];

            Debug.WriteLine($"Script references: {References.Count}");
        }

        public static string ResolveAssemblyPath(string Name)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == Name);
            if (asm != null)
                return asm.Location;

            foreach (var dir in GetSearchDirectories()) {
                Debug.WriteLine($"Searching directory '{dir}'...");

                var path = Path.Combine(dir, Name);
                if (!File.Exists(path))
                    path += ".dll";
                if (!File.Exists(path))
                    continue;

                Debug.WriteLine($"Found assembly at {path}");
                return path;
            }

            throw new DllNotFoundException(Name);
        }

        private static IEnumerable<string> GetSearchDirectories()
        {
            yield return ".";

            foreach (var dir in GetProbeDirectories())
                yield return dir;

            var dotnet = new DirectoryInfo(AppContext.BaseDirectory);
            while (dotnet != null && dotnet.Name != "dotnet")
                dotnet = dotnet.Parent;

            if (dotnet == null)
                yield break;

            var refPacks = Path.Combine(dotnet.FullName, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(refPacks))
                yield break;

            foreach (var pack in Directory.EnumerateDirectories(refPacks).OrderDescending(StringComparer.Ordinal)) {
                var analyzers = Path.Combine(pack, "analyzers", "dotnet", "cs");
                if (Directory.Exists(analyzers))
                    yield return analyzers;
            }
        }

        private static IEnumerable<string> GetProbeDirectories()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            string?[] dirs = [
                AppContext.BaseDirectory,
                Path.GetDirectoryName(typeof(object).Assembly.Location),
                Path.GetDirectoryName(typeof(HttpContext).Assembly.Location)
            ];

            foreach (var dir in dirs) {
                if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir) && seen.Add(dir))
                    yield return dir;
            }
        }

        private static bool IsManagedAssembly(string Path)
        {
            try {
                _ = AssemblyName.GetAssemblyName(Path);
                return true;
            } catch (BadImageFormatException) {
                return false;
            } catch (FileLoadException) {
                // Already loaded by this process - still a managed assembly
                return true;
            } catch (IOException) {
                return false;
            }
        }
    }
}
