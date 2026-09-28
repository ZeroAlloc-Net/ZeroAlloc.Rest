namespace ZeroAlloc.Rest.Tools;

// What the tool reports about the spec. Code is a stable ZRT diagnostic ID, which the MSBuild task
// logs as the warning or error code and the CLI prints, so either can suppress a warning by it.
// An error fails generation: the CLI exits with 1 and the task fails the build, and neither
// writes the file. docs/advanced.md documents each code, next to the ZRA diagnostics.
internal sealed record OpenApiDiagnostic(string Code, string Message, OpenApiSeverity Severity = OpenApiSeverity.Warning)
{
    // A cookie parameter: ZeroAlloc.Rest has no cookie binding.
    internal const string CookieParameterNotEmitted = "ZRT001";

    // A schema the generated code cannot type, mapped to JsonElement.
    internal const string SchemaMappedToJsonElement = "ZRT002";

    // Two object variants of a union that its converter cannot tell apart: an error for oneOf,
    // whose converter throws on an object matching both, a warning for anyOf, whose converter
    // always takes the first declared.
    internal const string IndistinguishableUnionVariants = "ZRT003";

    internal static OpenApiDiagnostic MappedToJsonElement(string path, string reason)
        => new(SchemaMappedToJsonElement, $"Schema '{path}' is mapped to JsonElement, because {reason}.");
}
