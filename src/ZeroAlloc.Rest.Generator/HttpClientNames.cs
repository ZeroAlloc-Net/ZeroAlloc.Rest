using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

// What a client takes part in named-HttpClient collision detection with: its hint name stem, which
// identifies it, its default name, its namespace-qualified name, and whether a client is generated.
internal readonly record struct HttpClientNameKey(string HintNameStem, string DefaultName, string QualifiedName, bool IsSupported);

// Decides which clients get a namespace-qualified named HttpClient (#395). Add{I} names the client
// after the interface's name within its namespace, IUserApi, so two interfaces with that name in
// different namespaces would share one HttpClient, and the last BaseAddress and handlers would win
// for both. Only such clients are qualified, MyApp.IUserApi; every other client keeps its name.
internal static class HttpClientNames
{
    // The hint name stems of the clients that take their qualified name, in ordinal order. A
    // qualified name can match another client's default name, as MyApp.IUserApi matches the
    // default name of an IUserApi nested in a type MyApp, so that client is qualified too, until no
    // two clients share a name. Two qualified names never match: they are full type names, and C#
    // does not allow a namespace and a type with the same full name in one compilation.
    internal static EquatableArray<string> Qualified(ImmutableArray<HttpClientNameKey> keys)
    {
        var qualified = new HashSet<string>(System.StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            var byName = new Dictionary<string, List<HttpClientNameKey>>(System.StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!key.IsSupported)
                    continue;
                var name = qualified.Contains(key.HintNameStem) ? key.QualifiedName : key.DefaultName;
                if (!byName.TryGetValue(name, out var group))
                    byName[name] = group = new List<HttpClientNameKey>();
                group.Add(key);
            }

            foreach (var group in byName.Values)
            {
                if (group.Count < 2)
                    continue;
                foreach (var key in group)
                    changed |= qualified.Add(key.HintNameStem);
            }
        }
        while (changed);

        var result = new List<string>(qualified);
        result.Sort(System.StringComparer.Ordinal);
        return new EquatableArray<string>(result.ToImmutableArray());
    }
}
