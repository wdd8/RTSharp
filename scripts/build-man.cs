using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

/*
 * Compiles a man page declared in the "man" section of docs/help/help.json from the HTML pages of docs/help.
 *
 * Requires pandoc: `apt install pandoc` on Debian/Ubuntu.
 */

if (args.Length != 2) {
    Console.Error.WriteLine("Usage: dotnet run scripts/build-man.cs -- <page name> <man root dir>");
    return 1;
}

var repoDir = Path.GetDirectoryName(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? throw new Exception("Must be run as a file-based app"))!;
var srcDir = Path.Combine(repoDir, "docs", "help");
var name = args[0];
var manRoot = Path.GetFullPath(args[1]);
var version = XDocument.Load(Path.Combine(repoDir, "Version.props")).Descendants("RTSharpVersion").Single().Value;

using var helpJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(srcDir, "help.json")));
var pages = helpJson.RootElement.GetProperty("man").Deserialize(HelpJsonContext.Default.ListManPage)
    ?? throw new Exception("help.json has an empty man section");

var page = pages.SingleOrDefault(x => x.Name == name)
    ?? throw new Exception($"help.json declares no man page named {name}");

var missing = page.Topics.Where(x => !File.Exists(Path.Combine(srcDir, x))).ToList();
if (missing.Count != 0) {
    Console.Error.WriteLine($"help.json references files missing from {srcDir}:");
    foreach (var file in missing)
        Console.Error.WriteLine($"  {file}");
    return 1;
}

var html = new StringBuilder();
html.AppendLine("<!DOCTYPE html><html><body>");
html.AppendLine($"<h1>NAME</h1><p>{WebUtility.HtmlEncode(page.Name)} - {WebUtility.HtmlEncode(page.Summary)}</p>");
html.AppendLine($"<h1>SYNOPSIS</h1><p><b>{WebUtility.HtmlEncode(page.Synopsis)}</b></p>");

// Pandoc maps h1 to .SH and everything below to .SS. Topics only use h2 and h3, so their titles become the sections
foreach (var topic in page.Topics) {
    var text = File.ReadAllText(Path.Combine(srcDir, topic));

    var titleStart = text.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
    var titleEnd = text.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
    if (titleStart == -1 || titleEnd == -1) {
        Console.Error.WriteLine($"{topic} has no <title>");
        return 1;
    }

    var bodyStart = text.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
    var bodyTagEnd = bodyStart == -1 ? -1 : text.IndexOf('>', bodyStart);
    var bodyEnd = text.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
    if (bodyTagEnd == -1 || bodyEnd == -1) {
        Console.Error.WriteLine($"{topic} has no <body>");
        return 1;
    }

    html.AppendLine($"<h1>{text[(titleStart + "<title>".Length)..titleEnd].Trim()}</h1>");
    html.AppendLine(text[(bodyTagEnd + 1)..bodyEnd]);
}

html.AppendLine($"<h1>SEE ALSO</h1><p>{WebUtility.HtmlEncode(String.Join(", ", page.SeeAlso))}</p>");
html.AppendLine("</body></html>");

var stage = Directory.CreateTempSubdirectory("rtsharp-man-").FullName;
var input = Path.Combine(stage, "page.html");
var roff = Path.Combine(stage, "page.roff");
File.WriteAllText(input, html.ToString());

Console.WriteLine($"Compiling {page.Name}({page.Section}) from {page.Topics.Count} topics...");

var psi = new ProcessStartInfo("pandoc") {
    UseShellExecute = false
};
foreach (var arg in new[] {
    "-f", "html",
    "-t", "man",
    "-s",
    "-M", $"title={page.Name.ToUpperInvariant()}",
    "-M", $"section={page.Section}",
    "-M", $"footer=RTSharp {version}",
    "-M", "header=RTSharp Manual",
    "-o", roff,
    input
})
    psi.ArgumentList.Add(arg);
using (var proc = Process.Start(psi) ?? throw new Exception("Failed to start pandoc")) {
    proc.WaitForExit();
    if (proc.ExitCode != 0)
        throw new Exception($"pandoc exited with code {proc.ExitCode}");
}

var outFile = Path.Combine(manRoot, $"man{page.Section}", $"{page.Name}.{page.Section}.gz");
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
using (var source = File.OpenRead(roff))
using (var dest = new GZipStream(File.Create(outFile), CompressionLevel.SmallestSize))
    source.CopyTo(dest);

Directory.Delete(stage, recursive: true);

Console.WriteLine($"Wrote {outFile}");
return 0;

record ManPage(string Name, int Section, string Summary, string Synopsis, List<string> Topics, List<string> SeeAlso);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(List<ManPage>))]
partial class HelpJsonContext : JsonSerializerContext;
