using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string ClassName,
    IReadOnlyList<MethodModel> Methods,
    string? SerializerTypeName);
