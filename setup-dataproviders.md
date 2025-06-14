# Setting up data providers
Initial setup for data provider (torrent daemon) consists of 4 parts: configuring RTSharp.Daemon on client & server side and configuring a data provider on client & server side.

# Servers
Each data provider must be associated with its server definition. Built-in torrent daemon data is all routed through `RTSharp.Daemon` to workaround various torrent daemon inefficiencies and supplement additional features.

## Client side

First, you will have to generate a public & private certificates. If you want to avoid generating PEM certificates yourself, you can open RT# and close it to automatically generate new certificates.

Next, you will need to add configuration of `RTSharp.Daemon` instances. `config.json` should already have an example server configuration:

* Key of the server is its name, which will be later referenced in configuration of data providers
* `Host` is a domain or an IP address
* `DaemonPort` is a port of `RTSharp.Daemon` service which will be set-up later
* `TrustedThumbprint` is a thumbprint of `RTSharp.Daemon` service certificate which will be set-up later
* `VerifyNative` can be used if you have a possibility to get a certificate which is trusted by the system natively, such as letsencrypt certificate

Example:

```
<...>
  "Servers": {
    "server1": {
      "Host": "myserver1.local",
      "DaemonPort": 1234,
      "TrustedThumbprint": "8645416854AC681681F6854l8534185E68541A54",
      "VerifyNative": false
    },
    "server2": {
      "Host": "myserver2.local",
      "DaemonPort": 4321,
      "TrustedThumbprint": "486548641AC683541685l4685F68541685E6854C",
      "VerifyNative": false
    }
  }
<...>
```

## Server side

As `RTSharp.Daemon` is a .NET application, you will have to [install dotnet SDK & runtime](https://learn.microsoft.com/en-us/dotnet/core/install/linux).

1. Clone the repository to a directory of your choosing and generate a public & private service certificates inside `RTSharp/src/RTSharp.Daemon`:
    ```
    openssl req -x509 -newkey rsa:4096 -nodes -keyout private.pem -out public.pem -days 36500
    ```

2. At this point, you can go back and fill in `TrustedThumbprint` for client side.
    ```
    openssl x509 -in public.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2
    ```

3. `appsettings.json` should have a template you can fill in:
   1. Modify `ListenAddress` to listen on `DaemonPort` of your choosing
   2. Add clients certificate thumbprint to `AllowedClients`. You can retrive the thumbprint using openssl and targeting RT# client certificate.
      
       ```
       openssl x509 -in client.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2
       ```

    > [!CAUTION]
    > Granting access to `RTSharp.Daemon` is equivalent to granting shell access. Daemon implements a runtime scripting interface that can be used to perform any action. Add clients to `AllowedClients` with caution.

You can run the service by building it `dotnet build -c Release` and running the executable `./bin/Release/net9.0/RTSharp.Daemon`, or just by running `dotnet run -c Release`.

Note that `RTSharp.Daemon` is able to perform file operations related to torrent data, so it's a good idea to run it under the same user as your torrent daemons. If you are not running daemon under docker, systemd or any other orchestrator with an ability to set process user, you can set `setuid` and `setgid` config properties at root level to your users and group ID.

---

If you wish to connect multiple servers to each other for torrent data transfers, you can perform the same client & server setup another server, but this time additionally adding:
 - First servers (acting as client) thumbprints to second server
 - Second servers (acting as client) thumbprint to first server

Make sure that firewall allows direct communication between the servers on `RTSharp.Daemon` ports.

# Data providers

## Client side

Each data provider is a plugin. Plugins are enabled by a configuration file in `plugins` folder, so you can have multiple instances of same data provider running for different remote daemons.
Configuration of data providers is not fully automated, but you can head over to Plugins -> Manage... and load your data provider to generate a plugin config. You can rename the generated data provider configuration file to your preference, for example to `DataProvider.Rtorrent-myserver.json`.

Following must be configured for each data provider in a new `DataProvider` object:

1. `ServerId` which points to a global server ID. This must correspond to configured server in `config.json`.
2. `Name` field, which is a name for data provider, corresponding to name configured on `RTSharp.Daemon`, which we will set-up later.
3. `ListUpdateInterval`, which supplies a torrent listing update interval to `RTSharp.Daemon`.

Optionally, you can set a `Color` field in `Plugin` object to for example `#ACACAC` to render torrent rows with a gray background color.

For example:
```
{
  "Plugin": {
    "Path": "DataProvider.Rtorrent",
    "Name": "rtorrent on server1",
    "Color": "#ACACAC",
    "InstanceId": "b95dd238-f4c1-44b6-b79c-ce92a6ac4c35",
  },
  "DataProvider": {
    "ServerId": "server1",
    "Name": "my rtorrent",
    "ListUpdateInterval": "00:00:01"
  }
}
```

## Server side

Torrent daemon types are configured in `DataProviders` section of `appsettings.json`. An example configuration is provided. There are currently 3 supported daemons, so there must be 3 key-values at most. Each daemon has key-values for each instance of daemon where key is their name (corresponding `DataProvider.Name` in client).

Example:
```
"DataProviders": {
  "rtorrent": {
    "my rtorrent": {
      "SCGIListen": "127.0.0.1:5000"
    }
  },
  "qbittorrent": {
    "my qbittorrent": {
      "Uri": "http://127.0.0.1:1234/",
      "Username": "admin",
      "Password": "chageme"
    }
  },
  "transmission": {
    "my transmission": {
      "Uri": "http://127.0.0.1:1234/transmission/rpc",
      "Username": "transmission",
      "Password": "changeme",
      "ConfigDir": "/home/user/.config/transmission-daemon/"
    }
  }
}
```

### rtorrent

rtorrent implementation uses `xmlrpc` interface. It does not support delta torrent listing changes, therefore requires significant bandwidth throughput to sustain fast updates for large lists of torrents.

* `SCGIListen` is an IP end point pointing to SCGI socket configured by rtorrents `scgi_port` option

Example:

```
{
  "SCGIListen": "127.0.0.1:1234"
}
```

### qbittorrent

qbittorrent implementation connects to its RPC server using a username and password via HTTP.

* `Uri` field must point to qbittorrent RPC server, for example `http://10.0.0.1:40000/`
* `Username` field must be filled
* `Password` field must be filled

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
* `ConfigDir` should be filled and accessible if possible. This impacts ability to get .torrent files of already added torrents.

```
{
  "Uri": "http://10.0.0.1:40000/transmission/rpc",
  "Username": "transmission",
  "Password": "password",
  "ConfigDir": "/docker_mount/transmission-1/config/"
}
```