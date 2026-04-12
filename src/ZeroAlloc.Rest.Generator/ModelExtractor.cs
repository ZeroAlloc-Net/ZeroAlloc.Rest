using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ModelExtractor
{
    private const string GetAttr    = "ZeroAlloc.Rest.Attributes.GetAttribute";
    private const string PostAttr   = "ZeroAlloc.Rest.Attributes.PostAttribute";
    private const string PutAttr    = "ZeroAlloc.Rest.Attributes.PutAttribute";
    private const string PatchAttr  = "ZeroAlloc.Rest.Attributes.PatchAttribute";
    private const string DeleteAttr = "ZeroAlloc.Rest.Attributes.DeleteAttribute";
    private const string BodyAttr   = "ZeroAlloc.Rest.Attributes.BodyAttribute";
    private const string QueryAttr  = "ZeroAlloc.Rest.Attributes.QueryAttribute";
    private const string HeaderAttr = "ZeroAlloc.Rest.Attributes.HeaderAttribute";
    private const string SerializerAttr = "ZeroAlloc.Rest.Attributes.SerializerAttribute";
    private const string ResultOpenType = "ZeroAlloc.Results.Result<T, E>";

    internal static ClientModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
            return null;

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        var interfaceName = interfaceSymbol.Name;
        // Strip leading 'I' to form implementation name: IUserApi -> UserApiClient
        var className = interfaceName.Length > 1 && interfaceName[0] == 'I'
            ? interfaceName.Substring(1) + "Client"
            : interfaceName + "Client";

        var clientSerializer = GetSerializerType(interfaceSymbol);

        var methods = new List<MethodModel>();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method) continue;
            var methodModel = ExtractMethod(method);
            if (methodModel is not null) methods.Add(methodModel);
        }

        return new ClientModel(ns, interfaceName, className, methods.AsReadOnly(), clientSerializer);
    }

    private static MethodModel? ExtractMethod(IMethodSymbol method)
    {
        string? httpMethod = null;
        string? route = null;

        foreach (var attr in method.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.ToDisplayString();
            if (attrClass is null) continue;
            if (attrClass == GetAttr    && attr.ConstructorArguments.Length > 0) { httpMethod = "GET";    route = (string?)attr.ConstructorArguments[0].Value; break; }
            if (attrClass == PostAttr   && attr.ConstructorArguments.Length > 0) { httpMethod = "POST";   route = (string?)attr.ConstructorArguments[0].Value; break; }
            if (attrClass == PutAttr    && attr.ConstructorArguments.Length > 0) { httpMethod = "PUT";    route = (string?)attr.ConstructorArguments[0].Value; break; }
            if (attrClass == PatchAttr  && attr.ConstructorArguments.Length > 0) { httpMethod = "PATCH";  route = (string?)attr.ConstructorArguments[0].Value; break; }
            if (attrClass == DeleteAttr && attr.ConstructorArguments.Length > 0) { httpMethod = "DELETE"; route = (string?)attr.ConstructorArguments[0].Value; break; }
        }

        var staticHeaders = new List<(string, string)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != HeaderAttr) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            var headerName = attr.ConstructorArguments[0].Value as string;
            if (headerName is null) continue;
            string? headerValue = null;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Value" && namedArg.Value.Value is string v)
                {
                    headerValue = v;
                    break;
                }
            }
            if (headerValue != null)
                staticHeaders.Add((headerName, headerValue));
        }

        if (httpMethod is null || route is null) return null;

        var returnType = method.ReturnType as INamedTypeSymbol;
        if (returnType is null) return null;

        bool returnsVoid = false;
        bool returnsResult = false;
        string? innerTypeName = null;
        string returnTypeName = returnType.ToDisplayString();

        if (returnType.TypeArguments.Length == 1)
        {
            var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
            innerTypeName = inner?.ToDisplayString();
            returnsResult = inner?.OriginalDefinition.ToDisplayString() == ResultOpenType;
            if (returnsResult && inner?.TypeArguments.Length >= 1)
                innerTypeName = inner.TypeArguments[0].ToDisplayString();
        }
        else
        {
            returnsVoid = true;
        }

        var methodSerializer = GetSerializerType(method);
        var parameters = ExtractParameters(method);

        return new MethodModel(
            method.Name, httpMethod, route, returnTypeName,
            innerTypeName, returnsResult, returnsVoid,
            parameters, methodSerializer, staticHeaders.AsReadOnly());
    }

    private static IReadOnlyList<ParameterModel> ExtractParameters(IMethodSymbol method)
    {
        var result = new List<ParameterModel>();
        foreach (var param in method.Parameters)
        {
            var typeName = param.Type.ToDisplayString();

            if (typeName == "System.Threading.CancellationToken")
            {
                result.Add(new ParameterModel(param.Name, typeName, ParameterKind.CancellationToken));
                continue;
            }

            var kind = ParameterKind.Path; // default: interpreted as path segment
            string? headerName = null;
            string? queryName = null;

            foreach (var attr in param.GetAttributes())
            {
                var attrClass = attr.AttributeClass?.ToDisplayString();
                if (attrClass == BodyAttr)
                {
                    kind = ParameterKind.Body;
                    break;
                }
                if (attrClass == QueryAttr)
                {
                    kind = ParameterKind.Query;
                    // Check named arg "Name", fall back to param name
                    queryName = param.Name;
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "Name" && namedArg.Value.Value is string n)
                        {
                            queryName = n;
                            break;
                        }
                    }
                    break;
                }
                if (attrClass == HeaderAttr)
                {
                    kind = ParameterKind.Header;
                    headerName = attr.ConstructorArguments.Length > 0
                        ? (string?)attr.ConstructorArguments[0].Value ?? param.Name
                        : param.Name;
                    break;
                }
            }

            // Non-nullable value types (int, bool, Guid…) can never be null at runtime.
            // Nullable value types (int?) have OriginalDefinition == System.Nullable<T>.
            bool isNullable = !param.Type.IsValueType
                || param.Type.OriginalDefinition.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Nullable_T;

            bool isCollection = false;
            if (kind == ParameterKind.Query
                && param.Type.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_String)
            {
                // Check if the type itself is IEnumerable<T>
                if (param.Type is INamedTypeSymbol namedParamType
                    && namedParamType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    isCollection = true;
                }
                else
                {
                    // Check if it implements IEnumerable<T>
                    foreach (var iface in param.Type.AllInterfaces)
                    {
                        if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                        {
                            isCollection = true;
                            break;
                        }
                    }
                }
            }

            result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name, isNullable, isCollection));
        }
        return result.AsReadOnly();
    }

    private static string? GetSerializerType(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == SerializerAttr
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol t)
            {
                return t.ToDisplayString();
            }
        }
        return null;
    }
}
