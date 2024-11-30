using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Abstractions
{
    public interface IScriptSession
    {
        public void ProgressChanged();

        public ScriptProgressState Progress { get; }
    }
}
