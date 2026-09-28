using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator.Models;

// A diagnostic found while extracting the model, reported when the client is emitted. The message
// arguments are strings, not objects: an object[] compares by reference, so a model holding one
// would never compare equal across runs and the incremental cache would never hit.
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{
    internal Diagnostic ToDiagnostic()
    {
        var args = new object[MessageArgs.Count];
        for (var i = 0; i < args.Length; i++)
            args[i] = MessageArgs[i];
        return Diagnostic.Create(Descriptor, Location?.ToLocation(), args);
    }
}
