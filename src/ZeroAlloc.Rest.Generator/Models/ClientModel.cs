using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic,
    int MaxErrorBodyBytes,
    IReadOnlyList<ErrorMapperModel> ErrorMappers,
    IReadOnlyList<DiagnosticInfo> Diagnostics)
{
    // The generated types follow the interface: a public client over an internal interface
    // would leak it, and fails to compile when the interface uses internal DTOs.
    internal string Accessibility => IsPublic ? "public" : "internal";

    // Partial declarations must agree on accessibility, so internal clients get their own class.
    internal string ExtensionsClassName => IsPublic ? "GeneratedRestClientExtensions" : "InternalGeneratedRestClientExtensions";

    // Method-level serializers that need their own constructor parameter. A method override equal to
    // the interface-level type reuses the main serializer instead of injecting it twice.
    internal IReadOnlyList<string> GetOverrideSerializerTypes()
    {
        var result = new List<string>();
        foreach (var m in Methods)
        {
            if (m.SerializerTypeName != null
                && m.SerializerTypeName != SerializerTypeName
                && !result.Contains(m.SerializerTypeName))
                result.Add(m.SerializerTypeName);
        }
        return result.AsReadOnly();
    }

    // The error types methods use that have a usable mapper, each with that mapper and the error type
    // as the mapper declares it, in order of first use. Each gets one constructor parameter. A mapper
    // for an error type no method uses is only registered.
    internal IReadOnlyList<(string ErrorTypeName, string MapperTypeName, string MapperErrorTypeName)> GetUsedErrorMappings()
    {
        var result = new List<(string ErrorTypeName, string MapperTypeName, string MapperErrorTypeName)>();
        foreach (var m in Methods)
        {
            if (!m.MapsError || m.ErrorMapperTypeName is null)
                continue;
            var seen = false;
            foreach (var (errorType, _, _) in result)
            {
                if (errorType == m.ErrorTypeName) { seen = true; break; }
            }
            if (!seen)
                result.Add((m.ErrorTypeName!, m.ErrorMapperTypeName, m.MapperErrorTypeName!));
        }
        return result.AsReadOnly();
    }
}
