# Setting up data providers
Initial setup for data provider (torrent daemon) mainly consists of 4 parts: configuring RTSharp.Auxiliary on client & server side and configuring a data provider on client & server side.

## Servers
Each data provider must be associated with its server definition. This is to provide functionality that is applicable to the server itself, like browsing directories or transferring files between drives.

### Client side

First, you will have to generate a public & private certificates, this is done automatically upon confirmation starting RT#. This is the only part that is currently automated, so unless you want to generate PEM certificates yourself, you can open RT# and close it after its done generating.

`config.json` should already have an example server configuration:

1. Key of the server is its name, which will be later referenced in configuration of data providers
2. `Host` is a domain or an IP address
3. `AuxiliaryServicePort` is a port of `RTSharp.Auxiliary` service which will be set-up later
4. `TrustedThumbprint` is a thumbprint of `RTSharp.Auxiliary` service certificate which will be set-up later
5. `VerifyNative` can be used if you have a possibility to get a certificate which is trusted by the system natively, such as letsencrypt certificate

### Server side

Clone the repository to a directory of your choosing and generate a public & private service certificates inside `RTSharp/src/RTSharp.Auxiliary`:

    openssl req -x509 -newkey rsa:4096 -nodes -keyout private.pem -out public.pem -days 36500

At this point, you can go back and fill in `TrustedThumbprint` for client side:

    openssl x509 -in public.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2

`appsettings.json` should again have a template you can fill in:

1. Modify `ListenAddress` to listen on `AuxiliaryServicePort` of your choosing
2. Add clients certificate thumbprint to `AllowedClients`:
3. 1. Run `openssl x509 -in client.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2` targeting RT# client certificate.

You can run the service by building it `dotnet build -c Release` and running the executable `./bin/Release/net8.0/RTSharp.Auxiliary`, or just by running `dotnet run -c Release`. Note that `RTSharp.Auxiliary` is able to perform file operations related to torrent data, so it's a good idea to run it under the same user as your torrent daemons.

---
If you wish to connect multiple servers to enable torrent data transfers, you can perform the same client & server setup another server, but this time additionally adding:
 - First servers (acting as client) thumbprints to second server
 - Second servers (acting as client) thumbprint to first server

Make sure that firewall allows direct communication between the servers on `RTSharp.Auxiliary` ports.

## Data providers

Each data provider is a plugin. Plugins are enabled by a configuration file in `plugins` folder, so you can have multiple instances of same data provider running for different remote daemons.
Configuration of data providers is not fully automated, but you can head over to Plugins -> Manage... and load your data provider to generate a config. It will complain about missing configuration fields, you can ignore this and once again close RT#, we will fix this later. You can rename the generated data provider configuration file to your preference, for example to `DataProvider.Rtorrent-myserver.json`.

Following must be configured for each data provider:

1. Rename the data provider by modifying `Name` field. This field does not have any significance and can be set to anything
2. Add a `ServerId` field to `Plugin` object. This must correspond to configured server in `config.json`.
3. (optional) You can set a `Color` field in `Plugin` object to render torrent rows in a different background color.

### rtorrent

#### Client side

rtorrent data provider connects to `RTSharp.DataProvider.Rtorrent.Server` to avoid communicating through rtorrents native `xmlrpc` --- it does not support delta torrent listing changes, therefore requires significant bandwidth throughput to sustain fast updates for large lists of torrents.

1. Fill server information in a new `Server` object in root of the configuration:
2. 1. `Uri` field must point to `RTSharp.DataProvider.Rtorrent.Server` service including a port and `https` protocol, for example `https://10.0.0.1:40000/`
    2. `PollInterval` will instruct `RTSharp.DataProvider.Rtorrent.Server` to send torrent listing updates in specified interval, for example `00:00:01`

#### Server side

Generate a public & private service certificates inside `RTSharp/src/rtorrent/RTSharp.DataProvider.Rtorrent.Server`:

    openssl req -x509 -newkey rsa:4096 -nodes -keyout private.pem -out public.pem -days 36500

1. Modify `ListenAddress` to listen on port of your choosing
2. Modify `SCGIListen` to configured rtorrent SCGI listen endpoint, for example `/home/user/.rtorrent/rtorrent.sock` or `127.0.0.1:5000`. Path must be absolute for unix socket.
3. Add clients certificate thumbprint to `AllowedClients`:
4. 1. Run `openssl x509 -in client.pem -noout -fingerprint -sha256 | sed s/://g | cut -d= -f2` targeting RT# client certificate.

You can run the service by building it `dotnet build -c Release` and running the executable `./bin/Release/net8.0/RTSharp.DataProvider.Rtorrent.Server`, or just by running `dotnet run -c Release`. Note that `RTSharp.DataProvider.Rtorrent.Server` is able to perform file operations related to torrent data, so it's a good idea to run it under the same user as your torrent daemons.

### qbittorrent

qbittorrent data provider connects directly to its RPC server using a username and password.

1. Fill server information in a new `Server` object in root of the configuration:
2. 1. `Uri` field must point to qbittorrent RPC server, for example `http://10.0.0.1:40000/`
    2. `Username` field must be filled
    3. `Password` field must be filled
    4. `PollInterval` will check for torrent listing updates in specified interval, for example `00:00:01`

### transmission

transmission data provider connects directly to its RPC server using a username and password.

1. Fill server information in a new `Server` object in root of the configuration:
2. 1. `Uri` field must point to qbittorrent RPC server, for example `http://10.0.0.1:40000/transmission/rpc`
    2. `Username` field must be filled
    3. `Password` field must be filled
    4. `PollInterval` will check for torrent listing updates in specified interval, for example `00:00:01`
