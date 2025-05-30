using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using RTSharp.Daemon.RuntimeCompilation.Exceptions;

using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace RTSharp.Daemon.RuntimeCompilation
{
	public static class DynamicCompilation
	{
		const string BuiltInUsingsPragma = "#pragma usings";

		static readonly Dictionary<string, AssemblyMetadata> AssemblyMetadatas = new Dictionary<string, AssemblyMetadata>();

		private static string GetAssemblyLocationByName(string Name)
		{
			var asms = AppDomain.CurrentDomain.GetAssemblies();
			var asm = asms.FirstOrDefault(x => x!.FullName!.Substring(0, x.FullName.IndexOf(',', StringComparison.Ordinal)) == Name);
			if (asm == null) {
				var searchDirs = new List<string>() { "." };
			
				var runtimeVer = FileVersionInfo.GetVersionInfo(typeof(System.Runtime.GCSettings).Assembly.Location).ProductVersion;
				runtimeVer = runtimeVer[..runtimeVer.IndexOf('+')];
				
				var runtime = typeof(System.Runtime.GCSettings).Assembly!.Location;
				searchDirs.Add(Path.GetDirectoryName(runtime));
				
				var pathComponents = runtime.Split(Path.DirectorySeparatorChar, StringSplitOptions.None);
				var analyzersPath = new List<string>();
				bool found = false;
				for (var x = 0;x < pathComponents.Length;x++) {
					if (pathComponents[x] != "dotnet")
						analyzersPath.Add(pathComponents[x]);
					else {
						analyzersPath.Add("dotnet");
						found = true;
						break;
					}
				}
				
				if (found) {
					analyzersPath.Add("packs");
					analyzersPath.Add("Microsoft.NETCore.App.Ref");
					analyzersPath.Add(runtimeVer);
					analyzersPath.Add("analyzers");
					analyzersPath.Add("dotnet");
					analyzersPath.Add("cs");
					
					if (Directory.Exists(string.Join(Path.DirectorySeparatorChar, analyzersPath))) {
						searchDirs.Add(string.Join(Path.DirectorySeparatorChar, analyzersPath));
					}
				}

				var thisLoc = Assembly.GetEntryAssembly()!.Location;
				searchDirs.Add(Path.GetDirectoryName(thisLoc));

				foreach (var dir in searchDirs) {
					Debug.WriteLine($"Searching directory '{dir}'...");
				
					if (File.Exists(Path.Combine(dir, Name))) {
						Debug.WriteLine($"Found assembly at {Path.Combine(dir, Name)}");
						return Path.Combine(dir, Name);
					}
					if (File.Exists(Path.Combine(dir, Name) + ".dll")) {
						Debug.WriteLine($"Found assembly at {Path.Combine(dir, Name)}.dll");
						return Path.Combine(dir, Name) + ".dll";
					}
				}

				throw new DllNotFoundException(Name);
			}

			return asm.Location;
		}


		public static (List<MetadataReference> References, List<string> SourceGenAsms, List<string> PragmaTags, string Script) TransformScript(ref string Script, IEnumerable<string> BuildInAssemblyReferences, IEnumerable<string> BuiltInUsings)
		{
			// Include dynamic keyword
			_ = (dynamic)1 + 1;

			var references = new List<MetadataReference>();
			var baseReferences = new[] {
					"netstandard",
					"System.Private.CoreLib",

					// dynamic keyword
					"System.Runtime",
					"Microsoft.CSharp",
					"System.Linq.Expressions"
				};
			var sourceGenAssemblies = new List<string>();

			if (AssemblyMetadatas.Count == 0) {
				foreach (var reference in baseReferences) {
					AssemblyMetadatas[reference!] = AssemblyMetadata.CreateFromFile(GetAssemblyLocationByName(reference!));
				}
			}

			foreach (var reference in baseReferences) {
				references.Add(AssemblyMetadatas[reference].GetReference());
			}

			foreach (var reference in BuildInAssemblyReferences) {
				if (!AssemblyMetadatas.ContainsKey(reference))
					AssemblyMetadatas[reference] = AssemblyMetadata.CreateFromFile(GetAssemblyLocationByName(reference));

				references.Add(AssemblyMetadatas[reference].GetReference());
			}

			int lineStart = 0;

			void removeFromStart(ref string Script, int chars)
			{
				Script = Script[..lineStart] + Script[(lineStart+chars)..];
			}

			void addToStart(ref string Script, string inp)
			{
				Script = Script[..lineStart] + inp + Script[lineStart..];
			}

			bool foundPragmaUsings = false;
			List<string> pragmaTags = new List<string>();

			while (Script[lineStart..].StartsWith("#pragma", StringComparison.Ordinal)) {
                int eol;

                bool genEol(string Script)
                {
                    while (Script[lineStart] == '\n' || Script[lineStart] == '\r') {
                        lineStart++;
                    }

                    eol = Script[lineStart..].IndexOf("\r\n", StringComparison.Ordinal);
                    if (eol == -1)
                        eol = Script[lineStart..].IndexOf('\n', StringComparison.Ordinal);

                    return eol != -1;
                }
                if (!genEol(Script))
                    break;

                var thisLine = Script[lineStart..(lineStart+eol)];

                if (thisLine.StartsWith("#pragma lib \"", StringComparison.Ordinal)) {
					var contents = thisLine[13..];

                    if (thisLine[^1] != '"')
                        throw new PragmaParsingException("Invalid file: #pragma lib end not found");

                    references.Add(MetadataReference.CreateFromFile(GetAssemblyLocationByName(contents[..^1])));

					removeFromStart(ref Script, thisLine.Length);
                    eol = 0;
                } else if (thisLine.StartsWith("#pragma sourcegen \"", StringComparison.Ordinal)) {
					var contents = thisLine[19..];

                    if (thisLine[^1] != '"')
                        throw new PragmaParsingException("Invalid file: #pragma sourcegen end not found");

                    sourceGenAssemblies.Add(GetAssemblyLocationByName(contents[..^1]));

					removeFromStart(ref Script, thisLine.Length);
                    eol = 0;
                } else if (thisLine.StartsWith(BuiltInUsingsPragma, StringComparison.Ordinal)) {
					foundPragmaUsings = true;
					var builtinUsings = BuiltInUsings.Any() ? BuiltInUsings.Select(x => "using " + x + ";").Aggregate((a, b) => a + b) : "";

					removeFromStart(ref Script, thisLine.Length);
					addToStart(ref Script, builtinUsings);

                    if (!genEol(Script))
                        break;
                } else if (thisLine.StartsWith("#pragma tag ", StringComparison.Ordinal)) {
					var contents = thisLine[12..];

					removeFromStart(ref Script, thisLine.Length);
                    eol = 0;

					pragmaTags.Add(contents);
                }

                lineStart += eol;
                while (Script[lineStart] == '\n' || Script[lineStart] == '\r') {
                    lineStart++;
                }
            }

			if (!foundPragmaUsings) {
				throw new PragmaParsingException("Script is missing '#pragma usings'");
			}

			return (references, sourceGenAssemblies, pragmaTags, Script);
		}

		public static CSharpParseOptions Parse { get; } = new CSharpParseOptions(
			kind: SourceCodeKind.Regular,
			languageVersion: LanguageVersion.Latest);
		public static CSharpCompilationOptions Compilation { get; } = new CSharpCompilationOptions(
			OutputKind.DynamicallyLinkedLibrary,
			optimizationLevel: OptimizationLevel.Debug,
			allowUnsafe: true);

		public static string[] GetScriptTags(string Script)
		{
            string copy = Script;
			var (_, _, pragmaTags, _) = TransformScript(ref copy, new string[] { }, new string[] { });

			return pragmaTags.ToArray();
		}

		public static (MemoryStream Assembly, string[] PragmaTags) CompileAssembly(
			ref string Script,
			string Name,
			IEnumerable<string> BuiltInAssemblyReferences,
			IEnumerable<string> BuiltInUsings)
		{
			var (references, sourceGenAsmNames, pragmaTags, script) = TransformScript(ref Script, BuiltInAssemblyReferences, BuiltInUsings);

			var generators = new List<ISourceGenerator>();

			foreach (var name in sourceGenAsmNames) {
				Debug.WriteLine($"Loading {name}...");
				var loaded = Assembly.LoadFile(name);
				
				generators.AddRange(loaded.GetTypes().Where(x => x.IsAssignableTo(typeof(ISourceGenerator))).Select(x => (ISourceGenerator)Activator.CreateInstance(x)));
				generators.AddRange(loaded.GetTypes().Where(x => x.IsAssignableTo(typeof(IIncrementalGenerator))).Select(x => GeneratorExtensions.AsSourceGenerator((IIncrementalGenerator)Activator.CreateInstance(x))));
			}
			
			Debug.WriteLine($"Generators: {string.Join(',', generators.Select(x => x.GetType().Name))}...");

			var asmName = Name + "_" + Guid.NewGuid().ToString()[..8];
			var encoding = Encoding.UTF8;

			var buffer = encoding.GetBytes(script);
			var sourceText = SourceText.From(buffer, buffer.Length, encoding, canBeEmbedded: true);
			var csTree = CSharpSyntaxTree.ParseText(sourceText, Parse, path: $"{asmName}.cs");
			var encoded = CSharpSyntaxTree.Create((CSharpSyntaxNode)csTree.GetRoot(), null, $"{asmName}.cs", encoding);
			var compilation = CSharpCompilation.Create(asmName, options: Compilation, references: references, syntaxTrees: [ encoded ]);
			var driver = CSharpGeneratorDriver.Create(generators);
			driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var _);
            
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

			return (ms, pragmaTags.ToArray());
		}

		public static DynamicScript<T> Compile<T>(
			ref string Script,
			string Name,
			IEnumerable<string> BuiltInAssemblyReferences,
			IEnumerable<string> BuiltInUsings)
		{
			var (ms, pragmaTags) = CompileAssembly(ref Script, Name, BuiltInAssemblyReferences, BuiltInUsings);

            return new DynamicScript<T>(ms, pragmaTags);
        }
    }
}
