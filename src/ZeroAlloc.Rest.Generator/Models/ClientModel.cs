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
}
