$pwd = Get-Location
dotnet publish ./src/RTSharp -c Release -r $args[0] --no-self-contained -o $args[0]
dotnet build ./src/plugins/RTSharp.ColoredRatio -c Release -r $args[0] --no-self-contained /p:OutputPath="$(Join-Path $pwd $args[0] 'plugins/ColoredRatio')/" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet build ./src/rtorrent/RTSharp.DataProvider.Rtorrent.Plugin -c Release -r $args[0] --no-self-contained /p:OutputPath="$(Join-Path $pwd $args[0] 'plugins/DataProvider.Rtorrent')/" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet build ./src/qbittorrent/RTSharp.DataProvider.Qbittorrent.Plugin -c Release -r $args[0] --no-self-contained /p:OutputPath="$(Join-Path $pwd $args[0] 'plugins/DataProvider.Qbittorrent')/" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"
dotnet build ./src/transmission/RTSharp.DataProvider.Transmission.Plugin -c Release -r $args[0] --no-self-contained /p:OutputPath="$(Join-Path $pwd $args[0] 'plugins/DataProvider.Transmission')/" /p:ScriptsDir="$(Join-Path $pwd 'scripts')/"