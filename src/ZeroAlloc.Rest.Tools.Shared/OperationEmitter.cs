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

    // serializable receives the request and response types, which the JSON context registers.
    internal static void Emit(
        StringBuilder sb, string path, OpenApiPathItem pathItem, OperationType operationType, OpenApiOperation operation,
        ISchemaTypeNamer namer, List<string> serializable, List<OpenApiWarning> warnings)
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

    // Design decision 4: only JSON content is typed. A body without it, or one that maps to a
    // Stream, keeps today's [Body] object body.
    private static string BodyParameter(OpenApiRequestBody body, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        const string Untyped = "[" + Attributes + "Body] object body";
        if (JsonSchema(body.Content) is not { } schema)
            return Untyped;
        var type = TypeMapper.Map(schema, baseName + "Request", $"{operationName}: request body", namer, isBody: true);
        if (type.Kind == TypeRefKind.Stream)
            return Untyped;
        serializable.Add(type.Name);
        return $"[{Attributes}Body] {TypeMapper.Declare(type, body.Required, schema.Nullable)} body";
    }

    // Spec §6: the success type comes from the first 2xx response with a schema; a 2xx response
    // whose content carries no schema is skipped, and none gives UnitResult. Only a schema under a
    // non-JSON media type is reported as content the client cannot read.
    private static string ReturnType(OpenApiOperation operation, string baseName, string operationName, ISchemaTypeNamer namer, List<string> serializable)
    {
        foreach (var (status, response) in operation.Responses)
        {
            if (!status.StartsWith('2') || response.Content is null)
                continue;
            var withSchema = response.Content.FirstOrDefault(c => c.Value.Schema is not null).Key;
            if (withSchema is null)
                continue;
            var where = $"{operationName}: response {status}";
            TypeRef type;
            if (JsonSchema(response.Content) is { } schema)
            {
                type = TypeMapper.Map(schema, baseName + "Response", where, namer, isBody: true);
                if (type.Kind == TypeRefKind.Stream)
                {
                    namer.Unsupported(where, "binary content needs a raw stream response, which ZeroAlloc.Rest does not support yet");
                    type = TypeRef.JsonElement;
                }
            }
            else
            {
                namer.Unsupported(where, $"its content '{withSchema}' is not JSON, which the generated client cannot read yet");
                type = TypeRef.JsonElement;
            }
            serializable.Add(type.Name);
            return $"{ResultOf}{type.Name}, {HttpError}>>";
        }
        return UnitResult;
    }

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
    private static (string Comment, OpenApiWarning Warning) Skipped(OpenApiParameter param, string operationName)
    {
        // ZeroAlloc.Rest binds no cookies. Emitted without an attribute, a cookie parameter became
        // a route parameter with no token, and its value was never sent.
        return ($"// Cookie parameter '{param.Name}' is not emitted: ZeroAlloc.Rest has no cookie binding.",
            new OpenApiWarning(OpenApiWarning.CookieParameterNotEmitted,
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
