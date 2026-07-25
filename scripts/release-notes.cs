using System.Diagnostics;
using System.Text;

if (args.Length != 2) {
    Console.Error.WriteLine("Usage: dotnet run scripts/release-notes.cs -- <version> <owner/repo>");
    Console.Error.WriteLine("Writes release_notes.md and assets.txt into the working directory.");
    return 1;
}

var version = args[0];
var repo = args[1];

string[] rids = ["linux-x64", "linux-arm64", "win-x64", "win-arm64"];

var assets = new List<Asset>();
foreach (var rid in rids) {
    var linux = rid.StartsWith("linux-");
    var pkgArch = rid == "linux-x64" ? "x86_64" : "aarch64";
    var debArch = rid == "linux-x64" ? "amd64" : "arm64";

    foreach (var selfContained in new[] { false, true }) {
        var sc = selfContained ? "-self-contained" : "";

        assets.Add(new Asset($"RTSharp-{rid}{sc}-v{version}.zip", COMPONENT.CLIENT, rid, selfContained, FORMAT.ZIP));
        if (!linux)
            continue;

        assets.Add(new Asset($"RTSharp-Daemon-{rid}{sc}-v{version}.zip", COMPONENT.DAEMON, rid, selfContained, FORMAT.ZIP));

        if (selfContained)
            continue;

        foreach (var (component, pkg) in new[] { (COMPONENT.CLIENT, "rtsharp"), (COMPONENT.DAEMON, "rtsharp-daemon") }) {
            assets.Add(new Asset($"{pkg}_{version}_{debArch}.deb", component, rid, selfContained, FORMAT.DEB));
            assets.Add(new Asset($"{pkg}-{version}-1.{pkgArch}.rpm", component, rid, selfContained, FORMAT.RPM));
            assets.Add(new Asset($"{pkg}-{version}-1-{pkgArch}.pkg.tar.zst", component, rid, selfContained, FORMAT.PKG_TAR_ZST));
        }
    }
}

var missing = assets.Where(x => !File.Exists(x.File)).ToArray();
if (missing.Length > 0) {
    Console.Error.WriteLine("Missing expected release assets:");
    foreach (var asset in missing)
        Console.Error.WriteLine($"  {asset.File}");
    return 1;
}

var notes = new StringBuilder();
notes.AppendLine("## Downloads");
notes.AppendLine();

foreach (var component in new[] { COMPONENT.CLIENT, COMPONENT.DAEMON }) {
    notes.AppendLine($"### {(component == COMPONENT.CLIENT ? "Client" : "Daemon")}");
    notes.AppendLine();
    notes.AppendLine("| Platform | Runtime self-contained | zip | deb | rpm | pkg.tar.zst |");
    notes.AppendLine("| --- | --- | :-: | :-: | :-: | :-: |");

    foreach (var rid in rids) {
        foreach (var selfContained in new[] { false, true }) {
            var row = assets
                .Where(x => x.Component == component && x.Rid == rid && x.SelfContained == selfContained)
                .ToDictionary(x => x.Format);

            if (row.Count == 0)
                continue;

            var platformLabel = rid switch {
                "linux-x64" => "Linux x64",
                "linux-arm64" => "Linux arm64",
                "win-x64" => "Windows x64",
                "win-arm64" => "Windows arm64",
                _ => rid
            };

            notes.Append($"| {platformLabel} | {(selfContained ? "Yes" : "")} |");
            foreach (var format in new[] { FORMAT.ZIP, FORMAT.DEB, FORMAT.RPM, FORMAT.PKG_TAR_ZST }) {
                var formatLabel = format switch {
                    FORMAT.ZIP => ".zip",
                    FORMAT.DEB => ".deb",
                    FORMAT.RPM => ".rpm",
                    FORMAT.PKG_TAR_ZST => ".pkg.tar.zst",
                    _ => format.ToString()
                };

                notes.Append($" {(row.TryGetValue(format, out var asset) ? $"[{formatLabel}]({Url(asset.File)})" : "–")} |");
            }
            notes.AppendLine();
        }
    }
    notes.AppendLine();
}

notes.AppendLine("Docker image for this version:");
notes.AppendLine();
notes.AppendLine("```");
notes.AppendLine($"docker pull ghcr.io/{repo.ToLowerInvariant()}/daemon:{version}");
notes.AppendLine("```");
notes.AppendLine();
notes.AppendLine("## Changes");
notes.AppendLine();

var previousTag = Capture("git", ["describe", "--tags", "--abbrev=0", $"v{version}^"], AllowFailure: true).Trim();
var range = String.IsNullOrEmpty(previousTag) ? "HEAD" : $"{previousTag}..HEAD";

var log = Capture("git", ["log", "--format=%B%x00", range]);
foreach (var commit in log.Split('\0')) {
    var lines = commit.Replace("\r\n", "\n").Split('\n').Select(x => x.TrimEnd()).SkipWhile(String.IsNullOrEmpty).ToArray();
    if (lines.Length == 0)
        continue;

    notes.AppendLine($"- {lines[0]}");
    foreach (var line in lines.Skip(1).Reverse().SkipWhile(String.IsNullOrEmpty).Reverse())
        notes.AppendLine(String.IsNullOrEmpty(line) ? "" : $"  {line}");
}

File.WriteAllText("release_notes.md", notes.ToString());
File.WriteAllLines("assets.txt", assets.Select(A => A.File));

return 0;

string Url(string File) => $"https://github.com/{repo}/releases/download/v{version}/{File}";

static string Capture(string Cmd, string[] Arguments, bool AllowFailure = false)
{
    var psi = new ProcessStartInfo(Cmd) {
        UseShellExecute = false,
        RedirectStandardOutput = true
    };
    foreach (var arg in Arguments)
        psi.ArgumentList.Add(arg);
    using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start {Cmd}");
    var output = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();
    if (proc.ExitCode != 0)
        return AllowFailure ? "" : throw new Exception($"{Cmd} exited with code {proc.ExitCode}");
    return output;
}

record Asset(string File, COMPONENT Component, string Rid, bool SelfContained, FORMAT Format);

enum COMPONENT
{
    CLIENT,
    DAEMON
}

enum FORMAT
{
    ZIP,
    DEB,
    RPM,
    PKG_TAR_ZST
}
