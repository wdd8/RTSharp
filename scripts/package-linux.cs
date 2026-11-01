using System.Diagnostics;
using System.Text;

if (args.Length != 4) {
    Console.Error.WriteLine("Usage: dotnet run scripts/package-linux.cs -- <deb|rpm|pkg.tar.zst> <linux-x64|linux-arm64> <version> <client|daemon>");
    return 1;
}

var format = args[0];   // deb, rpm or pkg.tar.zst
var rid = args[1];      // linux-x64 or linux-arm64
var version = args[2];  // 0.0.1
var type = args[3];     // client / daemon

if (format is not ("deb" or "rpm" or "pkg.tar.zst"))
    throw new ArgumentException($"Unsupported format: {format}");

var arch = (format, rid) switch {
    ("deb", "linux-x64")   => "amd64",
    ("deb", "linux-arm64") => "arm64",
    (_, "linux-x64")            => "x86_64",
    (_, "linux-arm64")          => "aarch64",
    _ => throw new ArgumentException($"Unsupported RID: {rid}")
};

var pkg = type switch {
    "client" => new Package(
        PkgName: "rtsharp",
        SrcDir: rid,
        InstallDir: "usr/lib/rtsharp",
        ExecName: "RTSharp",
        BinName: "rtsharp",
        Recommends: [
            new("mpv", "media player used to preview torrent contents"),
            new("xchm", "viewer for the RTSharp.chm help file opened with F1")
        ],
        Description: "RTSharp client GUI",
        Csproj: "src/RTSharp/RTSharp.csproj",
        IsDaemon: false
    ),
    "daemon" => new Package(
        PkgName: "rtsharp-daemon",
        SrcDir: $"{rid}-daemon",
        InstallDir: "usr/lib/rtsharp-daemon",
        ExecName: "RTSharp.Daemon",
        BinName: "rtsharp-daemon",
        Recommends: [],
        Description: "RTSharp daemon service",
        Csproj: "src/RTSharp.Daemon/RTSharp.Daemon.csproj",
        IsDaemon: true
    ),
    _ => throw new ArgumentException($"Unknown type: {type}")
};

var dotnetVersion = TargetFrameworkVersion(pkg.Csproj);

var runtimeDep = (format, pkg.IsDaemon) switch {
    ("pkg.tar.zst", true)  => $"aspnet-runtime-{dotnetVersion}",
    (_, true)            => $"aspnetcore-runtime-{dotnetVersion}",
    (_, false)           => $"dotnet-runtime-{dotnetVersion}"
};

var outFile = format switch {
    "deb"  => $"{pkg.PkgName}_{version}_{arch}.deb",
    "rpm"  => $"{pkg.PkgName}-{version}-1.{arch}.rpm",
    "pkg.tar.zst" => $"{pkg.PkgName}-{version}-1-{arch}.pkg.tar.zst",
    _ => throw new UnreachableException()
};

Console.WriteLine($"Packaging {outFile}...");

var stage = Path.GetFullPath($"stage-{format}-{pkg.PkgName}-{arch}");
if (Directory.Exists(stage))
    Directory.Delete(stage, recursive: true);

BuildTree(stage, pkg, format);

switch (format) {
    case "deb": PackDeb(stage, pkg, version, arch, runtimeDep, outFile); break;
    case "rpm": PackRpm(stage, pkg, version, arch, runtimeDep, outFile); break;
    case "pkg.tar.zst": PackPkgTarZst(stage, pkg, version, arch, runtimeDep, outFile); break;
}

Directory.Delete(stage, recursive: true);

if (!File.Exists(outFile))
    throw new Exception($"{format} packaging did not produce {outFile}");

Console.WriteLine($"Built {outFile} ({new FileInfo(outFile).Length / 1024 / 1024} MiB)");
return 0;

static void BuildTree(string Stage, Package Pkg, string Format)
{
    Directory.CreateDirectory(Path.Combine(Stage, Pkg.InstallDir));
    Directory.CreateDirectory(Path.Combine(Stage, "usr", "bin"));

    CopyDirectory(Pkg.SrcDir, Path.Combine(Stage, Pkg.InstallDir));

    if (Pkg.IsDaemon) {
        var etcDir = Path.Combine(Stage, "etc", "rtsharp-daemon");
        Directory.CreateDirectory(etcDir);
        File.Move(Path.Combine(Stage, Pkg.InstallDir, "appsettings.json"), Path.Combine(etcDir, "appsettings.json"));
    }

    File.SetUnixFileMode(Path.Combine(Stage, Pkg.InstallDir, Pkg.ExecName), Consts.ExecMode);
    File.CreateSymbolicLink(Path.Combine(Stage, "usr", "bin", Pkg.BinName), $"/{Pkg.InstallDir}/{Pkg.ExecName}");

    Run("dotnet", ["run", "scripts/build-man.cs", "--", Pkg.BinName, Path.Combine(Stage, "usr", "share", "man")]);

    if (!Pkg.IsDaemon)
        return;

    var systemdDir = Path.Combine(Stage, Format == "deb" ? "lib" : Path.Combine("usr", "lib"), "systemd", "system");
    Directory.CreateDirectory(systemdDir);
    File.WriteAllText(Path.Combine(systemdDir, Consts.ServiceFile),
"""
[Unit]
Description=RTSharp Daemon
After=network.target

[Service]
Type=simple
User=__CHANGE_ME__
Group=__CHANGE_ME__
ExecStart=/usr/lib/rtsharp-daemon/RTSharp.Daemon
Restart=on-failure
RestartSec=5
StateDirectory=rtsharp-daemon
NoNewPrivileges=yes
PrivateTmp=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectKernelLogs=yes
ProtectClock=yes
LockPersonality=yes
RestrictSUIDSGID=yes
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX

[Install]
WantedBy=multi-user.target
""");
}

static void PackDeb(string Stage, Package Pkg, string Version, string Arch, string RuntimeDep, string OutFile)
{
    var debianDir = Path.Combine(Stage, "DEBIAN");
    Directory.CreateDirectory(debianDir);

    var control = new StringBuilder();
    control.AppendLine($"Package: {Pkg.PkgName}");
    control.AppendLine($"Version: {Version}");
    control.AppendLine($"Architecture: {Arch}");
    control.AppendLine($"Maintainer: {Consts.Maintainer}");
    control.AppendLine($"Installed-Size: {InstalledSize(Stage) / 1024}");
    control.AppendLine($"Depends: {RuntimeDep}");
    if (Pkg.Recommends.Length != 0)
        control.AppendLine($"Recommends: {String.Join(", ", Pkg.Recommends.Select(x => x.Name))}");
    control.AppendLine("Section: utils");
    control.AppendLine("Priority: optional");
    control.AppendLine($"Homepage: {Consts.Url}");
    control.AppendLine($"Description: {Pkg.Description}");

    File.WriteAllText(Path.Combine(debianDir, "control"), control.ToString());

    if (Pkg.IsDaemon) {
        File.WriteAllText(Path.Combine(debianDir, "conffiles"), $"/{Consts.ConfigFile}\n");

        var postinst = Path.Combine(debianDir, "postinst");
        File.WriteAllText(postinst,
"""
#!/bin/bash
set -e

SERVICE_FILE=/lib/systemd/system/rtsharp-daemon.service

echo ""
echo "RT# Daemon needs a user account to run as."

USERS=$(awk -F: '$3 >= 1000 && $3 < 65534 { print $1 }' /etc/passwd | sort | tr '\n' ' ')
if [ -n "$USERS" ]; then
    echo "Available users: $USERS"
fi

while true; do
    read -rp "Run rtsharp-daemon as user: " RUN_USER
    if [ -z "$RUN_USER" ]; then
        echo "User cannot be empty." >&2
        continue
    fi
    if ! id -u "$RUN_USER" > /dev/null 2>&1; then
        echo "User '$RUN_USER' does not exist." >&2
        continue
    fi
    break
done

sed -i "s/__CHANGE_ME__/$RUN_USER/g" "$SERVICE_FILE"

systemctl daemon-reload || true
""");
        File.SetUnixFileMode(postinst, Consts.ExecMode);

        var prerm = Path.Combine(debianDir, "prerm");
        File.WriteAllText(prerm,
"""
#!/bin/bash
set -e

# dpkg also runs prerm on upgrade ("upgrade <ver>"), where disabling the unit would
# leave the daemon down for good since postinst never re-enables it.
if [ "$1" != "remove" ]; then
    exit 0
fi

if systemctl is-active --quiet rtsharp-daemon 2>/dev/null; then
    systemctl stop rtsharp-daemon || true
fi
if systemctl is-enabled --quiet rtsharp-daemon 2>/dev/null; then
    systemctl disable rtsharp-daemon || true
fi
""");
        File.SetUnixFileMode(prerm, Consts.ExecMode);
    }

    Run("dpkg-deb", ["--build", "--root-owner-group", Stage, OutFile]);
}

static void PackRpm(string Stage, Package Pkg, string Version, string Arch, string RuntimeDep, string OutFile)
{
    var topDir = Path.GetFullPath($"rpmbuild-{Pkg.PkgName}-{Arch}");
    if (Directory.Exists(topDir))
        Directory.Delete(topDir, recursive: true);
    Directory.CreateDirectory(topDir);

    var spec = new StringBuilder();
    spec.AppendLine("%global __os_install_post %{nil}");
    spec.AppendLine("%global debug_package %{nil}");
    spec.AppendLine("%global _build_id_links none");
    spec.AppendLine();
    spec.AppendLine($"Name:           {Pkg.PkgName}");
    spec.AppendLine($"Version:        {Version}");
    spec.AppendLine($"Release:        1");
    spec.AppendLine($"Summary:        {Pkg.Description}");
    spec.AppendLine($"License:        {Consts.License}");
    spec.AppendLine($"URL:            {Consts.Url}");
    spec.AppendLine("AutoReqProv:    no");
    spec.AppendLine($"Requires:       {RuntimeDep}");
    foreach (var recommendation in Pkg.Recommends)
        spec.AppendLine($"Recommends:     {recommendation.Name}");
    if (Pkg.IsDaemon)
        spec.AppendLine("Requires:       systemd");
    spec.AppendLine();
    spec.AppendLine("%description");
    spec.AppendLine(Pkg.Description);
    spec.AppendLine();
    spec.AppendLine("%install");
    spec.AppendLine($"cp -a \"{Stage}/.\" \"%{{buildroot}}/\"");
    spec.AppendLine();
    spec.AppendLine("%files");
    spec.AppendLine($"/{Pkg.InstallDir}");
    spec.AppendLine($"/usr/bin/{Pkg.BinName}");
    spec.AppendLine($"/usr/share/man/man*/{Pkg.BinName}.*");
    if (Pkg.IsDaemon) {
        spec.AppendLine("%dir /etc/rtsharp-daemon");
        spec.AppendLine($"%config(noreplace) /{Consts.ConfigFile}");
        spec.AppendLine($"/usr/lib/systemd/system/{Consts.ServiceFile}");
        spec.AppendLine();
        spec.AppendLine("%post");
        spec.AppendLine("systemctl daemon-reload || true");
        spec.AppendLine($"cat <<'EOF'\n{Consts.RunAsHint}\nEOF");
        spec.AppendLine();
        spec.AppendLine("%preun");
        spec.AppendLine("if [ $1 -eq 0 ]; then");
        spec.AppendLine("    systemctl --no-reload disable --now rtsharp-daemon || true");
        spec.AppendLine("fi");
        spec.AppendLine();
        spec.AppendLine("%postun");
        spec.AppendLine("systemctl daemon-reload || true");
    }
    spec.AppendLine();
    spec.AppendLine("%changelog");

    var specPath = Path.Combine(topDir, $"{Pkg.PkgName}.spec");
    File.WriteAllText(specPath, spec.ToString());

    Run("rpmbuild", [
        "--define", $"_topdir {topDir}",
        "--define", $"_rpmdir {Directory.GetCurrentDirectory()}",
        "--define", "_rpmfilename %{NAME}-%{VERSION}-%{RELEASE}.%{ARCH}.rpm",
        "--target", Arch,
        "-bb", specPath
    ]);

    Directory.Delete(topDir, recursive: true);
}

static void PackPkgTarZst(string Stage, Package Pkg, string Version, string Arch, string RuntimeDep, string OutFile)
{
    var info = new StringBuilder();
    info.AppendLine($"pkgname = {Pkg.PkgName}");
    info.AppendLine($"pkgbase = {Pkg.PkgName}");
    info.AppendLine($"pkgver = {Version}-1");
    info.AppendLine($"pkgdesc = {Pkg.Description}");
    info.AppendLine($"url = {Consts.Url}");
    info.AppendLine($"builddate = {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    info.AppendLine($"packager = {Consts.Maintainer}");
    info.AppendLine($"size = {InstalledSize(Stage)}");
    info.AppendLine($"arch = {Arch}");
    info.AppendLine($"license = {Consts.License}");
    info.AppendLine($"depend = {RuntimeDep}");
    foreach (var recommendation in Pkg.Recommends)
        info.AppendLine($"optdepend = {recommendation.Name}: {recommendation.Reason}");
    if (Pkg.IsDaemon)
        info.AppendLine($"backup = {Consts.ConfigFile}");

    File.WriteAllText(Path.Combine(Stage, ".PKGINFO"), info.ToString());

    var entries = new List<string> { ".PKGINFO", ".MTREE" };

    if (Pkg.IsDaemon) {
        File.WriteAllText(Path.Combine(Stage, ".INSTALL"),
$$"""
post_install() {
    systemctl daemon-reload || true
    cat <<'EOF'
{{Consts.RunAsHint}}
EOF
}

post_upgrade() {
    systemctl daemon-reload || true
}

pre_remove() {
    systemctl --no-reload disable --now rtsharp-daemon || true
}

post_remove() {
    systemctl daemon-reload || true
}
""");
        entries.Add(".INSTALL");
    }

    var payload = new DirectoryInfo(Stage).EnumerateDirectories().Select(D => D.Name).Order().ToArray();

    Run("bsdtar", [
        "-C", Stage,
        "-czf", Path.Combine(Stage, ".MTREE"),
        "--format=mtree",
        "--options=!all,use-set,type,uid,gid,mode,time,size,md5,sha256,link",
        "--uid", "0", "--gid", "0", "--uname", "root", "--gname", "root",
        ..entries.Where(E => E != ".MTREE"),
        ..payload
    ]);

    var tar = Path.GetFullPath($"{OutFile}.tmp.tar");
    Run("bsdtar", [
        "-C", Stage,
        "-cf", tar,
        "--uid", "0", "--gid", "0", "--uname", "root", "--gname", "root",
        ..entries,
        ..payload
    ]);

    Run("zstd", ["-19", "-T0", "-q", "-f", "-o", Path.GetFullPath(OutFile), tar]);
    File.Delete(tar);
}

static string TargetFrameworkVersion(string Csproj)
{
    const string open = "<TargetFramework>";
    const string close = "</TargetFramework>";

    var xml = File.ReadAllText(Csproj);
    var start = xml.IndexOf(open, StringComparison.Ordinal);
    var end = start < 0 ? -1 : xml.IndexOf(close, start + open.Length, StringComparison.Ordinal);
    if (end < 0)
        throw new Exception($"No <TargetFramework> found in {Csproj}");

    var tfm = xml[(start + open.Length)..end].Trim();

    return tfm[3..];
}

static long InstalledSize(string Stage) => Directory
    .EnumerateFiles(Stage, "*", SearchOption.AllDirectories)
    .Where(F => !new FileInfo(F).Attributes.HasFlag(FileAttributes.ReparsePoint))
    .Sum(F => new FileInfo(F).Length);

static void CopyDirectory(string Src, string Dst)
{
    Directory.CreateDirectory(Dst);
    foreach (var file in Directory.EnumerateFiles(Src))
    {
        var dstFile = Path.Combine(Dst, Path.GetFileName(file));
        File.Copy(file, dstFile, overwrite: true);
        File.SetUnixFileMode(dstFile, File.GetUnixFileMode(file));
    }
    foreach (var dir in Directory.EnumerateDirectories(Src))
        CopyDirectory(dir, Path.Combine(Dst, Path.GetFileName(dir)));
}

static void Run(string Cmd, string[] Arguments)
{
    var psi = new ProcessStartInfo(Cmd) {
        UseShellExecute = false
    };
    foreach (var arg in Arguments)
        psi.ArgumentList.Add(arg);
    using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start {Cmd}");
    proc.WaitForExit();
    if (proc.ExitCode != 0)
        throw new Exception($"{Cmd} exited with code {proc.ExitCode}");
}

record Package(
    string PkgName,
    string SrcDir,
    string InstallDir,
    string ExecName,
    string BinName,
    Recommendation[] Recommends,
    string Description,
    string Csproj,
    bool IsDaemon
);

record Recommendation(string Name, string Reason);

static class Consts
{
    public const string Maintainer = "wdd8 <wdd@riseup.net>";
    public const string License = "MIT";
    public const string Url = "https://github.com/wdd8/RTSharp";

    public const string ServiceFile = "rtsharp-daemon.service";
    public const string ConfigFile = "etc/rtsharp-daemon/appsettings.json";

    public const UnixFileMode ExecMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public const string RunAsHint =
$"""

rtsharp-daemon will not start until you set the user to run as. It needs
read/write access to the files of the torrent daemon it manages.

    systemctl edit rtsharp-daemon

    [Service]
    User=<your torrent daemon user>
    Group=<your torrent daemon user>

    systemctl enable --now rtsharp-daemon

Configuration lives in /{ConfigFile}.
""";
}
