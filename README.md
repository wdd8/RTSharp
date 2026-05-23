
## RTSharp

RTSharp is a cross-platform bittorrent daemon GUI written in C# using AvaloniaUI framework. RTSharp supports multiple types of daemons with some additional features when it is not supported by the daemon itself.

### Requirements

Client application requirements are the same as for [AvaloniaUI](https://docs.avaloniaui.net/docs/faq#what-platforms-are-supported), however mobile, embedded systems and wasm are not supported.
Daemons and their helper services must be run in a linux environment.

### Security

> [!CAUTION]
> Granting access to `RTSharp.Daemon` is equivalent to granting shell access. Daemon implements a runtime scripting interface that can be used to perform any action. Add clients to `AllowedClients` with caution.

### Getting started

See [setting up data providers](setup-dataproviders.md)

### Features

 - Multiple servers/torrent daemons in the same GUI
 - RTSharp.Daemon
   - Remote folder selection
   - Torrent data copy to other servers
   - Mediainfo
   - Runtime scripting
 - Standalone built-in torrent creation
 - Peer AS listing
 - Custom names for trackers
 - Virtualized main torrent list with search
 - Plugins for custom torrent daemon support & extensibility

### Supported daemons

||rtorrent|qbittorrent|transmission|
|-|-|-|-|
|Torrent Listing|🟡Delta changes|🟢Delta changes|🟢Delta changes|
|File listing|🟢Supported|🟢Supported|🟢Supported|
|Peer listing|🟢Supported|🟢Supported|🟡No downloaded/uploaded column|
|Tracker listing|🟢Supported|🟡Only URI, seeders, peers|🟢Supported|
|Start torrent|🟢Supported|🟢Supported|🟢Supported|
|Pause torrent|🟢Supported|🟢Supported|🔴Unsupported by daemon|
|Stop torrent|🟢Supported|🔴Unsupported by daemon|🟢Supported|
|Force recheck (rehash)|🟢Supported|🟢Supported|🟢Supported|
|Reannounce to all trackers|🟢Supported|🟢Supported|🟢Supported|
|Add torrent|🟢Supported|🟢Supported|🟢Supported|
|Add torrent paused|🟢Supported|🟢Supported|🟢Supported|
|Move download directory|🟢With data move & checks|🟢Supported|🟢Supported|
|Remove torrent|🟢Supported|🟢Supported|🟢Supported|
|Remove torrent & data|🟢Supported|🟢Supported|🟢Supported|
|Add label|🟢Supported|🟢Supported|🟢Supported|
|Set labels|🟢Supported|🟢Supported|🟢Supported|
|Add peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Ban peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Kick peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Snub peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Unsnub peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Get .torrent|🟢Supported|🟢Supported|🟡With daemon access to config dir|
|Prefill default save path|🟢Supported|🟢Supported|🟢Supported|
|Pieces progress bar|🟢Supported|🟢Supported|🟢Supported|
|Extended statistics|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Partial downloads|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Add new tracker|🔴Not implemented|🟢Supported|🟢Supported|
|Enable tracker|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Disable tracker|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Remove tracker|🔴Not implemented|🟢Supported|🟢Supported|
|Reannounce tracker|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Replace tracker|🟡Hacky workaround*|🟢Supported|🟢Supported|
|Global download statistics|🟡Workaround*|🟢Supported|🟢Supported|
|Global upload statistics|🟡Workaround*|🟢Supported|🟢Supported|
|Global ratio statistics|🟡Workaround*|🟢Supported|🟢Supported|
|Sequential downloads|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|File renaming|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Torrent renaming|🔴Not implemented|🔴Not implemented|🔴Not implemented|

_* - not directly supported by torrent daemon_

### Other features

|Feature|Support|
|-|-|
|V2/Hybrid torrent creation|🟢Supported|
|File previews|🔴Not implemented|
|File icons (as determined by OS)|🔴Not implemented|
|Native AOT|🔴https://github.com/DapperLib/DapperAOT/issues/160|

### Configuration

Currently configuration can be edited only in `config.json`. First setup emits a config with all possible entries.