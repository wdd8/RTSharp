using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Auxiliary
{
    public record FileTransferToken(string Token, DateTime Expiry);
}
