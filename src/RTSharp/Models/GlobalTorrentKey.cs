using System;

namespace RTSharp.Models;

public readonly record struct GlobalTorrentKey(byte[] InfoHash, Guid DataProviderInstanceId);
