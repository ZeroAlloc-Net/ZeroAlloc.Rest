namespace ZeroAlloc.Rest.Generator.Models;

internal enum ParameterKind { Path, Query, Body, FormBody, Header, CancellationToken }

internal record ParameterModel(
    string Name,
    string TypeName,
    ParameterKind Kind,
    string? HeaderName = null,
    string? QueryName = null,
    bool IsNullable = true,
    bool IsCollection = false,
    ValueFormatModel? Format = null,
    // A [Body] whose type is System.IO.Stream or derives from it: sent as it is, not serialized.
    bool IsStream = false,
    // [Body(ContentType = "...")], the media type the body is sent with, or null for the default.
    string? BodyContentType = null);
