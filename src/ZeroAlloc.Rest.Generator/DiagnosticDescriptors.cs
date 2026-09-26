using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "ZeroAlloc.Rest.Generator";

    internal static readonly DiagnosticDescriptor ConflictingBody = new(
        id: "ZRA001",
        title: "Conflicting body attributes",
        messageFormat: "Method '{0}' has both [Body] and [FormBody] parameters; only one is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingErrorMapper = new(
        id: "ZRA002",
        title: "No error mapper for a Result error type",
        messageFormat: "Method '{0}' returns a Result with error type '{1}', but no [ErrorMapper] on the interface implements IHttpErrorMapper<{1}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidErrorMapper = new(
        id: "ZRA003",
        title: "Invalid error mapper type",
        messageFormat: "'{0}' cannot be an error mapper: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateErrorMapper = new(
        id: "ZRA004",
        title: "Duplicate error mapper",
        messageFormat: "'{1}' maps '{2}', which '{0}' already maps; declare one [ErrorMapper] per error type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
