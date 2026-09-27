namespace ZeroAlloc.Rest.Tools;

// A part of the spec the emitted interface leaves out. Code is a stable ZRT diagnostic ID, which
// the MSBuild task logs as the warning code and the CLI prints, so either can suppress it.
// docs/advanced.md documents each code, next to the ZRA diagnostics.
internal sealed record OpenApiWarning(string Code, string Message)
{
    // A cookie parameter: ZeroAlloc.Rest has no cookie binding.
    internal const string CookieParameterNotEmitted = "ZRT001";
}
