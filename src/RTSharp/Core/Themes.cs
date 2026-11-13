extern alias AvaloniaBuildTasks;

using Avalonia.Styling;

using AvaloniaBuildTasks::Avalonia.Build.Tasks;

using Microsoft.Build.Framework;

using Serilog;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace RTSharp.Core;

public static class Themes
{
    public const string Standard = "Standard";

    const string ThemeAssemblyName = "RTSharp.Theme";

    const string RootFile = "Theme.axaml";

    static readonly string UserThemesPath = Path.Combine(Shared.Abstractions.Consts.USER_DATA_PATH, "themes");

    static Themes()
    {
        // Avalonia.Build.Tasks' assembly name has a random suffix (Avalonia.Build.Tasks.<guid>), which doesn't match
        // its file name, so the default probing can't find it. Registered here so it's in place before Compile is JIT compiled
        AssemblyLoadContext.Default.Resolving += (Context, Name) =>
            Name.Name?.StartsWith("Avalonia.Build.Tasks.", StringComparison.Ordinal) == true ?
                Context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Avalonia.Build.Tasks.dll")) :
                null;
    }

    public static IReadOnlyList<string> List()
    {
        if (!Directory.Exists(UserThemesPath))
            return [];

        return Directory.EnumerateDirectories(UserThemesPath)
            .Where(x => File.Exists(Path.Combine(x, RootFile)))
            .Select(x => Path.GetFileName(x))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void CreateMissingBuiltInThemes()
    {
        const string prefix = "Themes/";
        var assembly = typeof(Themes).Assembly;

        // Themes/<theme>/<path>
        var builtIn = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
            .Select(x => (Resource: x, Path: x[prefix.Length..].Split('/', '\\')))
            .GroupBy(x => x.Path[0]);

        foreach (var theme in builtIn) {
            var themeDir = Path.Combine(UserThemesPath, theme.Key);
            if (Directory.Exists(themeDir))
                continue;

            var tempDir = $"{themeDir}.{Guid.NewGuid():N}";
            foreach (var (resource, path) in theme) {
                var file = Path.Combine([tempDir, .. path[1..]]);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                using var source = assembly.GetManifestResourceStream(resource)!;
                using var target = File.Create(file);
                source.CopyTo(target);
            }

            try {
                Directory.Move(tempDir, themeDir);
            } catch (IOException) when (Directory.Exists(themeDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    static string Prepare(string Name)
    {
        var themeDir = Path.Combine(UserThemesPath, Name);
        if (!File.Exists(Path.Combine(themeDir, RootFile)))
            throw new FileNotFoundException($"Theme {Name} not found", Path.Combine(themeDir, RootFile));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(typeof(Themes).Assembly.ManifestModule.ModuleVersionId.ToByteArray());

        foreach (var file in new DirectoryInfo(themeDir).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(x => x.FullName, StringComparer.Ordinal)) {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(UserThemesPath, file.FullName)));
            hash.AppendData(BitConverter.GetBytes(file.Length));
            hash.AppendData(BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks));
        }

        var themeHash = Convert.ToHexString(hash.GetHashAndReset(), 0, 8);

        var compiledPath = Path.Combine(UserThemesPath, $"{ThemeAssemblyName}.{themeHash}.dll");

        if (File.Exists(compiledPath))
            return compiledPath;

        var temp = Compile(Name);
        try {
            File.Move(temp, compiledPath);
        } catch (IOException) when (File.Exists(compiledPath)) {
            File.Delete(temp);
        }

        foreach (var stale in Directory.EnumerateFiles(UserThemesPath, $"{ThemeAssemblyName}.*.dll").Where(x => x != compiledPath)) {
            try {
                File.Delete(stale);
            } catch (IOException) {
            }
        }

        return compiledPath;
    }

    /// <summary>
    /// Compiles the theme if it has no stored assembly, and loads the assembly.
    /// </summary>
    public static Assembly LoadAssembly(string Name)
    {
        var compiledPath = Prepare(Name);

        Assembly assembly;
        try {
            assembly = Assembly.Load(File.ReadAllBytes(compiledPath));
        } catch (Exception ex) {
            Log.Logger.Warning(ex, $"Stored theme {compiledPath} failed to load, compiling {Name} again");
            File.Delete(compiledPath);
            assembly = Assembly.Load(File.ReadAllBytes(Prepare(Name)));
        }

        // The UI thread runs this code as it builds the styles, and the templates of every control
        Jit.CompileInBackground(assembly.GetTypes());

        return assembly;
    }

    public static IStyle Load(Assembly Theme)
    {
        var tryLoad = Theme.GetType("CompiledAvaloniaXaml.!XamlLoader")?.GetMethod("TryLoad", [typeof(IServiceProvider), typeof(string)]) ?? throw new InvalidOperationException("Compiled theme contains no XAML");

        return tryLoad.Invoke(null, [null, $"avares://{ThemeAssemblyName}/{RootFile}"]) as IStyle ?? throw new InvalidOperationException($"{RootFile} must have Styles as its root element");
    }

    static string Compile(string Name)
    {
        var themeDir = Path.Combine(UserThemesPath, Name);
        var id = Guid.NewGuid().ToString("N");
        var resourcesPath = Path.Combine(UserThemesPath, $"{id}.resources");
        var inputPath = Path.Combine(UserThemesPath, $"{id}.input.dll");
        var outputPath = Path.Combine(UserThemesPath, $"{id}.dll");
        var engine = new BuildEngine();

        try {
            var resources = new GenerateAvaloniaResourcesTask {
                BuildEngine = engine,
                Root = themeDir,
                Resources = Directory.EnumerateFiles(themeDir, "*", SearchOption.AllDirectories)
                    .Select(x => (ITaskItem)new TaskItem(Path.GetRelativePath(themeDir, x)))
                    .ToArray(),
                Output = resourcesPath
            };
            if (!resources.Execute())
                throw engine.Failure(Name);

            var avaloniaResources = File.ReadAllBytes(resourcesPath);

            var builder = new PersistedAssemblyBuilder(new AssemblyName(ThemeAssemblyName), typeof(object).Assembly);
            builder.DefineDynamicModule(ThemeAssemblyName + ".dll");
            var metadata = builder.GenerateMetadata(out var ilStream, out var fieldData);

            var managedResources = new BlobBuilder();
            metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("!AvaloniaResources"), default, (uint)managedResources.Count);
            managedResources.WriteInt32(avaloniaResources.Length);
            managedResources.WriteBytes(avaloniaResources);

            var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), ilStream, fieldData, managedResources);
            var image = new BlobBuilder();
            pe.Serialize(image);

            using (var file = File.Create(inputPath))
                image.WriteContentTo(file);

            var input = new TaskItem(inputPath);
            input.SetMetadata("AvaloniaCompileOutput", outputPath);

            var compile = new CompileAvaloniaXamlTask {
                BuildEngine = engine,
                AssemblyFile = input,
                References = [.. new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(object).Assembly.Location)! }
                    .SelectMany(x => Directory.EnumerateFiles(x, "*.dll"))
                    .DistinctBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                    .Where(x => {
                        try {
                            using var pe = new PEReader(File.OpenRead(x));
                            return pe.HasMetadata;
                        } catch (BadImageFormatException) {
                            return false;
                        }
                    })
                    .Select(x => (ITaskItem)new TaskItem(x))],
                ProjectDirectory = themeDir,
                DefaultCompileBindings = true,
                AnalyzerConfigFiles = []
            };
            if (!compile.Execute())
                throw engine.Failure(Name);
        } catch {
            File.Delete(outputPath);
            throw;
        } finally {
            File.Delete(resourcesPath);
            File.Delete(inputPath);
        }

        return outputPath;
    }

    sealed class BuildEngine : IBuildEngine
    {
        readonly List<string> Errors = [];

        public Exception Failure(string Theme) => new InvalidOperationException($"Theme {Theme} failed to compile:\n{String.Join('\n', Errors)}");

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add($"{e.File}({e.LineNumber},{e.ColumnNumber}): {e.Code}: {e.Message}");

        public void LogWarningEvent(BuildWarningEventArgs e) => Log.Logger.Warning($"Theme: {e.File}({e.LineNumber},{e.ColumnNumber}): {e.Code}: {e.Message}");

        public void LogMessageEvent(BuildMessageEventArgs e) => Log.Logger.Verbose($"Theme: {e.Message}");
    }

    sealed class TaskItem(string ItemSpec) : ITaskItem
    {
        readonly Dictionary<string, string> Metadata = [];

        public string ItemSpec { get; set; } = ItemSpec;

        public ICollection MetadataNames => Metadata.Keys;

        public int MetadataCount => Metadata.Count;

        public string GetMetadata(string metadataName) => Metadata.GetValueOrDefault(metadataName, "");

        public void SetMetadata(string metadataName, string metadataValue) => Metadata[metadataName] = metadataValue;

        public void RemoveMetadata(string metadataName) => Metadata.Remove(metadataName);

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            foreach (var (name, value) in Metadata)
                destinationItem.SetMetadata(name, value);
        }

        public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(Metadata);
    }
}
