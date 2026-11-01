#:package Microsoft.CodeAnalysis.CSharp@5.6.0

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/*
 * Generates the plugin API reference pages of the help file from the plugin contract sources.
 *
 * Usage: dotnet run scripts/generate-api-help.cs
 */

var repoDir = Path.GetDirectoryName(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? throw new Exception("Must be run as a file-based app"))!;
var outDir = Path.Combine(repoDir, "docs", "help", "api");
var css = "../help.css";
string[] projects = [
    Path.Combine(repoDir, "src", "RTSharp.Shared.Abstractions"),
    Path.Combine(repoDir, "src", "RTSharp.Shared.Abstractions.Client")
];

var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse);
var types = new Dictionary<string, ApiType>();

var sources = projects
    .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories).Order().Select(file => (Project: project, File: file, Rel: Path.GetRelativePath(project, file).Replace('\\', '/'))))
    .Where(x => !x.Rel.StartsWith("bin/", StringComparison.Ordinal) && !x.Rel.StartsWith("obj/", StringComparison.Ordinal))
    .ToList();

var fragments = new List<(string Key, ApiType Type)>[sources.Count];
Parallel.For(0, sources.Count, index => {
    var (project, file, _) = sources[index];
    var assembly = Path.GetFileName(project);
    var result = fragments[index] = [];

    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file).GetCompilationUnitRoot();
    foreach (var decl in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
        if (decl is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax) || !IsPublicType(decl))
            continue;

        var ns = String.Join('.', decl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(x => x.Name.ToString()));
        var containers = decl.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(x => x.Identifier.Text);
        var name = String.Join('.', [.. containers, Identifier(decl)]);
        var typeParams = TypeParameters(decl);

        var type = new ApiType {
            Namespace = ns,
            Name = name,
            TypeParameters = typeParams,
            Kind = KindOf(decl),
            Assembly = assembly,
            Signature = TypeSignature(decl),
            File = $"{ns}.{name}{(typeParams.Count == 0 ? "" : "-" + typeParams.Count)}.htm",
            Doc = GetDoc(decl)
        };
        type.BaseTypes.AddRange(decl is TypeDeclarationSyntax { BaseList: { } baseList } ? baseList.Types.Select(x => x.Type.ToString()) : []);
        CollectMembers(decl, type);
        result.Add(($"{ns}.{name}`{typeParams.Count}", type));
    }
});

foreach (var (key, fragment) in fragments.SelectMany(x => x)) {
    if (!types.TryGetValue(key, out var type)) {
        types[key] = fragment;
        continue;
    }

    type.Doc ??= fragment.Doc;
    type.BaseTypes.AddRange(fragment.BaseTypes);
    type.Members.AddRange(fragment.Members);
}

foreach (var type in types.Values) {
    var seen = new Dictionary<string, int>();
    for (var x = 0; x < type.Members.Count; x++) {
        var member = type.Members[x];
        var overloads = seen.GetValueOrDefault(member.Name);
        seen[member.Name] = overloads + 1;
        type.Members[x] = member with { Anchor = overloads == 0 ? member.Name : $"{member.Name}-{overloads + 1}" };
    }
}

var byName = new Dictionary<string, List<ApiType>>();
foreach (var type in types.Values) {
    foreach (var name in new[] { type.Name, type.Name[(type.Name.LastIndexOf('.') + 1)..] }.Distinct()) {
        var key = $"{name}`{type.TypeParameters.Count}";
        if (!byName.TryGetValue(key, out var list))
            byName[key] = list = [];
        list.Add(type);
    }
}

if (Directory.Exists(outDir))
    Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

var namespaces = types.Values.GroupBy(x => x.Namespace).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
var toc = new List<TocEntry>();


var body = new StringBuilder();
Paragraph(body, "This part of the manual describes all the types that RT# exposes to the plugins. It has been generated automatically from the source code of the plugin contracts, so it always matches the version of RT# you are using.");
Paragraph(body, "Every plugin references at least the <b>RTSharp.Shared.Abstractions</b> assembly, which contains the headless contracts (torrents, data providers, server daemons, scripts...). Plugins that display some user interface or that register into the menus also reference the <b>RTSharp.Shared.Abstractions.Client</b> assembly, which contains the Avalonia related contracts such as the plugin host.");
Paragraph(body, "The types are organized by namespace:");
body.AppendLine("<div class=\"list\">");
foreach (var ns in namespaces)
    body.AppendLine($"- <a href=\"{Encode(ns.Key)}.htm\">{Encode(ns.Key)}</a>: {ns.Count()} type{(ns.Count() == 1 ? "" : "s")}<br>");
body.AppendLine("</div>");
Paragraph(body, "If you are new to plugin development, we recommend you to read the \"Plugin development\" chapter first, it explains how all these types fit together.");
WritePage("index.htm", "Plugin API reference", ["API reference", "Plugin API"], body.ToString());


foreach (var ns in namespaces) {
    var body = new StringBuilder();
    Paragraph(body, $"The <b>{Encode(ns.Key)}</b> namespace contains the following types:");

    var section = 0;
    foreach (var kind in ns.GroupBy(x => x.Kind).OrderBy(x => KindOrder(x.Key))) {
        Heading(body, $"{++section}. {KindPlural(kind.Key)}:");
        foreach (var type in kind.OrderBy(x => x.Name, StringComparer.Ordinal)) {
            var summary = FirstSentence(RenderDoc(type.Doc?.Element("summary"), type));
            body.AppendLine($"<p><b><a href=\"{Encode(type.File)}\">{Encode(DisplayName(type))}</a></b>{(summary == "" ? "" : ": " + summary)}</p>");
        }
    }

    WritePage($"{ns.Key}.htm", $"{ns.Key} namespace", [ns.Key], body.ToString());

    toc.Add(new TocEntry(ns.Key, $"{ns.Key}.htm", [.. ns.OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new TocEntry($"{DisplayName(x)} {x.Kind}", x.File))]));
}

Parallel.ForEach(types.Values, WriteTypePage);

File.WriteAllText(Path.Combine(outDir, "toc.json"), JsonSerializer.Serialize(toc, TocJsonContext.Default.ListTocEntry));
Console.WriteLine($"Wrote {types.Count} types in {namespaces.Count} namespaces to {outDir}");
return 0;

void WriteTypePage(ApiType Type)
{
    var body = new StringBuilder();
    var summary = RenderDoc(Type.Doc?.Element("summary"), Type);
    Paragraph(body, summary != "" ? summary : $"There is no description for this {Type.Kind} yet.");
    body.AppendLine($"<p><b>Namespace:</b> <a href=\"{Encode(Type.Namespace)}.htm\">{Encode(Type.Namespace)}</a><br>");
    body.AppendLine($"<b>Assembly:</b> {Encode(Type.Assembly)}</p>");

    Heading(body, "Declaration:");
    body.AppendLine($"<pre>{LinkSignature(Type.Signature, Type)}</pre>");

    AppendDocDetails(body, Type.Doc, Type, typeParamsOnly: true);

    var keywords = new List<string> { Type.Name, $"{Type.Name} {Type.Kind}" };
    var section = 0;
    foreach (var group in Type.Members.GroupBy(x => x.Kind).OrderBy(x => MemberKindOrder(x.Key))) {
        Heading(body, $"{++section}. {MemberKindPlural(group.Key)}:");

        foreach (var member in group) {
            var doc = ResolveInheritedDoc(Type, member);
            // Positional record properties are documented by the <param> of the record itself
            var memberSummary = member.RecordParameter != null
                ? RenderDoc(Type.Doc?.Elements("param").FirstOrDefault(x => (string?)x.Attribute("name") == member.RecordParameter), Type)
                : RenderDoc(doc?.Element("summary"), Type);

            body.AppendLine($"<a name=\"{Encode(member.Anchor)}\"></a>");
            body.AppendLine($"<p><b>{Encode(member.Name)}</b>: {(memberSummary != "" ? memberSummary : "There is no description for this member yet.")}</p>");
            body.AppendLine($"<pre>{LinkSignature(member.Signature, Type)}</pre>");
            AppendDocDetails(body, doc, Type, typeParamsOnly: false);

            if (group.Key != "Value")
                keywords.Add($"{member.Name} {group.Key.ToLowerInvariant()}");
        }
    }

    body.AppendLine($"<p class=\"note\">* This page has been generated automatically from the source code of the {Encode(Type.Assembly)} assembly.</p>");
    WritePage(Type.File, $"{DisplayName(Type)} {Type.Kind}", keywords, body.ToString());
}

void AppendDocDetails(StringBuilder Body, XElement? Doc, ApiType Context, bool typeParamsOnly)
{
    if (Doc == null)
        return;

    var lines = new List<string>();
    foreach (var typeParam in Doc.Elements("typeparam"))
        lines.Add($"- <i>{Encode((string?)typeParam.Attribute("name") ?? "")}</i>: {RenderDoc(typeParam, Context)}");
    if (!typeParamsOnly) {
        foreach (var param in Doc.Elements("param"))
            lines.Add($"- <i>{Encode((string?)param.Attribute("name") ?? "")}</i>: {RenderDoc(param, Context)}");
        if (Doc.Element("returns") is { } returns && RenderDoc(returns, Context) is { Length: > 0 } returnsText)
            lines.Add($"- Returns: {returnsText}");
        foreach (var exception in Doc.Elements("exception"))
            lines.Add($"- Throws {LinkCref((string?)exception.Attribute("cref") ?? "", Context)}: {RenderDoc(exception, Context)}");
    }
    if (lines.Count != 0) {
        Body.AppendLine("<div class=\"list\">");
        foreach (var line in lines)
            Body.AppendLine(line + "<br>");
        Body.AppendLine("</div>");
    }

    if (Doc.Element("remarks") is { } remarks && RenderDoc(remarks, Context) is { Length: > 0 } remarksText)
        Paragraph(Body, $"<b>Remarks:</b> {remarksText}");
}

XElement? ResolveInheritedDoc(ApiType Type, ApiMember Member)
{
    if (Member.Doc == null || Member.Doc.Element("inheritdoc") == null)
        return Member.Doc;

    var visited = new HashSet<ApiType>();
    var pending = new Queue<ApiType>([Type]);
    while (pending.TryDequeue(out var current)) {
        if (!visited.Add(current))
            continue;

        if (current != Type) {
            var found = current.Members.FirstOrDefault(x => x.Name == Member.Name && x.Kind == Member.Kind && x.Doc != null && x.Doc.Element("inheritdoc") == null);
            if (found != null)
                return found.Doc;
        }

        foreach (var baseType in current.BaseTypes) {
            var resolved = ResolveType(baseType, current);
            if (resolved != null)
                pending.Enqueue(resolved);
        }
    }
    return Member.Doc;
}

ApiType? ResolveType(string Text, ApiType Context)
{
    // "IPluginHost<TConfig>", "BasePlugin{TConfig}", "Abstractions.Torrent" -> name + arity
    var generic = Text.IndexOfAny(['<', '{']);
    var name = generic == -1 ? Text : Text[..generic];
    var arity = 0;
    if (generic != -1) {
        arity = 1;
        var depth = 0;
        foreach (var ch in Text[generic..]) {
            if (ch is '<' or '{')
                depth++;
            else if (ch is '>' or '}')
                depth--;
            else if (ch == ',' && depth == 1)
                arity++;
        }
    }

    // Whole name first (nested "Outer.Inner"), then the last segment of a qualified one
    var candidates = new List<ApiType>();
    foreach (var probe in new[] { name, name[(name.LastIndexOf('.') + 1)..] }) {
        if (!byName.TryGetValue($"{probe}`{arity}", out var list))
            continue;
        candidates = [.. list.Where(x => $".{x.Namespace}.{x.Name}".EndsWith("." + name, StringComparison.Ordinal))];
        if (candidates.Count != 0)
            break;
    }

    return candidates.FirstOrDefault(x => x.Namespace == Context.Namespace) ?? candidates.FirstOrDefault();
}

string LinkCref(string Cref, ApiType Context)
{
    var cref = Cref.Length > 2 && Cref[1] == ':' ? Cref[2..] : Cref;
    var paren = cref.IndexOf('(');
    if (paren != -1)
        cref = cref[..paren];
    var display = Encode(cref.Replace('{', '<').Replace('}', '>'));

    var type = ResolveType(cref, Context);
    if (type != null)
        return $"<a href=\"{Encode(type.File)}\">{display}</a>";

    // Type.Member, or a member of the current type
    var dot = cref.LastIndexOf('.');
    var owner = dot == -1 ? Context : ResolveType(cref[..dot], Context);
    var memberName = cref[(dot + 1)..];
    var member = owner?.Members.FirstOrDefault(x => x.Name == memberName);
    if (owner != null && member != null)
        return $"<a href=\"{Encode(owner.File)}#{Encode(member.Anchor)}\">{display}</a>";

    return $"<code>{display}</code>";
}

string LinkSignature(string Signature, ApiType Context)
{
    var sb = new StringBuilder();
    var x = 0;
    while (x < Signature.Length) {
        if (!Char.IsLetter(Signature[x]) && Signature[x] != '_') {
            sb.Append(Encode(Signature[x].ToString()));
            x++;
            continue;
        }

        var start = x;
        while (x < Signature.Length && (Char.IsLetterOrDigit(Signature[x]) || Signature[x] == '_'))
            x++;
        var identifier = Signature[start..x];

        // Take type arguments along so the arity is known
        var end = x;
        if (end < Signature.Length && Signature[end] == '<') {
            var depth = 0;
            for (; end < Signature.Length; end++) {
                if (Signature[end] == '<')
                    depth++;
                else if (Signature[end] == '>' && --depth == 0)
                    break;
            }
        }
        var type = ResolveType(end > x && end < Signature.Length ? Signature[start..(end + 1)] : identifier, Context);

        sb.Append(type != null && type != Context ? $"<a href=\"{Encode(type.File)}\">{Encode(identifier)}</a>" : Encode(identifier));
    }
    return sb.ToString();
}

string RenderDoc(XElement? Element, ApiType Context)
{
    if (Element == null)
        return "";

    var sb = new StringBuilder();
    foreach (var node in Element.Nodes()) {
        if (node is XText text) {
            sb.Append(Encode(CollapseWhitespace(text.Value)));
            continue;
        }
        if (node is not XElement e)
            continue;

        switch (e.Name.LocalName) {
            case "see":
            case "seealso":
                if (e.Attribute("cref") is { } cref)
                    sb.Append(LinkCref(cref.Value, Context));
                else if (e.Attribute("langword") is { } langword)
                    sb.Append($"<code>{Encode(langword.Value)}</code>");
                else if (e.Attribute("href") is { } href)
                    sb.Append($"<a href=\"{Encode(href.Value)}\">{(e.Value.Length != 0 ? Encode(e.Value) : Encode(href.Value))}</a>");
                break;
            case "c":
                sb.Append($"<code>{Encode(e.Value)}</code>");
                break;
            case "code":
                sb.Append($"<pre>{Encode(e.Value.Trim('\r', '\n'))}</pre>");
                break;
            case "paramref":
            case "typeparamref":
                sb.Append($"<i>{Encode((string?)e.Attribute("name") ?? "")}</i>");
                break;
            case "para":
                sb.Append("<br><br>").Append(RenderDoc(e, Context));
                break;
            case "list":
                sb.Append("<br>");
                foreach (var item in e.Elements("item")) {
                    var term = RenderDoc(item.Element("term"), Context);
                    var description = item.Element("description") != null ? RenderDoc(item.Element("description"), Context) : RenderDoc(item, Context);
                    sb.Append($"- {(term != "" ? $"<b>{term}</b>: " : "")}{description}<br>");
                }
                break;
            case "b":
            case "i":
                sb.Append($"<{e.Name.LocalName}>{RenderDoc(e, Context)}</{e.Name.LocalName}>");
                break;
            default:
                sb.Append(RenderDoc(e, Context));
                break;
        }
    }
    return sb.ToString().Trim();
}

void WritePage(string File, string Title, IEnumerable<string> Keywords, string Body)
{
    var html = $"""
        <!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 3.2 Final//EN">
        <html>
        <head>
           <title>{Encode(Title)}</title>
           <meta name="keywords" content="{Encode(String.Join(",", Keywords.Select(x => x.Replace(",", "")).Distinct()))}">
           <link rel="stylesheet" type="text/css" href="{css}">
        </head>
        <body bgcolor="#FFFFFF">

        {Body}
        </body>
        </html>

        """;
    System.IO.File.WriteAllText(Path.Combine(outDir, File), html);
}

static void Paragraph(StringBuilder Sb, string Html) => Sb.AppendLine($"<p>{Html}</p>");

static void Heading(StringBuilder Sb, string Text) => Sb.AppendLine($"<h3>{Encode(Text)}</h3>");

static string Encode(string Text) => WebUtility.HtmlEncode(Text);

static string DisplayName(ApiType Type) => Type.TypeParameters.Count == 0 ? Type.Name : $"{Type.Name}<{String.Join(", ", Type.TypeParameters)}>";

static string FirstSentence(string Html)
{
    var dot = Html.IndexOf(". ", StringComparison.Ordinal);
    return dot == -1 ? Html : Html[..(dot + 1)];
}

static string CollapseWhitespace(string Text)
{
    var sb = new StringBuilder(Text.Length);
    var lastWasSpace = false;
    foreach (var ch in Text) {
        if (Char.IsWhiteSpace(ch)) {
            if (!lastWasSpace)
                sb.Append(' ');
            lastWasSpace = true;
        } else {
            sb.Append(ch);
            lastWasSpace = false;
        }
    }
    return sb.ToString();
}

static XElement? GetDoc(SyntaxNode Node)
{
    var trivia = Node.GetLeadingTrivia().Select(x => x.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().FirstOrDefault();
    if (trivia == null)
        return null;

    var lines = trivia.ToFullString().Split('\n').Select(line => {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("///", StringComparison.Ordinal) ? trimmed[3..] : trimmed;
    });

    try {
        return XElement.Parse($"<doc>{String.Join('\n', lines)}</doc>", LoadOptions.PreserveWhitespace);
    } catch (XmlException ex) {
        Console.Error.WriteLine($"warning: malformed XML documentation at {Node.GetLocation().GetLineSpan()}: {ex.Message}");
        return null;
    }
}

static bool IsPublicType(MemberDeclarationSyntax Decl)
{
    SyntaxNode? node = Decl;
    while (node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax) {
        if (!((MemberDeclarationSyntax)node).Modifiers.Any(SyntaxKind.PublicKeyword))
            return false;
        node = node.Parent;
    }
    return true;
}

static string Identifier(MemberDeclarationSyntax Decl) => Decl switch {
    BaseTypeDeclarationSyntax t => t.Identifier.Text,
    DelegateDeclarationSyntax d => d.Identifier.Text,
    _ => throw new InvalidOperationException()
};

static List<string> TypeParameters(MemberDeclarationSyntax Decl) => Decl switch {
    TypeDeclarationSyntax { TypeParameterList: { } list } => [.. list.Parameters.Select(x => x.Identifier.Text)],
    DelegateDeclarationSyntax { TypeParameterList: { } list } => [.. list.Parameters.Select(x => x.Identifier.Text)],
    _ => []
};

static string KindOf(MemberDeclarationSyntax Decl) => Decl switch {
    InterfaceDeclarationSyntax => "interface",
    RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
    StructDeclarationSyntax => "struct",
    EnumDeclarationSyntax => "enum",
    DelegateDeclarationSyntax => "delegate",
    _ => "class"
};

static int KindOrder(string Kind) => Kind switch {
    "interface" => 0, "class" => 1, "record" => 2, "struct" => 3, "record struct" => 4, "enum" => 5, _ => 6
};

static string KindPlural(string Kind) => Kind switch {
    "interface" => "Interfaces", "class" => "Classes", "record" => "Records", "struct" => "Structures",
    "record struct" => "Record structures", "enum" => "Enumerations", _ => "Delegates"
};

static int MemberKindOrder(string Kind) => Kind switch {
    "Value" => 0, "Constructor" => 1, "Property" => 2, "Indexer" => 3, "Method" => 4, "Event" => 5, "Operator" => 6, _ => 7
};

static string MemberKindPlural(string Kind) => Kind switch {
    "Value" => "Values", "Constructor" => "Constructors", "Property" => "Properties", "Indexer" => "Indexers",
    "Method" => "Methods", "Event" => "Events", "Operator" => "Operators", _ => "Fields"
};

static string Modifiers(SyntaxTokenList Modifiers) => String.Join(' ', Modifiers.Where(x => !x.IsKind(SyntaxKind.PartialKeyword)).Select(x => x.Text));

static string Normalize(SyntaxNode? Node) => Node == null ? "" : Node.NormalizeWhitespace().ToString();

static string JoinParts(params string[] Parts) => String.Join(' ', Parts.Where(x => x.Length != 0));

static string Constraints(SyntaxList<TypeParameterConstraintClauseSyntax> Clauses) =>
    Clauses.Count == 0 ? "" : " " + String.Join(' ', Clauses.Select(x => Normalize(x)));

static string TypeSignature(MemberDeclarationSyntax Decl)
{
    if (Decl is DelegateDeclarationSyntax d)
        return JoinParts(Modifiers(d.Modifiers), "delegate", Normalize(d.ReturnType), d.Identifier.Text + Normalize(d.TypeParameterList) + Normalize(d.ParameterList)) + Constraints(d.ConstraintClauses);

    var t = (BaseTypeDeclarationSyntax)Decl;
    var keyword = Decl switch {
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        TypeDeclarationSyntax td => td.Keyword.Text,
        EnumDeclarationSyntax => "enum",
        _ => ""
    };
    var signature = JoinParts(Modifiers(t.Modifiers), keyword, t.Identifier.Text);
    if (Decl is TypeDeclarationSyntax typeDecl)
        signature += Normalize(typeDecl.TypeParameterList) + Normalize(typeDecl.ParameterList);
    if (t.BaseList != null)
        signature += " " + Normalize(t.BaseList);
    if (Decl is TypeDeclarationSyntax withConstraints)
        signature += Constraints(withConstraints.ConstraintClauses);
    return signature;
}

static string Accessors(BasePropertyDeclarationSyntax Property)
{
    if (Property is PropertyDeclarationSyntax { ExpressionBody: not null } or IndexerDeclarationSyntax { ExpressionBody: not null })
        return "{ get; }";

    var accessors = Property.AccessorList?.Accessors
        .Where(x => !x.Modifiers.Any(SyntaxKind.PrivateKeyword) && !x.Modifiers.Any(SyntaxKind.InternalKeyword))
        .Select(x => JoinParts(Modifiers(x.Modifiers), x.Keyword.Text) + ";") ?? [];
    return $"{{ {String.Join(' ', accessors)} }}";
}

static void CollectMembers(MemberDeclarationSyntax Decl, ApiType Type)
{
    var isInterface = Decl is InterfaceDeclarationSyntax;

    bool Visible(MemberDeclarationSyntax Member)
    {
        if (Member.Modifiers.Any(SyntaxKind.PrivateKeyword))
            return false;
        if (isInterface)
            return true;
        return Member.Modifiers.Any(SyntaxKind.PublicKeyword) || Member.Modifiers.Any(SyntaxKind.ProtectedKeyword);
    }

    void Add(string Kind, string Name, string Signature, XElement? Doc, string? RecordParameter = null) =>
        Type.Members.Add(new ApiMember(Kind, Name, Signature, Doc, Name, RecordParameter));

    if (Decl is EnumDeclarationSyntax e) {
        foreach (var value in e.Members)
            Add("Value", value.Identifier.Text, value.Identifier.Text + (value.EqualsValue != null ? " " + Normalize(value.EqualsValue) : ""), GetDoc(value));
        return;
    }

    if (Decl is RecordDeclarationSyntax { ParameterList: { } positional } record) {
        var accessors = record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) && !record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ? "{ get; set; }" : "{ get; init; }";
        foreach (var param in positional.Parameters)
            Add("Property", param.Identifier.Text, $"public {Normalize(param.Type)} {param.Identifier.Text} {accessors}", null, param.Identifier.Text);
    }

    if (Decl is not TypeDeclarationSyntax typeDecl)
        return;

    foreach (var member in typeDecl.Members) {
        if (member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax || !Visible(member))
            continue;

        var mods = Modifiers(member.Modifiers);
        switch (member) {
            case ConstructorDeclarationSyntax c when !c.Modifiers.Any(SyntaxKind.StaticKeyword):
                Add("Constructor", c.Identifier.Text, JoinParts(mods, c.Identifier.Text + Normalize(c.ParameterList)), GetDoc(c));
                break;
            case MethodDeclarationSyntax m when m.ExplicitInterfaceSpecifier == null:
                Add("Method", m.Identifier.Text, JoinParts(mods, Normalize(m.ReturnType), m.Identifier.Text + Normalize(m.TypeParameterList) + Normalize(m.ParameterList)) + Constraints(m.ConstraintClauses), GetDoc(m));
                break;
            case PropertyDeclarationSyntax p when p.ExplicitInterfaceSpecifier == null:
                Add("Property", p.Identifier.Text, JoinParts(mods, Normalize(p.Type), p.Identifier.Text, Accessors(p)), GetDoc(p));
                break;
            case IndexerDeclarationSyntax i when i.ExplicitInterfaceSpecifier == null:
                Add("Indexer", "this[]", JoinParts(mods, Normalize(i.Type), "this" + Normalize(i.ParameterList), Accessors(i)), GetDoc(i));
                break;
            case EventDeclarationSyntax ev when ev.ExplicitInterfaceSpecifier == null:
                Add("Event", ev.Identifier.Text, JoinParts(mods, "event", Normalize(ev.Type), ev.Identifier.Text), GetDoc(ev));
                break;
            case EventFieldDeclarationSyntax ef:
                foreach (var variable in ef.Declaration.Variables)
                    Add("Event", variable.Identifier.Text, JoinParts(mods, "event", Normalize(ef.Declaration.Type), variable.Identifier.Text), GetDoc(ef));
                break;
            case FieldDeclarationSyntax f:
                foreach (var variable in f.Declaration.Variables) {
                    var value = f.Modifiers.Any(SyntaxKind.ConstKeyword) && variable.Initializer != null ? " " + Normalize(variable.Initializer) : "";
                    Add("Field", variable.Identifier.Text, JoinParts(mods, Normalize(f.Declaration.Type), variable.Identifier.Text) + value, GetDoc(f));
                }
                break;
            case OperatorDeclarationSyntax o:
                Add("Operator", "operator " + o.OperatorToken.Text, JoinParts(mods, Normalize(o.ReturnType), "operator " + o.OperatorToken.Text + Normalize(o.ParameterList)), GetDoc(o));
                break;
            case ConversionOperatorDeclarationSyntax co:
                Add("Operator", $"{co.ImplicitOrExplicitKeyword.Text} operator", JoinParts(mods, co.ImplicitOrExplicitKeyword.Text, "operator", Normalize(co.Type) + Normalize(co.ParameterList)), GetDoc(co));
                break;
        }
    }
}

class ApiType
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required List<string> TypeParameters { get; init; }
    public required string Kind { get; init; }
    public required string Assembly { get; init; }
    public required string Signature { get; init; }
    public required string File { get; init; }
    public XElement? Doc { get; set; }
    public List<string> BaseTypes { get; } = [];
    public List<ApiMember> Members { get; } = [];
}

record ApiMember(string Kind, string Name, string Signature, XElement? Doc, string Anchor, string? RecordParameter);

record TocEntry(string Title, string? File = null, List<TocEntry>? Children = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<TocEntry>))]
partial class TocJsonContext : JsonSerializerContext;
