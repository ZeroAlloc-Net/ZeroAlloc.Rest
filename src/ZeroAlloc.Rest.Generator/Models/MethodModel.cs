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
    string? SerializerTypeName,
    IReadOnlyList<(string Name, string Value)> StaticHeaders,
    LocationInfo Location,
    string? ErrorTypeName,
    string? ErrorMapperTypeName,
    string? DeclaredErrorTypeName,
    string? MapperErrorTypeName,
    bool MappedErrorNeedsNullCheck)
{
    // ErrorTypeName is the key a mapper is matched by: no tuple element names and no nullable
    // annotations. DeclaredErrorTypeName is the method's E with its annotations, which the generated
    // Result type must repeat. MapperErrorTypeName is E as the mapper declares it, which the injected
    // IHttpErrorMapper<E> must repeat. MappedErrorNeedsNullCheck is set when the mapper may return null
    // but the method's E is not nullable.
    internal const string HttpErrorTypeName = "global::ZeroAlloc.Rest.HttpError";

    // Result<T, E> with E other than HttpError: every failure goes through an IHttpErrorMapper<E>.
    // ErrorMapperTypeName is null when no usable [ErrorMapper] maps E.
    internal bool MapsError => ReturnsResult && ErrorTypeName is not null && ErrorTypeName != HttpErrorTypeName;
}
