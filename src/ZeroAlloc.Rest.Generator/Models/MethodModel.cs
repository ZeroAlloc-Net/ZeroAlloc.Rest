using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,
    string? InnerTypeName,
    bool ReturnsApiResponse,
    bool ReturnsVoid,
    IReadOnlyList<ParameterModel> Parameters,
    string? SerializerTypeName);
