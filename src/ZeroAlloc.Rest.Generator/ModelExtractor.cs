using System.Collections.Generic;
using System.Linq;
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
    private const string BodyAttr     = "ZeroAlloc.Rest.Attributes.BodyAttribute";
    private const string FormBodyAttr = "ZeroAlloc.Rest.Attributes.FormBodyAttribute";
    private const string QueryAttr  = "ZeroAlloc.Rest.Attributes.QueryAttribute";
    private const string HeaderAttr = "ZeroAlloc.Rest.Attributes.HeaderAttribute";
    private const string SerializerAttr = "ZeroAlloc.Rest.Attributes.SerializerAttribute";
    private const string ErrorMapperAttr = "ZeroAlloc.Rest.Attributes.ErrorMapperAttribute";
    private const string ErrorMapperOpenType = "ZeroAlloc.Rest.IHttpErrorMapper<TError>";
    private const string NotConstructibleReason = "it must be a closed, non-abstract class with a public constructor";
    private const string NoMapperInterfaceReason = "it implements no IHttpErrorMapper<TError> interface";
    private const string ResultOpenType = "ZeroAlloc.Results.Result<T, E>";
    private const int DefaultMaxErrorBodyBytes = 65536;

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
        var diagnostics = new List<DiagnosticInfo>();
        var mappers = ResolveErrorMappers(interfaceSymbol, diagnostics, ct);

        var methods = new List<MethodModel>();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method) continue;
            var methodModel = ExtractMethod(method, mappers, diagnostics);
            if (methodModel is not null) methods.Add(methodModel);
        }

        return new ClientModel(ns, interfaceName, className, methods.AsReadOnly(), clientSerializer,
            IsEffectivelyPublic(interfaceSymbol), GetMaxErrorBodyBytes(ctx),
            mappers.Valid.AsReadOnly(), diagnostics.AsReadOnly());
    }

    private sealed class ErrorMapperResolution
    {
        internal List<ErrorMapperModel> Valid { get; } = new();

        // Error type key -> the usable mapper type that maps it, global::-qualified, and the error
        // type exactly as that mapper declares it, nullable annotation included.
        internal Dictionary<string, (string MapperTypeName, ITypeSymbol ErrorType)> MapperByError { get; } = new(System.StringComparer.Ordinal);

        // Error types claimed by a mapper that ZRA003 rejected. A method using one is not also ZRA002.
        internal HashSet<string> ClaimedByInvalidMapper { get; } = new(System.StringComparer.Ordinal);

        // An [ErrorMapper] whose argument has a compiler error. Its error types are unknown, so no
        // method on the interface gets ZRA002.
        internal bool HasUnresolvedMapper { get; set; }
    }

    private static ErrorMapperResolution ResolveErrorMappers(
        INamedTypeSymbol interfaceSymbol, List<DiagnosticInfo> diagnostics, CancellationToken ct)
    {
        var resolution = new ErrorMapperResolution();
        // Error type -> display name of the mapper that claimed it first, valid or not, for ZRA004.
        var ownerByError = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var attr in interfaceSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ErrorMapperAttr) continue;

            // No argument, or a type that does not resolve, already has its own compiler error.
            // Which error types such a mapper was meant to map cannot be known, so ZRA002 is
            // suppressed for the whole interface instead of piling on guesses.
            if (attr.ConstructorArguments.Length == 0
                || attr.ConstructorArguments[0].Kind == TypedConstantKind.Error
                || attr.ConstructorArguments[0].Value is ITypeSymbol { TypeKind: TypeKind.Error })
            {
                resolution.HasUnresolvedMapper = true;
                continue;
            }

            var location = LocationInfo.From(
                attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? interfaceSymbol.Locations[0]);

            // An array type or a null argument can never be a mapper, and names no error type.
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol mapperType)
            {
                var argumentDisplay = attr.ConstructorArguments[0].Value is ITypeSymbol other
                    ? other.ToDisplayString()
                    : "null";
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { argumentDisplay, NotConstructibleReason }));
                continue;
            }

            var mapperDisplay = mapperType.ToDisplayString();

            // An open generic cannot be constructed, but still claims each error type that does not
            // depend on its type parameters, so a method using one gets only this ZRA003.
            var definition = mapperType.IsUnboundGenericType ? mapperType.OriginalDefinition : mapperType;
            var implementsMapper = false;
            var errorTypes = new List<ITypeSymbol>();
            foreach (var iface in GetMapperInterfaces(definition))
            {
                implementsMapper = true;
                if (!ContainsTypeParameter(iface.TypeArguments[0]))
                    errorTypes.Add(iface.TypeArguments[0]);
            }

            if (!implementsMapper)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { mapperDisplay, NoMapperInterfaceReason }));
                continue;
            }

            var constructible = !mapperType.IsUnboundGenericType && IsConstructible(mapperType);
            if (!constructible)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    new object[] { mapperDisplay, NotConstructibleReason }));
            }

            var mapperName = mapperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var mapped = new List<string>();
            foreach (var errorType in errorTypes)
            {
                var errorName = ErrorTypeKey(errorType);
                if (ownerByError.TryGetValue(errorName, out var owner))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateErrorMapper, location,
                        new object[] { owner, mapperDisplay, errorType.ToDisplayString() }));
                    continue;
                }

                ownerByError[errorName] = mapperDisplay;
                if (constructible)
                {
                    resolution.MapperByError[errorName] = (mapperName, errorType);
                    mapped.Add(errorName);
                }
                else
                {
                    resolution.ClaimedByInvalidMapper.Add(errorName);
                }
            }

            if (mapped.Count > 0)
                resolution.Valid.Add(new ErrorMapperModel(mapperName, mapped.AsReadOnly()));
        }

        return resolution;
    }

    // The global::-qualified name, without tuple element names but with nullable reference
    // annotations, for generated code that must match a declaration exactly: IHttpErrorMapper<T> is
    // invariant, and so is Result<T, E>.
    private static readonly SymbolDisplayFormat AnnotatedErrorTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.ExpandValueTuple | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string AnnotatedErrorTypeName(ITypeSymbol errorType) => errorType.ToDisplayString(AnnotatedErrorTypeFormat);

    // Error types are keyed without tuple element names, so IHttpErrorMapper<(int A, string B)> maps
    // Result<T, (int X, string Y)>, and without the top-level nullable annotation, so a mapper over
    // JevError? maps Result<T, JevError>; the generated code checks such a mapper's result for null.
    // Nested annotations are kept: List<string?> does not convert to List<string> without a warning,
    // so a mapper over one does not map the other.
    private static string ErrorTypeKey(ITypeSymbol errorType)
        => AnnotatedErrorTypeName(errorType.IsValueType ? errorType : errorType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

    // AllInterfaces leaves out the type itself, so typeof(IHttpErrorMapper<E>) is checked directly.
    private static IEnumerable<INamedTypeSymbol> GetMapperInterfaces(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface && IsMapperInterface(type))
            yield return type;
        foreach (var iface in type.AllInterfaces)
        {
            if (IsMapperInterface(iface))
                yield return iface;
        }
    }

    private static bool IsMapperInterface(INamedTypeSymbol type)
        => type.TypeArguments.Length == 1 && type.OriginalDefinition.ToDisplayString() == ErrorMapperOpenType;

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
        _ => false,
    };

    // TryAddSingleton<TMapper> needs a class, and the container needs a public constructor.
    private static bool IsConstructible(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
            return false;
        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public)
                return true;
        }
        return false;
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
                return false;
        }
        return true;
    }

    // [ZeroAllocRestClient(MaxErrorBodyBytes = n)]. A value below 0 means the same as 0: no read.
    private static int GetMaxErrorBodyBytes(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attr in ctx.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "MaxErrorBodyBytes" && namedArg.Value.Value is int value)
                    return value < 0 ? 0 : value;
            }
        }
        return DefaultMaxErrorBodyBytes;
    }

    private static MethodModel? ExtractMethod(IMethodSymbol method, ErrorMapperResolution mappers, List<DiagnosticInfo> diagnostics)
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
            // A method-level [Header] without a Value is intentionally ignored — there is nothing
            // to emit at compile time. Users who omit Value get no output and no diagnostic.
            if (headerValue != null)
                staticHeaders.Add((headerName, headerValue));
        }

        if (httpMethod is null || route is null) return null;

        var returnType = method.ReturnType as INamedTypeSymbol;
        if (returnType is null) return null;

        bool returnsVoid = false;
        bool returnsResult = false;
        string? innerTypeName = null;
        string? errorTypeName = null;
        string? declaredErrorTypeName = null;
        string? errorMapperTypeName = null;
        string? mapperErrorTypeName = null;
        var mappedErrorNeedsNullCheck = false;
        string returnTypeName = returnType.ToDisplayString();
        var location = LocationInfo.From(method.Locations[0]);

        if (returnType.TypeArguments.Length == 1)
        {
            var inner = returnType.TypeArguments[0] as INamedTypeSymbol;
            innerTypeName = inner?.ToDisplayString();
            returnsResult = inner?.OriginalDefinition.ToDisplayString() == ResultOpenType;
            if (returnsResult && inner?.TypeArguments.Length == 2)
            {
                innerTypeName = inner.TypeArguments[0].ToDisplayString();
                var errorType = inner.TypeArguments[1];
                errorTypeName = ErrorTypeKey(errorType);
                declaredErrorTypeName = AnnotatedErrorTypeName(errorType);

                // An unresolved error type already has its own compiler error.
                if (errorTypeName != MethodModel.HttpErrorTypeName && errorType.TypeKind != TypeKind.Error)
                {
                    if (mappers.MapperByError.TryGetValue(errorTypeName, out var mapper))
                    {
                        errorMapperTypeName = mapper.MapperTypeName;
                        mapperErrorTypeName = AnnotatedErrorTypeName(mapper.ErrorType);
                        // A mapper declared over JevError? may return null, which a method returning
                        // Result<T, JevError> must not pass on as its error. An oblivious E counts as
                        // not nullable: the generated code is compiled with nullable enabled.
                        mappedErrorNeedsNullCheck = !mapper.ErrorType.IsValueType
                            && mapper.ErrorType.NullableAnnotation == NullableAnnotation.Annotated
                            && errorType.NullableAnnotation != NullableAnnotation.Annotated;
                    }
                    else if (!mappers.HasUnresolvedMapper && !mappers.ClaimedByInvalidMapper.Contains(errorTypeName))
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MissingErrorMapper, location,
                            new object[] { method.Name, errorType.ToDisplayString() }));
                }
            }
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
            parameters, methodSerializer, staticHeaders.AsReadOnly(),
            location, errorTypeName, errorMapperTypeName,
            declaredErrorTypeName, mapperErrorTypeName, mappedErrorNeedsNullCheck);
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
                if (attrClass == FormBodyAttr)
                {
                    kind = ParameterKind.FormBody;
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
                // global::-qualified, so a namespace in scope at the interface cannot capture the name.
                return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
        return null;
    }
}
