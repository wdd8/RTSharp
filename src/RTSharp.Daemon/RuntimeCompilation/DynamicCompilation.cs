using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using RTSharp.Daemon.RuntimeCompilation.Exceptions;

using System.Reflection;
using System.Security.Cryptography;

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
				var runtime = typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly;
				var index = Math.Max(runtime!.Location.LastIndexOf('/'), runtime.Location.LastIndexOf('\\'));
				var searchDirs = new List<string>() { "." };

				if (index != -1) {
					searchDirs.Add(runtime.Location[0..index]);
				}

				var thisLoc = Assembly.GetEntryAssembly()!.Location;
				index = Math.Max(thisLoc.LastIndexOf('/'), thisLoc.LastIndexOf('\\'));
				if (index != -1) {
					searchDirs.Add(thisLoc[0..index]);
				}

				foreach (var dir in searchDirs) {
					if (File.Exists(Path.Combine(dir, Name)))
						return Path.Combine(dir, Name);
					if (File.Exists(Path.Combine(dir, Name) + ".dll"))
						return Path.Combine(dir, Name) + ".dll";
				}

				throw new DllNotFoundException(Name);
			}

			return asm.Location;
		}


		public static (List<MetadataReference> References, List<string> PragmaTags, string Script) TransformScript(ref string Script, IEnumerable<string> BuildInAssemblyReferences, IEnumerable<string> BuiltInUsings)
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

			return (references, pragmaTags, Script);
		}

		public static CSharpParseOptions Parse { get; } = new CSharpParseOptions(
			kind: SourceCodeKind.Regular,
			languageVersion: LanguageVersion.Latest);
		public static CSharpCompilationOptions Compilation { get; } = new CSharpCompilationOptions(
			OutputKind.DynamicallyLinkedLibrary,
			optimizationLevel: OptimizationLevel.Release,
			allowUnsafe: false);

		public static string[] GetScriptTags(string Script)
		{
            string copy = Script;
			var (_, pragmaTags, _) = TransformScript(ref copy, new string[] { }, new string[] { });

			return pragmaTags.ToArray();
		}

		public static (MemoryStream Stream, string[] PragmaTags) CompileAssembly(
			ref string Script,
			IEnumerable<string> BuiltInAssemblyReferences,
			IEnumerable<string> BuiltInUsings)
		{
			var (references, pragmaTags, script) = TransformScript(ref Script, BuiltInAssemblyReferences, BuiltInUsings);

			var csTree = CSharpSyntaxTree.ParseText(script, Parse);

			var asmName = Guid.NewGuid().ToString();

			Compilation compilation = CSharpCompilation.Create(asmName, options: Compilation, references: references)
				.AddSyntaxTrees(csTree);

			var ms = new MemoryStream();

			var emitResult = compilation.Emit(ms);

			if (!emitResult.Success)
				throw new CompilationFailureException($"Compilation failure. {emitResult.Diagnostics.Select(x => x.ToString()).Aggregate((a, b) => a + Environment.NewLine + b)}");
			ms.Seek(0, SeekOrigin.Begin);

			return (ms, pragmaTags.ToArray());
		}

		public static DynamicScript<T> Compile<T>(
			ref string Script,
			IEnumerable<string> BuiltInAssemblyReferences,
			IEnumerable<string> BuiltInUsings)
		{
			var (ms, pragmaTags) = CompileAssembly(ref Script, BuiltInAssemblyReferences, BuiltInUsings);

            return new DynamicScript<T>(ms, pragmaTags);
        }
    }
}
