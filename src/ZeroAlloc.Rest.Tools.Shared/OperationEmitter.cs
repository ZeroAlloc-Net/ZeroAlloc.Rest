using System.Text;
using Microsoft.OpenApi.Models;

namespace ZeroAlloc.Rest.Tools;

// Writes one interface method per operation, spec §6: parameters and body typed from their schemas,
// and a Result or UnitResult return. Every type is global::-qualified, design decision 10.
internal static class OperationEmitter
{
    private const string Attributes = "global::ZeroAlloc.Rest.Attributes.";
    private const string ResultOf = "global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.Result<";
    private const string UnitResult = "global::System.Threading.Tasks.Task<global::ZeroAlloc.Results.UnitResult<global::ZeroAlloc.Rest.HttpError>>";
    private const string HttpError = "global::ZeroAlloc.Rest.HttpError";
    private const string Stream = "global::System.IO.Stream";
    private const string OctetStream = "application/octet-stream";

    // serializable receives the request and response types, which the JSON context registers.
    internal static void Emit(
        StringBuilder sb, string path, OpenApiPathItem pathItem, OperationType operationType, OpenApiOperation operation,
        ISchemaTypeNamer namer, List<string> serializable, List<OpenApiDiagnostic> warnings)
    {
        var httpAttr = operationType switch
        {
            OperationType.Get => "Get",
            OperationType.Post => "Post",
            OperationType.Put => "Put",
            OperationType.Patch => "Patch",
            OperationType.Delete => "Delete",
            _ => null,
        };
        if (httpAttr is null) return;

        var baseName = CSharpNames.ToIdentifier(CSharpNames.ToPascalCase(operation.OperationId
            ?? httpAttr + path.Replace("/", "_", StringComparison.Ordinal).Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal)),
            upperFirst: true);
        var operationName = operation.OperationId ?? $"{httpAttr.ToUpperInvariant()} {path}";

        // The CancellationToken is always ct and the request body always body. A parameter whose
        // identifier would clash with either, or with an earlier parameter, gets a numeric suffix.
        var used = new HashSet<string>(StringComparer.Ordinal) { "ct" };
        if (operation.RequestBody != null)
            used.Add("body");

        var parameters = new List<string>();
        var comments = new List<string>();
        var routeIdentifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in EffectiveParameters(pathItem, operation))
        {
            if (param.In is not (ParameterLocation.Path or ParameterLocation.Query or ParameterLocation.Header))
            {
                var (comment, warning) = Skipped(param, operationName);
                comments.Add(comment);
                warnings.Add(warning);
                continue;
            }

            var identifier = CSharpNames.Unique(CSharpNames.ToIdentifier(param.Name, upperFirst: false), used);
            // The source generator binds a {token} to the parameter of exactly its name, so the
            // route's token is rewritten to the identifier. See RewriteRoute.
            if (param.In == ParameterLocation.Path)
                routeIdentifiers[param.Name] = identifier;
            parameters.Add(Declaration(param, identifier, ParameterType(param, baseName, operationName, namer)));
        }

        if (operation.RequestBody != null)
            parameters.Add(BodyParameter(operation.RequestBody, baseName, operationName, namer, serializable));
        parameters.Add("global::System.Threading.CancellationToken ct = default");

        foreach (var comment in comments)
            sb.Append("    ").AppendLine(comment);
        sb.Append("    [").Append(Attributes).Append(httpAttr).Append('(').Append(CSharpNames.Literal(RewriteRoute(path, routeIdentifiers))).AppendLine(")]");
        sb.Append("    ").Append(ReturnType(operation, baseName, operationName, namer, serializable))
            .Append(' ').Append(baseName).Append("Async(").Append(string.Join(", ", parameters)).AppendLine(");");
        sb.AppendLine();
    }

    // Design decision 14: a path parameter, or a required one, is T; an optional one is T?.
    private static string ParameterType(OpenApiParameter param, string baseName, string operationName, ISchemaTypeNamer namer)
    {
        var type = param.Schema is null
            ? TypeRef.String
            : TypeMapper.Map(param.Schema, baseName + CSharpNames.Pascal(param.Name, "Value"), $"{operationName}: parameter '{param.Name}'", namer);
        var required = param.Required || param.In == ParameterLocation.Path;
        return TypeMapper.Declare(type, required, param.Schema?.Nullable == true);
    }

    private static string Declaration(OpenApiParameter param, string identifier, string type)
    {
        var name = CSharpNames.Escape(identifier);
        return param.In switch
        {
            ParameterLocation.Query when string.Equals(identifier, param.Name, StringComparison.Ordinal)
                => $"[{Attributes}Query] {type} {name}",
            ParameterLocation.Query => $"[{Attributes}Query(Name = {CSharpNames.Literal(param.Name)})] {type} {name}",
            ParameterLocation.Header => $"[{Attributes}Header({CSharpNames.Literal(param.Name)})] {type} {name}",
            _ => $"{type} {name}",
        };
    }

    // Design decision 4: JSON content is typed. Binary content, application/octet-stream or a
    // format: binary schema, is a Stream sent with its media type (#358). Any other body, such as
    // XML, multipart or form content, keeps [Body] object body.
    private static string BodyParameter(OpenApiRequestBody body, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        const string Untyped = "[" + Attributes + "Body] object body";
        if (JsonSchema(body.Content) is { } schema)
        {
            var type = TypeMapper.Map(schema, baseName + "Request", $"{operationName}: request body", namer, isBody: true);
            if (type.Kind != TypeRefKind.Stream)
            {
                var declared = TypeMapper.Declare(type, body.Required, schema.Nullable);
                serializable.Add(Registered(type, declared));
                return $"[{Attributes}Body] {declared} body";
            }
        }
        if (BinaryMediaType(body.Content) is not { } mediaType)
            return Untyped;
        var stream = body.Required ? Stream : Stream + "?";
        // A range such as image/* names no type to send, so the body goes as application/octet-stream,
        // the default [Body] needs no ContentType for.
        return mediaType.Contains('*', StringComparison.Ordinal) || string.Equals(mediaType, OctetStream, StringComparison.OrdinalIgnoreCase)
            ? $"[{Attributes}Body] {stream} body"
            : $"[{Attributes}Body(ContentType = {CSharpNames.Literal(mediaType)})] {stream} body";
    }

    // The first media type whose content is binary: application/octet-stream, with or without a
    // schema, or any media type whose schema is a string of format: binary.
    private static string? BinaryMediaType(IDictionary<string, OpenApiMediaType>? content)
    {
        if (content is null)
            return null;
        foreach (var (mediaType, media) in content)
        {
            var type = mediaType.Split(';')[0].Trim();
            if (string.Equals(type, OctetStream, StringComparison.OrdinalIgnoreCase)
                || media.Schema is { Type: "string", Format: "binary" })
                return type;
        }
        return null;
    }

    // The type the serializer is called with is the declared one. For a value type, T? is
    // Nullable<T>, which the context registers on its own. A reference type is registered bare:
    // typeof takes no nullable reference type annotation.
    private static string Registered(TypeRef type, string declared) => type.IsValueType ? declared : type.Name;

    // Spec §6: the success type comes from the first 2xx response with a schema or binary content;
    // a 2xx response whose content carries neither is skipped, and none gives UnitResult. Binary
    // content, application/octet-stream or a format: binary schema, is a Stream. Only a schema
    // under another non-JSON media type is reported as content the client cannot read. The success type is T?
    // when the operation may also succeed with no body, such as 200 with a schema and 204 without,
    // or when the schema is nullable: the client reads an empty body as a success with no value.
    private static string ReturnType(OpenApiOperation operation, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        foreach (var (status, response) in operation.Responses)
        {
            if (!IsSuccess(status))
                continue;
            var binary = BinaryMediaType(response.Content);
            var withSchema = SchemaMediaType(response);
            if (withSchema is null && binary is null)
                continue;
            var where = $"{operationName}: response {status}";
            TypeRef type;
            var nullable = operation.Responses.Any(r => IsSuccess(r.Key) && SchemaMediaType(r.Value) is null && BinaryMediaType(r.Value.Content) is null);
            if (JsonSchema(response.Content) is { } schema
                && TypeMapper.Map(schema, baseName + "Response", where, namer, isBody: true) is { Kind: not TypeRefKind.Stream } mapped)
            {
                type = mapped;
                nullable |= schema.Nullable;
            }
            else if (binary is not null)
            {
                // Binary content is returned as a stream that owns the response (#358). An empty
                // body, such as a 204's, is an empty stream, so the type is never nullable.
                return $"{ResultOf}{Stream}, {HttpError}>>";
            }
            else
            {
                namer.Unsupported(where, $"its content '{withSchema}' is not JSON, which the generated client cannot read yet");
                type = TypeRef.JsonElement;
            }
            var declared = TypeMapper.Declare(type, required: true, nullable);
            serializable.Add(Registered(type, declared));
            return $"{ResultOf}{declared}, {HttpError}>>";
        }
        return UnitResult;
    }

    private static bool IsSuccess(string status) => status.StartsWith('2');

    // The first media type of a response that carries a schema, or null for a response whose
    // content, if any, has none.
    private static string? SchemaMediaType(OpenApiResponse response)
        => response.Content?.FirstOrDefault(c => c.Value.Schema is not null).Key;

    private static OpenApiSchema? JsonSchema(IDictionary<string, OpenApiMediaType> content)
    {
        foreach (var (mediaType, media) in content)
        {
            if (media.Schema is not null && IsJson(mediaType))
                return media.Schema;
        }
        return null;
    }

    // Design decision 5. */* counts: springdoc writes it for JSON responses.
    private static bool IsJson(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        return string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "text/json", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "*/*", StringComparison.Ordinal);
    }

    // A parameter the interface cannot bind: a comment for the emitted method, and a warning.
    // Only a cookie parameter gets here; the OpenAPI reader rejects a parameter with no location.
    private static (string Comment, OpenApiDiagnostic Warning) Skipped(OpenApiParameter param, string operationName)
    {
        // ZeroAlloc.Rest binds no cookies. Emitted without an attribute, a cookie parameter became
        // a route parameter with no token, and its value was never sent.
        return ($"// Cookie parameter '{param.Name}' is not emitted: ZeroAlloc.Rest has no cookie binding.",
            new OpenApiDiagnostic(OpenApiDiagnostic.CookieParameterNotEmitted,
                $"Operation '{operationName}': cookie parameter '{param.Name}' is not emitted, because "
                    + "ZeroAlloc.Rest has no cookie binding. Send the cookie from the HttpClient, for example "
                    + "with a CookieContainer on its handler."));
    }

    // The operation's parameters, after those its path item declares and the operation does not
    // override. OpenAPI identifies a parameter by its name and location.
    private static List<OpenApiParameter> EffectiveParameters(OpenApiPathItem pathItem, OpenApiOperation operation)
    {
        var operationParameters = operation.Parameters ?? new List<OpenApiParameter>();
        var result = new List<OpenApiParameter>();
        if (pathItem.Parameters != null)
        {
            foreach (var shared in pathItem.Parameters)
            {
                if (!operationParameters.Any(p => p.In == shared.In && string.Equals(p.Name, shared.Name, StringComparison.Ordinal)))
                    result.Add(shared);
            }
        }
        result.AddRange(operationParameters);
        return result;
    }

    // Replaces the {name} token of each path parameter with the identifier it was emitted under.
    private static string RewriteRoute(string path, Dictionary<string, string> routeIdentifiers)
    {
        var sb = new StringBuilder(path.Length);
        var i = 0;
        while (i < path.Length)
        {
            var open = path.IndexOf('{', i);
            var close = open < 0 ? -1 : path.IndexOf('}', open + 1);
            if (close < 0)
            {
                sb.Append(path, i, path.Length - i);
                break;
            }
            var name = path.Substring(open + 1, close - open - 1);
            sb.Append(path, i, open - i)
                .Append('{')
                .Append(routeIdentifiers.TryGetValue(name, out var identifier) ? identifier : name)
                .Append('}');
            i = close + 1;
        }
        return sb.ToString();
    }
}
