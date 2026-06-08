using System.Diagnostics;
using System.Text;

var rid = args[0];      // linux-x64 or linux-arm64
var version = args[1];  // e.g. 0.0.1
var type = args[2];     // client or daemon

var arch = rid switch {
    "linux-x64"   => "amd64",
    "linux-arm64" => "arm64",
    _ => throw new ArgumentException($"Unsupported RID: {rid}")
};

var (pkgName, srcDir, installDir, execName, binName, depends, recommends, description) = type switch {
    "client" => (
        "rtsharp",
        rid,
        "usr/lib/rtsharp",
        "RTSharp",
        "rtsharp",
        "dotnet-apphost-pack-10.0",
        "mpv",
        "RTSharp client GUI"
    ),
    "daemon" => (
        "rtsharp-daemon",
        $"{rid}-daemon",
        "usr/lib/rtsharp-daemon",
        "RTSharp.Daemon",
        "rtsharp-daemon",
        "dotnet-apphost-pack-10.0",
        "",
        "RTSharp daemon service"
    ),
    _ => throw new ArgumentException($"Unknown type: {type}")
};

var pkgDir = $"{pkgName}_{version}_{arch}";
Console.WriteLine($"Packaging {pkgDir}.deb...");

Directory.CreateDirectory(Path.Combine(pkgDir, "DEBIAN"));
Directory.CreateDirectory(Path.Combine(pkgDir, installDir));
Directory.CreateDirectory(Path.Combine(pkgDir, "usr", "bin"));

CopyDirectory(srcDir, Path.Combine(pkgDir, installDir));

var execPath = Path.Combine(pkgDir, installDir, execName);
File.SetUnixFileMode(execPath,
    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

File.CreateSymbolicLink(Path.Combine(pkgDir, "usr", "bin", binName), $"/{installDir}/{execName}");

var size = Directory
    .EnumerateFiles(Path.Combine(pkgDir, installDir), "*", SearchOption.AllDirectories)
    .Sum(f => new FileInfo(f).Length) / 1024;

var control = new StringBuilder();
control.AppendLine($"Package: {pkgName}");
control.AppendLine($"Version: {version}");
control.AppendLine($"Architecture: {arch}");
control.AppendLine("Maintainer: wdd8 <wdd@riseup.net>");
control.AppendLine($"Installed-Size: {size}");
control.AppendLine($"Depends: {depends}");
if (!String.IsNullOrEmpty(recommends))
    control.AppendLine($"Recommends: {recommends}");
control.AppendLine("Section: utils");
control.AppendLine("Priority: optional");
control.AppendLine($"Description: {description}");

File.WriteAllText(Path.Combine(pkgDir, "DEBIAN", "control"), control.ToString());

Run("dpkg-deb", ["--build", "--root-owner-group", pkgDir]);

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
