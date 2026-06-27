#!/bin/sh
set -e

CONFIG_DIR=/etc/rtsharp

if [ ! -f "$CONFIG_DIR/appsettings.json" ]; then
    mv /app/appsettings.example.json "$CONFIG_DIR/appsettings.json"
fi

exec dotnet RTSharp.Daemon.dll "$@"
