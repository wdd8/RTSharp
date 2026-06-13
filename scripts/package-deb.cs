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

var (pkgName, srcDir, installDir, execName, binName, depends, recommends, description, conflicts) = type switch {
    "client" => (
        "rtsharp",
        rid,
        "usr/lib/rtsharp",
        "RTSharp",
        "rtsharp",
        "",
        "mpv",
        "RTSharp client GUI",
        "rtsharp-self-contained"
    ),
    "client-self-contained" => (
        "rtsharp-self-contained",
        $"{rid}-self-contained",
        "usr/lib/rtsharp",
        "RTSharp",
        "rtsharp",
        "",
        "mpv",
        "RTSharp client GUI (self-contained)",
        "rtsharp"
    ),
    "daemon" => (
        "rtsharp-daemon",
        $"{rid}-daemon",
        "usr/lib/rtsharp-daemon",
        "RTSharp.Daemon",
        "rtsharp-daemon",
        "",
        "",
        "RTSharp daemon service",
        "rtsharp-daemon-self-contained"
    ),
    "daemon-self-contained" => (
        "rtsharp-daemon-self-contained",
        $"{rid}-daemon-self-contained",
        "usr/lib/rtsharp-daemon",
        "RTSharp.Daemon",
        "rtsharp-daemon",
        "",
        "",
        "RTSharp daemon service (self-contained)",
        "rtsharp-daemon"
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
if (!String.IsNullOrEmpty(depends))
    control.AppendLine($"Depends: {depends}");
if (!String.IsNullOrEmpty(recommends))
    control.AppendLine($"Recommends: {recommends}");
if (!String.IsNullOrEmpty(conflicts)) {
    control.AppendLine($"Conflicts: {conflicts}");
    control.AppendLine($"Replaces: {conflicts}");
}
control.AppendLine("Section: utils");
control.AppendLine("Priority: optional");
control.AppendLine($"Description: {description}");

File.WriteAllText(Path.Combine(pkgDir, "DEBIAN", "control"), control.ToString());

if (type == "daemon" || type == "daemon-self-contained") {
    var execMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    var systemdDir = Path.Combine(pkgDir, "lib", "systemd", "system");
    Directory.CreateDirectory(systemdDir);
    File.WriteAllText(Path.Combine(systemdDir, "rtsharp-daemon.service"),
"""
[Unit]
Description=RTSharp Daemon
After=network.target

[Service]
Type=simple
ExecStart=/usr/lib/rtsharp-daemon/RTSharp.Daemon
Restart=on-failure
RestartSec=5
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

    var postinst = Path.Combine(pkgDir, "DEBIAN", "postinst");
    File.WriteAllText(postinst, "#!/bin/bash\nset -e\nsystemctl daemon-reload || true\n");
    File.SetUnixFileMode(postinst, execMode);

    var prerm = Path.Combine(pkgDir, "DEBIAN", "prerm");
    File.WriteAllText(prerm,
"""
#!/bin/bash
set -e
if systemctl is-active --quiet rtsharp-daemon 2>/dev/null; then
    systemctl stop rtsharp-daemon || true
fi
if systemctl is-enabled --quiet rtsharp-daemon 2>/dev/null; then
    systemctl disable rtsharp-daemon || true
fi
""");
    File.SetUnixFileMode(prerm, execMode);
}

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
