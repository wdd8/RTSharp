param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Rid,
    [switch]$SelfContained
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$pwd = Get-Location

$scFlag = if ($SelfContained) { "--self-contained" } else { "--no-self-contained" }
$outDir = if ($SelfContained) { "$Rid-self-contained" } else { $Rid }

dotnet publish ./src/RTSharp -c Release -r $Rid $scFlag -o $outDir
dotnet publish ./src/plugins/RTSharp.AutoIntegrify.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/AutoIntegrify')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet publish ./src/plugins/RTSharp.MassTrackerRewrite.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/MassTrackerRewrite')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet publish ./src/plugins/RTSharp.ColoredRatio.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/ColoredRatio')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet publish ./src/rtorrent/RTSharp.DataProvider.Rtorrent.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/DataProvider.Rtorrent')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet publish ./src/qbittorrent/RTSharp.DataProvider.Qbittorrent.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/DataProvider.Qbittorrent')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet publish ./src/transmission/RTSharp.DataProvider.Transmission.Plugin -c Release -r $Rid --no-self-contained -o "$(Join-Path $pwd $outDir 'plugins/DataProvider.Transmission')" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"

rm "$outDir/*.xml" -ErrorAction SilentlyContinue

rm "$outDir/plugins/*/*.xml" -ErrorAction SilentlyContinue
rm "$outDir/plugins/*/*.runtimeconfig.json" -ErrorAction SilentlyContinue
