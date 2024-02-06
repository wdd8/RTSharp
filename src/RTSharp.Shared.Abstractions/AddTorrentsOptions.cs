using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Abstractions
{

    public record AddTorrentsOptions(
        object? DataProviderOptions,
        string RemoteTargetPath
    );
}
