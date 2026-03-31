using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,
    string? InnerTypeName,
    bool ReturnsResult,
    bool ReturnsVoid,
    IReadOnlyList<ParameterModel> Parameters,
    string? SerializerTypeName);
