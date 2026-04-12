namespace ZeroAlloc.Rest.Generator.Models;

internal enum ParameterKind { Path, Query, Body, FormBody, Header, CancellationToken }

internal record ParameterModel(
    string Name,
    string TypeName,
    ParameterKind Kind,
    string? HeaderName = null,
    string? QueryName = null,
    bool IsNullable = true,
    bool IsCollection = false);
