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
rm -r "$outDir/clidriver" -ErrorAction SilentlyContinue
rm "$outDir/TestConn.*" -ErrorAction SilentlyContinue
rm "$outDir/*.xml" -ErrorAction SilentlyContinue
rm "$outDir/Azure.*.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Extensions.MySql.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Extensions.Oracle.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Extensions.Postgres.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Extensions.Snowflake.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Extensions.SqlServer.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Db2.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Firebird.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Hana.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.MySql.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Oracle.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Postgres.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Redshift.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.Snowflake.dll" -ErrorAction SilentlyContinue
rm "$outDir/FluentMigrator.Runner.SqlServer.dll" -ErrorAction SilentlyContinue
rm "$outDir/FirebirdSql.Data.FirebirdClient.dll" -ErrorAction SilentlyContinue
rm "$outDir/IBM.Data.Db2.dll" -ErrorAction SilentlyContinue
rm "$outDir/Microsoft.Data.SqlClient.dll" -ErrorAction SilentlyContinue
#rm "$outDir/Microsoft.Data.SqlClient.SNI.dll"
rm "$outDir/Microsoft.IdentityModel.*.dll" -ErrorAction SilentlyContinue
rm "$outDir/Microsoft.Identity.*.dll" -ErrorAction SilentlyContinue
rm "$outDir/Microsoft.SqlServer.Server.dll" -ErrorAction SilentlyContinue
rm "$outDir/Microsoft.Bcl.AsyncInterfaces.dll" -ErrorAction SilentlyContinue
rm "$outDir/Microsoft.Bcl.Cryptography.dll" -ErrorAction SilentlyContinue
rm "$outDir/System.ClientModel.dll" -ErrorAction SilentlyContinue
rm "$outDir/System.Configuration.ConfigurationManager.dll" -ErrorAction SilentlyContinue
#rm "$outDir/System.Diagnostics.EventLog.dll"
rm "$outDir/System.IdentityModel.Tokens.Jwt.dll" -ErrorAction SilentlyContinue
rm "$outDir/System.Memory.Data.dll" -ErrorAction SilentlyContinue
#rm "$outDir/System.Security.Cryptography.Pkcs.dll"
rm "$outDir/System.Security.Cryptography.ProtectedData.dll" -ErrorAction SilentlyContinue

rm "$outDir/plugins/*/*.xml" -ErrorAction SilentlyContinue
rm "$outDir/plugins/*/*.runtimeconfig.json" -ErrorAction SilentlyContinue
