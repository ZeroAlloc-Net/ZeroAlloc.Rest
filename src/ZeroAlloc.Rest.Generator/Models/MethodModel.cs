namespace ZeroAlloc.Rest.Generator.Models;

internal record MethodModel(
    string Name,
    string HttpMethod,
    string Route,
    string ReturnTypeName,
    string? InnerTypeName,
    bool ReturnsResult,
    bool ReturnsVoid,
    EquatableArray<ParameterModel> Parameters,
    string? SerializerTypeName,
    EquatableArray<(string Name, string Value)> StaticHeaders,
    LocationInfo? Location,
    string? ErrorTypeName,
    string? ErrorMapperTypeName,
    string? DeclaredErrorTypeName,
    string? MapperErrorTypeName,
    bool MappedErrorNeedsNullCheck,
    EquatableArray<string> EvaluatedRouteTokens,
    bool ReturnsUnitResult,
    bool InnerTypeIsValueType,
    bool InnerTypeIsNullable,
    bool StreamResponses,
    bool ReturnsStream,
    EventStreamModel? EventStream = null)
{
    // EventStream is set for a method returning IAsyncEnumerable<T>: it is emitted as an iterator
    // over a text/event-stream response, never by the Task paths. Its ErrorTypeName, when set, is
    // [EventStream(ErrorType = ...)], resolved to a mapper exactly as a Result's E is.
    // ReturnsStream is set when T, the success type, is System.IO.Stream: the call always sends with
    // ResponseHeadersRead, and the success body is handed to the caller as a stream that owns the
    // response, never deserialized.
    // StreamResponses is the method's own value when its HTTP attribute sets one, and otherwise the
    // interface's: the call sends with ResponseHeadersRead and reads the body from the connection.
    // EvaluatedRouteTokens are the {token} names the URL keeps as C# interpolation holes, exactly as
    // before ZRA005; see RouteTokenBinding.
    // ErrorTypeName is the key a mapper is matched by: no tuple element names and no nullable
    // annotations. DeclaredErrorTypeName is the method's E with its annotations, which the generated
    // Result type must repeat. MapperErrorTypeName is E as the mapper declares it, which the injected
    // IHttpErrorMapper<E> must repeat. MappedErrorNeedsNullCheck is set when the mapper may return null
    // but the method's E is not nullable.
    // ReturnsUnitResult is set for UnitResult<E>: ReturnsResult is also set, so the failure paths are
    // exactly those of Result<T, E>, and InnerTypeName is null because success reads no body.
    // InnerTypeIsValueType and InnerTypeIsNullable describe T, the type a success body is read as.
    // T is nullable when it is a nullable reference type or Nullable<T>: a body of JSON null or an
    // empty body is then a success with null. Otherwise such a body is a failure, never a null T.
    // An oblivious reference type counts as not nullable: the generated code enables nullable.
    internal const string HttpErrorTypeName = "global::ZeroAlloc.Rest.HttpError";

    // Result<T, E>, or an [EventStream] ErrorType, with E other than HttpError: every failure goes
    // through an IHttpErrorMapper<E>. ErrorMapperTypeName is null when no usable [ErrorMapper] maps E.
    internal bool MapsError => (ReturnsResult || EventStream is not null)
        && ErrorTypeName is not null && ErrorTypeName != HttpErrorTypeName;
}
