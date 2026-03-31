using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName)
{
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
