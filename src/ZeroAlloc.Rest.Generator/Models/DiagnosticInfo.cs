using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator.Models;

// A diagnostic found while extracting the model, reported when the client is emitted.
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo Location, object[] MessageArgs)
{
    internal Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location.ToLocation(), MessageArgs);
}
