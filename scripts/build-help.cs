using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/*
 * Compiles the HTML pages of docs/help into a CHM help file.
 *
 * Requires chmcmd, the Free Pascal CHM compiler: `apt install fp-utils` on Debian/Ubuntu, Free Pascal
 * installer on Windows.
 */

if (args.Length is < 1 or > 2) {
    Console.Error.WriteLine("Usage: dotnet run scripts/build-help.cs -- <output .chm> [chmcmd path]");
    return 1;
}

var repoDir = Path.GetDirectoryName(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? throw new Exception("Must be run as a file-based app"))!;
var srcDir = Path.Combine(repoDir, "docs", "help");
var stage = Directory.CreateTempSubdirectory("rtsharp-help-").FullName;
var outFile = Path.GetFullPath(args[0]);
var chmcmd = args.Length == 2 ? args[1] : "chmcmd";

var project = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(srcDir, "help.json")), HelpJsonContext.Default.HelpProject)
    ?? throw new Exception("help.json is empty");

var includes = new HashSet<string> { "help.json" };
var contents = ExpandIncludes(project.Contents, "", srcDir, includes);

var files = new List<string>();
foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)) {
    var rel = Path.GetRelativePath(srcDir, file).Replace('\\', '/');
    if (includes.Contains(rel))
        continue;

    var dest = Path.Combine(stage, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
    File.Copy(file, dest);
    files.Add(rel);
}

var missing = new List<string>();
if (!files.Contains(project.DefaultTopic))
    missing.Add(project.DefaultTopic);
CollectMissing(contents, files, missing);
if (missing.Count != 0) {
    Console.Error.WriteLine($"help.json references files missing from {srcDir}:");
    foreach (var file in missing)
        Console.Error.WriteLine($"  {file}");
    return 1;
}

var toc = new StringBuilder();
toc.AppendLine("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\">");
toc.AppendLine("<HTML><HEAD></HEAD><BODY>");
AppendTocEntries(toc, contents, 0);
toc.AppendLine("</BODY></HTML>");
File.WriteAllText(Path.Combine(stage, "toc.hhc"), toc.ToString());

var pages = new List<(string Title, string File)>();
CollectPages(contents, pages);
for (var x = 0; x < pages.Count; x++) {
    var (title, file) = pages[x];
    var path = Path.Combine(stage, file);
    var html = File.ReadAllText(path);

    var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
    var bodyEnd = bodyStart == -1 ? -1 : html.IndexOf('>', bodyStart);
    if (bodyEnd == -1) {
        Console.Error.WriteLine($"{file} has no <body>");
        return 1;
    }

    string Link(string Text, string Target) => $"<a href=\"{WebUtility.HtmlEncode(RelativeUrl(file, Target))}\">{Text}</a>";

    var nav = new List<string>();
    if (x != 0)
        nav.Add(Link("Previous", pages[x - 1].File));
    nav.Add(Link("Top", project.DefaultTopic));
    if (x != pages.Count - 1)
        nav.Add(Link("Next", pages[x + 1].File));

    var header = $"""

        <table width="100%" border="0" cellspacing="0" cellpadding="2" bgcolor="#80FFFF">
          <tr>
            <td align="left"><span style="font-family:Helvetica,Arial; font-size:12pt; color:#000000"><b>{WebUtility.HtmlEncode(title)}</b></span></td>
            <td align="right"><font face="Arial" size="2">{String.Join("&nbsp;\n", nav)}</font></td>
          </tr>
        </table>
        <br><br>

        """;
    File.WriteAllText(path, html.Insert(bodyEnd + 1, header));
}

// Keyword -> (page title, file) for every page declaring keywords
var keywords = new SortedDictionary<string, List<(string Title, string File)>>(StringComparer.OrdinalIgnoreCase);
foreach (var file in files) {
    if (!file.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        continue;

    var html = File.ReadAllText(Path.Combine(stage, file));
    var declared = Between(html, "<meta name=\"keywords\" content=\"", "\"");
    if (String.IsNullOrWhiteSpace(declared))
        continue;

    var title = WebUtility.HtmlDecode(Between(html, "<title>", "</title>") ?? file).Trim();
    foreach (var keyword in declared.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
        var decoded = WebUtility.HtmlDecode(keyword);
        if (!keywords.TryGetValue(decoded, out var topics))
            keywords[decoded] = topics = [];
        topics.Add((title, file));
    }
}

if (keywords.Count != 0) {
    var index = new StringBuilder();
    index.AppendLine("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML//EN\">");
    index.AppendLine("<HTML><HEAD></HEAD><BODY>");
    index.AppendLine("<UL>");
    foreach (var (keyword, topics) in keywords) {
        index.Append($"  <LI><OBJECT type=\"text/sitemap\"><param name=\"Name\" value=\"{WebUtility.HtmlEncode(keyword)}\">");
        foreach (var (title, file) in topics)
            index.Append($"<param name=\"Name\" value=\"{WebUtility.HtmlEncode(title)}\"><param name=\"Local\" value=\"{WebUtility.HtmlEncode(file)}\">");
        index.AppendLine("</OBJECT>");
    }
    index.AppendLine("</UL>");
    index.AppendLine("</BODY></HTML>");
    File.WriteAllText(Path.Combine(stage, "index.hhk"), index.ToString());
}

var compiledName = Path.GetFileName(outFile);
var hhp = new StringBuilder();
hhp.AppendLine("[OPTIONS]");
hhp.AppendLine("Compatibility=1.1 or later");
hhp.AppendLine($"Compiled file={compiledName}");
hhp.AppendLine("Contents file=toc.hhc");
if (keywords.Count != 0)
    hhp.AppendLine("Index file=index.hhk");
hhp.AppendLine($"Default topic={project.DefaultTopic}");
hhp.AppendLine("Display compile progress=No");
hhp.AppendLine("Full-text search=Yes");
hhp.AppendLine("Language=0x409 English (United States)");
hhp.AppendLine($"Title={project.Title}");
hhp.AppendLine();
hhp.AppendLine("[FILES]");
foreach (var file in files)
    hhp.AppendLine(file);
File.WriteAllText(Path.Combine(stage, "help.hhp"), hhp.ToString());

Console.WriteLine($"Compiling {compiledName} from {files.Count} files...");

var psi = new ProcessStartInfo(chmcmd) {
    UseShellExecute = false,
    WorkingDirectory = stage
};
psi.ArgumentList.Add("help.hhp");
using (var proc = Process.Start(psi) ?? throw new Exception($"Failed to start {chmcmd}")) {
    proc.WaitForExit();
    if (proc.ExitCode != 0)
        throw new Exception($"{chmcmd} exited with code {proc.ExitCode}");
}

var compiled = Path.Combine(stage, compiledName);
if (!File.Exists(compiled))
    throw new Exception($"{chmcmd} produced no {compiledName}");

Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
File.Move(compiled, outFile, overwrite: true);
Directory.Delete(stage, recursive: true);

Console.WriteLine($"Wrote {outFile}");
return 0;

static void CollectMissing(List<TocEntry> Entries, List<string> Files, List<string> Missing)
{
    foreach (var entry in Entries) {
        if (entry.File != null && !Files.Contains(entry.File))
            Missing.Add(entry.File);
        if (entry.Children != null)
            CollectMissing(entry.Children, Files, Missing);
    }
}

static List<TocEntry> ExpandIncludes(List<TocEntry> Entries, string BaseDir, string SrcDir, HashSet<string> Includes)
{
    var ret = new List<TocEntry>();
    foreach (var entry in Entries) {
        var file = entry.File == null ? null : CombineUrl(BaseDir, entry.File);
        var children = entry.Children == null ? null : ExpandIncludes(entry.Children, BaseDir, SrcDir, Includes);

        if (entry.Include != null) {
            var includePath = CombineUrl(BaseDir, entry.Include);
            var fullPath = Path.Combine(SrcDir, includePath);
            if (!File.Exists(fullPath))
                throw new Exception($"Included table of contents {fullPath} does not exist");

            Includes.Add(includePath);
            var included = JsonSerializer.Deserialize(File.ReadAllText(fullPath), HelpJsonContext.Default.ListTocEntry)
                ?? throw new Exception($"{fullPath} is empty");
            var slash = includePath.LastIndexOf('/');
            children = [.. (children ?? []), .. ExpandIncludes(included, slash == -1 ? "" : includePath[..slash], SrcDir, Includes)];
        }

        ret.Add(new TocEntry(entry.Title, file, children));
    }
    return ret;
}

static void CollectPages(List<TocEntry> Entries, List<(string Title, string File)> Pages)
{
    foreach (var entry in Entries) {
        if (entry.File != null && !Pages.Any(x => x.File == entry.File))
            Pages.Add((entry.Title, entry.File));
        if (entry.Children != null)
            CollectPages(entry.Children, Pages);
    }
}

static string CombineUrl(string BaseDir, string Path) => BaseDir == "" ? Path : BaseDir + "/" + Path;

static string RelativeUrl(string From, string To)
{
    var from = From.Split('/')[..^1];
    var to = To.Split('/');

    var common = 0;
    while (common < from.Length && common < to.Length - 1 && from[common] == to[common])
        common++;

    return String.Join('/', [.. Enumerable.Repeat("..", from.Length - common), .. to[common..]]);
}

static string? Between(string Text, string Start, string End)
{
    var start = Text.IndexOf(Start, StringComparison.OrdinalIgnoreCase);
    if (start == -1)
        return null;
    start += Start.Length;

    var end = Text.IndexOf(End, start, StringComparison.OrdinalIgnoreCase);
    return end == -1 ? null : Text[start..end];
}

static void AppendTocEntries(StringBuilder Sb, List<TocEntry> Entries, int Depth)
{
    var indent = new string(' ', Depth * 2);
    Sb.AppendLine($"{indent}<UL>");
    foreach (var entry in Entries) {
        Sb.Append($"{indent}  <LI><OBJECT type=\"text/sitemap\"><param name=\"Name\" value=\"{WebUtility.HtmlEncode(entry.Title)}\">");
        if (entry.File != null)
            Sb.Append($"<param name=\"Local\" value=\"{WebUtility.HtmlEncode(entry.File)}\">");
        Sb.AppendLine("</OBJECT>");
        if (entry.Children is { Count: > 0 })
            AppendTocEntries(Sb, entry.Children, Depth + 1);
    }
    Sb.AppendLine($"{indent}</UL>");
}

record HelpProject(string Title, string DefaultTopic, List<TocEntry> Contents);

record TocEntry(string Title, string? File = null, List<TocEntry>? Children = null, string? Include = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(HelpProject))]
[JsonSerializable(typeof(List<TocEntry>))]
partial class HelpJsonContext : JsonSerializerContext;
