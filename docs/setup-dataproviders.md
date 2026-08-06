# Setting up data providers
Initial setup for data provider (torrent daemon) consists of 4 parts: configuring `RTSharp.Daemon` on client & server side and configuring a data provider on client & server side.

# Servers
Each data provider must be associated with a server. Built-in torrent daemon data is processed through `RTSharp.Daemon` to workaround various torrent daemon inefficiencies and supplement additional features.

## Client side

First, you will have to generate a public & private certificates. If you want to avoid generating PEM certificates yourself, RT# client will prompt you with an option to automatically generate new certificates.

On Windows, application configuration files will be stored in `[application base directory]/user` folder. On Linux, application configuration files will be stored in `$XDG_CONFIG_HOME/rtsharp`


If you have bash on Windows, or if you are running the client on Linux, note down the thumbprint of this certificate:

```
openssl x509 -in client.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2
```
Otherwise, run the following in a powershell window:
```
[System.BitConverter]::ToString(([System.Security.Cryptography.SHA256]::Create()).ComputeHash((New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @("client.pem")).RawData)).Replace("-", "")
```

## Server side

There are multiple options to run `RTSharp.Daemon`:
 * Systemd service - Packaged service, either regular or self-contained version. To run regular version, you will have to [install dotnet SDK & runtime](https://learn.microsoft.com/en-us/dotnet/core/install/linux). Configuration is read from /etc/rtsharp-daemon if it exists, or from application base path.
 * Docker container - See [docker-compose.yml](src/RTSharp.Daemon/docker-compose.yml) for an example configuration. An example configuration will be created upon startup.

---

1. Generate a public & private service certificates in `/etc/rtsharp-daemon`:
    ```
    openssl req -x509 -newkey rsa:4096 -nodes -keyout private.pem -out public.pem -days 36500
    ```

2. Get certificates trusted thumbprint and note it down.
    ```
    openssl x509 -in public.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2
    ```

3. Modify example configuration at `/etc/rtsharp-daemon/appsettings.json`:
   1. Modify `ListenAddress` to listen on `DaemonPort` of your choosing
   2. Add clients certificate thumbprint you've noted down before to `AllowedClients`.

4. Explore other settings like `Prometheus`. Most of them are optional, like `DLBandwidth` and `UPBandwidth` which enables some visualizations & statistics.
   
5. Restart the daemon

> [!CAUTION]
> Granting access to `RTSharp.Daemon` is equivalent to granting shell access. Daemon implements a runtime scripting interface that can be used to perform any action. Add clients to `AllowedClients` with caution.

Optionally, enable `Prometheus` in `appsettings.json` to expose tracker upload/download totals at the configured `Path` (default `/metrics`). `PollInterval` controls how often the daemon refreshes metrics.

Note that `RTSharp.Daemon` is able to perform file operations related to torrent data, so it's a should be run under the same user as your torrent daemons.

## Client side again

You will need to fill in details about the server you've just configured. In Servers menu, you can add a new configuration:

* Server ID - Unique server name
* Host & Port - `RTSharp.Daemon` endpoint
* Trusted certificate thumbprint - Server's certificate thumbprint you've noted down before

# Data providers

## Server side

Torrent daemon types are configured in `DataProviders` section of `appsettings.json`. An example configuration is provided. There are currently 3 supported daemons, so there must be 3 key-values in `DataProviders` at most. Each daemon has key-values for each instance of daemon where key is their name (corresponding "Data provider name" in the client).

Example:
```
"DataProviders": {
  "rtorrent": {
    "my rtorrent": {
      "SCGIListen": "127.0.0.1:5000",
      "SessionDir": "/home/user/.session",
      "LogFile": "/home/user/.session/rtorrent.log" // optional
    }
  },
  "qbittorrent": {
    "my qbittorrent": {
      "Uri": "http://127.0.0.1:1234/",
      "ApiKey": "api-key" // api key, or...
      "Username": "admin", // ...username+password
      "Password": "changeme",
    }
  },
  "transmission": {
    "my transmission": {
      "Uri": "http://127.0.0.1:1234/transmission/rpc",
      "Username": "transmission",
      "Password": "changeme",
      "ConfigDir": "/home/user/.config/transmission-daemon/",
      "LogFile": "/home/user/.config/transmission-daemon/transmission.log" // optional
    }
  }
}
```

Restart the daemon after config changes.

---

If you wish to connect multiple servers to each other for torrent data transfers, you can perform the same client & server setup but between two servers, adding:
 - First servers (acting as client) thumbprint to second server
 - Second servers (acting as client) thumbprint to first server

Make sure that firewall allows direct communication between the servers on `RTSharp.Daemon` ports.

## Client side

Each data provider is a plugin. Plugins are enabled their configuration files, so you can have multiple instances of same data provider running for different remote daemons.

To configure a new data provider instance, head on over to Plugins -> Manage... . Here, you can choose from available plugins and configure a new data provider corresponding with torrent daemon you run:

* Instance name - Unique name for this data provider instance, as will be displayed in the GUI
* Server - Previously configured server
* Data provider name - Torrent daemon configuration key as specified in `RTSharp.Daemon` `appsettings.json`, for example "my qbittorrent"

After RT# restart & with data providers configured, you should see data provider active in data providers pane, and torrents appearing in the main list if there are any.

### rtorrent

rtorrent implementation uses `xmlrpc` interface. It does not support delta torrent listing changes, therefore requires significant bandwidth throughput to sustain fast updates for large lists of torrents.

* `SCGIListen` is an IP end point pointing to SCGI socket configured by rtorrents `scgi_port` option
* `SessionDir` is an optional field to override the session directory returned by rtorrent. This is useful in scenarios when rtorrent is hosted in a filesystem not visible to RT#. This impacts the ability to get .torrent files of already added torrents.
* `LogFile` is an optional field for streaming logs to the client. If you have rtorrent logging enabled, set this field to value of `log.open_file` option.

Example:

```
{
  "SCGIListen": "127.0.0.1:1234",
  "LogFile": "/var/log/rtorrent/rtorrent.log"
}
```

### qbittorrent

qbittorrent implementation connects to its RPC server using a username and password or API key via HTTP. API key is superior to username+password.

* `Uri` field must point to qbittorrent RPC server, for example `http://10.0.0.1:40000/`
* `ApiKey` field should be filled with API key generated from qBittorrent WebUI
* `Username` field is used together with password if `ApiKey` is not used
* `Password` field is used together with username

Example:
```
{
  "Uri": "http://127.0.0.1:4321/",
  "Username": "user",
  "Password": "password"
}
```

### transmission

transmission implementation connects to its RPC server using a username and password via HTTP.

* `Uri` field must point to transmission RPC path, for example `http://10.0.0.1:40000/transmission/rpc`
* `Username` field must be filled
* `Password` field must be filled
* `ConfigDir` is an optional field to override config directory returned by transmission. This is useful in scenarios when transmission is hosted in a filesystem not visible to RT#. This impacts the ability to get .torrent files of already added torrents.
* `LogFile` is an optional field for streaming logs to the client. If you have transmission logging enabled, set this field to value transmission's log file path.

```
{
  "Uri": "http://10.0.0.1:40000/transmission/rpc",
  "Username": "transmission",
  "Password": "password",
  "ConfigDir": "/home/user/.config/transmission-daemon/config/",
  "LogFile": "/home/user/.config/transmission-daemon/transmission.log"
}
```