using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic)
{
    // The generated types follow the interface: a public client over an internal interface
    // would leak it, and fails to compile when the interface uses internal DTOs.
    internal string Accessibility => IsPublic ? "public" : "internal";

    // Partial declarations must agree on accessibility, so internal clients get their own class.
    internal string ExtensionsClassName => IsPublic ? "GeneratedRestClientExtensions" : "InternalGeneratedRestClientExtensions";

    internal IReadOnlyList<string> GetOverrideSerializerTypes()
    {
        var result = new List<string>();
        foreach (var m in Methods)
        {
            if (m.SerializerTypeName != null && !result.Contains(m.SerializerTypeName))
                result.Add(m.SerializerTypeName);
        }
        return result.AsReadOnly();
    }
}
