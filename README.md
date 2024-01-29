## RTSharp

RTSharp is a cross-platform bittorrent daemon GUI written in C# using AvaloniaUI framework. RTSharp supports multiple types of daemons with some additional features when it is not supported by the daemon itself.

### Requirements

Client application requirements are the same as for [AvaloniaUI](https://docs.avaloniaui.net/docs/faq#what-platforms-are-supported), however mobile, embedded systems and wasm are not supported.
Daemons and their helper services must be run in a linux environment.

### Getting started

TODO servers config, data provider config...

### Features

 - Multiple servers/torrent daemons in the same GUI
 - RTSharp.Auxiliary
   - Remote folder selection
   - Torrent data copy to other servers
 - Standalone built-in torrent creation
 - Peer AS listing
 - Custom names for trackers
 - Virtualized main torrent list with search
 - Plugins for custom torrent daemon support & extensibility

### Supported daemons

||rtorrent|qbittorrent|transmission|
|-|-|-|-|
|Torrent Listing|🟡Delta changes using `Rtorrent.Server`|🟢Delta changes|🟢Delta changes|
|File listing|🟡Supported, broken caching|🟡Supported, broken caching|🟡Supported, broken caching|
|Peer listing|🟢Supported|🟢Supported|🟡No downloaded/uploaded column|
|Tracker listing|🟢Supported|🟡Only URI, seeders, peers|🟢Supported|
|Start torrent|🟢Supported|🟢Supported|🟢Supported|
|Pause torrent|🟢Supported|🟢Supported|🔴Unsupported by daemon|
|Stop torrent|🟢Supported|🔴Unsupported by daemon|🟢Supported|
|Force recheck (rehash)|🟢Supported|🟢Supported|🟢Supported|
|Reannounce to all trackers|🟢Supported|🟢Supported|🟢Supported|
|Add torrent|🟢Supported|🟢Supported|🟡Untested|
|Add torrent paused|🟢Supported|🟢Supported|🟡Untested|
|Move download directory|🟢With data move|🟡Supported|🔴Not implemented|
|Remove torrent|🟢Supported|🟢Supported|🟢Supported|
|Remove torrent & data|🟢Supported|🟢Supported|🟢Supported|
|Add label|🟡Buggy|🟡Buggy|🟡Buggy|
|Set labels|🟡Buggy|🟡Buggy|🟡Buggy|
|Add peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Ban peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Kick peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Snub peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Unsnub peer|🔴Not implemented|🔴Not implemented|🔴Not implemented|
|Get .torrent|🟢Supported|🔴Supported, not enabled|🔴Unsupported by daemon|
|Prefill default save path|🟢Supported|🟢Supported|🟢Supported|
