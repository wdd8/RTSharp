using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using RTSharp.Daemon.RuntimeCompilation.Exceptions;

using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;

namespace RTSharp.Daemon.RuntimeCompilation;

public sealed class ScriptSource(IEnumerable<string> BuiltInUsings)
{
	public readonly List<string> SourceGeneratorAssemblies = [];
	public readonly List<string> Tags = [];
	public string Text = "";

	internal readonly string Usings = String.Concat(BuiltInUsings.Select(x => "using " + x + ";"));
	internal bool FoundUsings;
}

public static class DynamicCompilation
{
	private static readonly TimeSpan CompilationCacheIdleTimeout = TimeSpan.FromMinutes(30);

	public static readonly MemoryCache Cache = new(new MemoryCacheOptions());
	private static readonly Lock CacheLock = new();

	static readonly Dictionary<string, SemaphoreSlim> CompileLocks = new();
	static readonly Lock CompileLocksLock = new();

	private static readonly (string Prefix, Func<string, ScriptSource, string> Handle)[] Pragmas = [
		("#pragma sourcegen \"", static (Arguments, Source) => {
			if (Arguments.Length == 0 || Arguments[^1] != '"')
				throw new PragmaParsingException("Invalid file: #pragma sourcegen end not found");

			Source.SourceGeneratorAssemblies.Add(ScriptReferenceResolver.ResolveAssemblyPath(Arguments[..^1]));
			return "";
		}),
		("#pragma tag ", static (Arguments, Source) => {
			Source.Tags.Add(Arguments);
			return "";
		}),
		("#pragma usings", static (_, Source) => {
			Source.FoundUsings = true;
			return Source.Usings;
		})
	];

	public static ScriptSource ParseScript(string Script, IEnumerable<string> BuiltInUsings)
	{
		var source = new ScriptSource(BuiltInUsings);
		var output = new StringBuilder(Script.Length);
		var pos = 0;

		while (pos < Script.Length) {
			var nl = Script.IndexOf('\n', pos);
			var nextLine = nl == -1 ? Script.Length : nl + 1;
			var lineEnd = nl == -1 ? Script.Length : nl;

			if (lineEnd > pos && Script[lineEnd - 1] == '\r')
				lineEnd--;

			var line = Script[pos..lineEnd];

			if (line.Length != 0 && !line.StartsWith("#pragma", StringComparison.Ordinal))
				break;

			foreach (var (prefix, handle) in Pragmas) {
				if (line.StartsWith(prefix, StringComparison.Ordinal)) {
					line = handle(line[prefix.Length..], source);
					break;
				}
			}

			output.Append(line);
			output.Append(Script[lineEnd..nextLine]);
			pos = nextLine;
		}

		if (!source.FoundUsings)
			throw new PragmaParsingException("Script is missing '#pragma usings'");

		output.Append(Script[pos..]);
		source.Text = output.ToString();

		return source;
	}

	public static CSharpParseOptions Parse { get; } = new CSharpParseOptions(
		kind: SourceCodeKind.Regular,
		languageVersion: LanguageVersion.Latest)
		.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Dapper.AOT")]);
	public static CSharpCompilationOptions Compilation { get; } = new CSharpCompilationOptions(
		OutputKind.DynamicallyLinkedLibrary,
		optimizationLevel: OptimizationLevel.Debug,
		allowUnsafe: true);

	public static (MemoryStream Assembly, string[] Tags) CompileAssembly(string Script, string Name)
	{
		var source = ParseScript(Script, ScriptEnvironment.BuiltInUsings);

		var generators = new List<ISourceGenerator> {
			new Dapper.CodeAnalysis.DapperInterceptorGenerator().AsSourceGenerator()
		};

		foreach (var path in source.SourceGeneratorAssemblies) {
			Debug.WriteLine($"Loading {path}...");
			var loaded = Assembly.LoadFile(path);

			generators.AddRange(loaded.GetTypes().Where(x => x.IsAssignableTo(typeof(ISourceGenerator))).Select(x => (ISourceGenerator)Activator.CreateInstance(x)!)!);
			generators.AddRange(loaded.GetTypes().Where(x => x.IsAssignableTo(typeof(IIncrementalGenerator))).Select(x => GeneratorExtensions.AsSourceGenerator((IIncrementalGenerator)Activator.CreateInstance(x)!)));
		}

		Debug.WriteLine($"Generators: {string.Join(',', generators.Select(x => x.GetType().Name))}...");

		var sanitizedName = string.Create(Name.Length, Name, static (span, name) => {
			for (var x = 0; x < name.Length; x++) {
				var ch = name[x];
				span[x] = char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_';
			}
		});
		var asmName = sanitizedName + "_" + Guid.NewGuid().ToString()[..8];
		var encoding = Encoding.UTF8;

		var buffer = encoding.GetBytes(source.Text);
		var sourceText = SourceText.From(buffer, buffer.Length, encoding, canBeEmbedded: true);
		var csTree = CSharpSyntaxTree.ParseText(sourceText, Parse, path: $"{asmName}.cs");
		var encoded = CSharpSyntaxTree.Create((CSharpSyntaxNode)csTree.GetRoot(), Parse, $"{asmName}.cs", encoding);
		var global = CSharpSyntaxTree.ParseText("[module: Dapper.DapperAot]", Parse, path: "global.cs", encoding: encoding);
		var compilation = CSharpCompilation.Create(asmName, options: Compilation, references: ScriptReferenceResolver.References, syntaxTrees: [ encoded, global ]);
		var driver = CSharpGeneratorDriver.Create(generators, parseOptions: Parse);
		driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
		if (generatorDiagnostics.Any(x => x.Severity == DiagnosticSeverity.Error || x.Id == "CS8785"))
			throw new CompilationFailureException($"Source generation failure. {string.Join(Environment.NewLine, generatorDiagnostics)}");

    var texts = new List<EmbeddedText>();
    texts.Add(EmbeddedText.FromSource($"{asmName}.cs", sourceText));

    texts.AddRange(outputCompilation.SyntaxTrees.Except([ encoded ]).Select((x, i) => {
        var filePath = string.IsNullOrEmpty(x.FilePath) ? $"{asmName}.cs" : x.FilePath;

        var root = x.GetRoot();
        using var writer = new StringWriter();

	        root.WriteTo(writer);

        var buffer = Encoding.UTF8.GetBytes(writer.ToString());
        return EmbeddedText.FromSource(filePath, SourceText.From(buffer, buffer.Length, Encoding.UTF8, canBeEmbedded: true));
    }));
		var ms = new MemoryStream();

		Debug.WriteLine($"Texts: {texts.Count}");

		var emitResult = outputCompilation.Emit(
			peStream: ms,
			embeddedTexts: texts,
			options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded)
		);

		if (!emitResult.Success)
			throw new CompilationFailureException($"Compilation failure. {string.Join(Environment.NewLine, emitResult.Diagnostics.Where(x => x.IsWarningAsError || x.Severity == DiagnosticSeverity.Error).Select(x => x.ToString()))}");
		ms.Seek(0, SeekOrigin.Begin);

		return (ms, [.. source.Tags]);
	}

	public static bool TryDescribeAuthoringError(Exception Ex, out string Message)
	{
		switch (Ex) {
			case CompilationFailureException:
				Message = string.Join('\n', Ex.Message.Split([ "\r\n", "\n" ], StringSplitOptions.None).Where(x => !x.StartsWith("warning", StringComparison.Ordinal)));
				return true;
			case PragmaParsingException:
			case InstantiationException:
			case DllNotFoundException:
			case InterfaceNotFoundException:
				Message = Ex.Message;
				return true;
			default:
				Message = "";
				return false;
		}
	}

	public static async Task<DynamicScript<T>> Compile<T>(string Script, string Name)
	{
		var key = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(Encoding.UTF8.GetBytes(Script)));

		SemaphoreSlim keyLock;
		lock (CompileLocksLock) {
			if (!CompileLocks.TryGetValue(key, out keyLock!)) {
				keyLock = new SemaphoreSlim(1, 1);
				CompileLocks[key] = keyLock;
			}
		}

		await keyLock.WaitAsync(); try {
			lock (CacheLock) {
				if (Cache.TryGetValue(key, out DynamicScript<T>? cached) && cached!.TryAddRef()) {
					return cached;
				}
			}

			var (ms, tags) = CompileAssembly(Script, Name);

			var compilation = new DynamicScript<T>(ms, tags, CompilationCacheIdleTimeout, self => {
				lock (CacheLock) {
					if (self.TryExpire() && Cache.TryGetValue(key, out DynamicScript<T>? current) && ReferenceEquals(current, self)) {
						Cache.Remove(key);
					}
				}
			});

			if (!compilation.TryAddRef()) {
            throw new InvalidOperationException();
        }

			lock (CacheLock) {
				Cache.Set(key, compilation);
			}

			return compilation;
		} finally {
			keyLock.Release();
		}
	}
}
