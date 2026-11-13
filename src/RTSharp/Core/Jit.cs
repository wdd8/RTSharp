using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RTSharp.Core;

public static class Jit
{
    /// <summary>
    /// Compiles the methods of the types on a thread pool thread, rather than each on the thread that first calls it.
    /// For code that has no precompiled image: assemblies loaded from memory, or into a collectible context
    /// </summary>
    public static void CompileInBackground(IReadOnlyCollection<Type> Types)
    {
        _ = Task.Run(() => {
            const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            foreach (var type in Types.Where(x => !x.ContainsGenericParameters)) {
                foreach (var method in type.GetMethods(declared).Concat<MethodBase>(type.GetConstructors(declared)).Where(x => !x.IsAbstract && !x.ContainsGenericParameters)) {
                    try {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    } catch {
                        // Compiled when it is called, if it ever is
                    }
                }
            }
        });
    }
}
