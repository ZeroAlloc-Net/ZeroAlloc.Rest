using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    private const string UnitResultOpenType = "ZeroAlloc.Results.UnitResult<E>";
    private const string JsonStringEnumMemberNameAttr = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";
    private const string EnumerableOpenType = "System.Collections.Generic.IEnumerable<T>";
    private const string FlagsAttr = "System.FlagsAttribute";
    private const string FormattableInterface = "System.IFormattable";
    private const int DefaultMaxErrorBodyBytes = 65536;

    // A namespace as a name, not as code: class.Models, where code would need @class.Models.
    private static readonly SymbolDisplayFormat UnescapedNamespaceFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

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
        var nestedPrefix = NestedPrefix(interfaceSymbol);
        // Strip leading 'I' to form implementation name: IUserApi -> UserApiClient, and
        // Orders.IApi -> Orders_ApiClient.
        var className = nestedPrefix + (interfaceName.Length > 1 && interfaceName[0] == 'I'
            ? interfaceName.Substring(1) + "Client"
            : interfaceName + "Client");
        var interfaceReference = interfaceSymbol.ContainingType is null
            ? interfaceName
            : interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var interfaceDisplayName = DisplayWithinNamespace(interfaceSymbol);
        var qualifiedDisplayName = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? interfaceDisplayName
            : interfaceSymbol.ContainingNamespace.ToDisplayString(UnescapedNamespaceFormat) + "." + interfaceDisplayName;
        var hintNameStem = HintNames.ForInterface(interfaceSymbol);

        if (UnsupportedReason(interfaceSymbol) is { } reason)
        {
            var identifier = (ctx.TargetNode as InterfaceDeclarationSyntax)?.Identifier.GetLocation()
                ?? interfaceSymbol.Locations[0];
            var unsupported = new DiagnosticInfo(DiagnosticDescriptors.UnsupportedClientInterface,
                LocationInfo.From(identifier),
                Args(interfaceSymbol.ToDisplayString(), reason));
            return new ClientModel(ns, interfaceName, hintNameStem, className, interfaceReference,
                interfaceDisplayName, qualifiedDisplayName, nestedPrefix, IsSupported: false,
                Methods: default, SerializerTypeName: null, IsPublic: false, MaxErrorBodyBytes: 0,
                ErrorMappers: default, Diagnostics: ToEquatable(new List<DiagnosticInfo> { unsupported }));
        }

        var clientSerializer = GetSerializerType(interfaceSymbol);
        var diagnostics = new List<DiagnosticInfo>();
        var mappers = ResolveErrorMappers(interfaceSymbol, diagnostics, ct);

        var compilation = ctx.SemanticModel.Compilation;
        var clientHasQueryParameter = RouteTokenBinding.ClientHasQueryParameter(interfaceSymbol);
        var clientStreamsResponses = GetStreamResponses(ctx);
        var methods = new List<MethodModel>();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method) continue;
            var methodModel = ExtractMethod(method, mappers, diagnostics, clientHasQueryParameter, clientStreamsResponses, compilation, ct);
            if (methodModel is not null) methods.Add(methodModel);
        }

        return new ClientModel(ns, interfaceName, hintNameStem, className, interfaceReference,
            interfaceDisplayName, qualifiedDisplayName, nestedPrefix, IsSupported: true,
            ToEquatable(methods), clientSerializer,
            IsEffectivelyPublic(interfaceSymbol), GetMaxErrorBodyBytes(ctx),
            ToEquatable(mappers.Valid), ToEquatable(diagnostics));
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
                    Args(argumentDisplay, NotConstructibleReason)));
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
                    Args(mapperDisplay, NoMapperInterfaceReason)));
                continue;
            }

            var constructible = !mapperType.IsUnboundGenericType && IsConstructible(mapperType);
            if (!constructible)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidErrorMapper, location,
                    Args(mapperDisplay, NotConstructibleReason)));
            }

            var mapperName = mapperType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var mapped = new List<string>();
            foreach (var errorType in errorTypes)
            {
                var errorName = ErrorTypeKey(errorType);
                if (ownerByError.TryGetValue(errorName, out var owner))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateErrorMapper, location,
                        Args(owner, mapperDisplay, errorType.ToDisplayString())));
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
                resolution.Valid.Add(new ErrorMapperModel(mapperName, ToEquatable(mapped)));
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

    // Why no client can be generated for the interface, or null when one can. The client is a
    // class at namespace level that implements the interface, so it cannot implement a generic
    // interface without becoming generic itself, cannot name an interface declared inside a generic
    // type without its type arguments, and cannot reach an interface that it, or a containing
    // type, hides as private or protected (#394).
    private static string? UnsupportedReason(INamedTypeSymbol interfaceSymbol)
    {
        // Arity, not IsGenericType, which is also true for a type nested in a generic type.
        if (interfaceSymbol.Arity > 0)
            return "it is generic";

        for (var container = interfaceSymbol.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.Arity > 0)
                return $"it is declared inside the generic type '{container.ToDisplayString()}'";
        }

        for (INamedTypeSymbol? t = interfaceSymbol; t is not null; t = t.ContainingType)
        {
            var hidden = t.DeclaredAccessibility switch
            {
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => null,
            };
            if (hidden is not null)
                return $"'{t.ToDisplayString()}' is {hidden}, and the generated client, a class at namespace level, cannot reach it";
        }

        return null;
    }

    // The containing types' names, outermost first, each followed by '_': "Orders_" for
    // Orders.IApi, "A_B_" for A.B.IApi, and "" for a top-level interface. A type's own name never
    // contains '_' under the .NET naming guidelines, so the prefix cannot make two clients collide.
    private static string NestedPrefix(INamedTypeSymbol interfaceSymbol)
    {
        var prefix = string.Empty;
        for (var container = interfaceSymbol.ContainingType; container is not null; container = container.ContainingType)
            prefix = container.Name + "_" + prefix;
        return prefix;
    }

    // The interface's name within its namespace: "IUserApi", or "Orders.IApi" for a nested one.
    private static string DisplayWithinNamespace(INamedTypeSymbol interfaceSymbol)
    {
        var name = interfaceSymbol.Name;
        for (var container = interfaceSymbol.ContainingType; container is not null; container = container.ContainingType)
            name = container.Name + "." + name;
        return name;
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

    // [ZeroAllocRestClient(StreamResponses = true)]. Off by default.
    private static bool GetStreamResponses(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attr in ctx.Attributes)
        {
            if (StreamResponsesOf(attr) is { } value)
                return value;
        }
        return false;
    }

    // StreamResponses when the attribute sets it, and null when it does not. A method's HTTP
    // attribute that leaves it unset follows the interface, so unset and false differ there.
    private static bool? StreamResponsesOf(AttributeData attr)
    {
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "StreamResponses" && namedArg.Value.Value is bool value)
                return value;
        }
        return null;
    }

    // The attribute's zero-argument constructor has no ConstructorArguments at all; that means
    // an empty route, not a missing one, so it must not be confused with "no HTTP attribute".
    internal static string RouteOf(AttributeData attr)
        => attr.ConstructorArguments.Length > 0 ? (string?)attr.ConstructorArguments[0].Value ?? "" : "";

    // The method's first HTTP method attribute, [Get] to [Delete], and its verb; null for a method
    // without one, which the generator does not implement.
    internal static AttributeData? FindHttpAttribute(IMethodSymbol method, out string? httpMethod)
    {
        foreach (var attr in method.GetAttributes())
        {
            httpMethod = attr.AttributeClass?.ToDisplayString() switch
            {
                GetAttr    => "GET",
                PostAttr   => "POST",
                PutAttr    => "PUT",
                PatchAttr  => "PATCH",
                DeleteAttr => "DELETE",
                _          => null,
            };
            if (httpMethod is not null)
                return attr;
        }
        httpMethod = null;
        return null;
    }

    // How the generated client sends a parameter. A parameter with none of [Body], [FormBody],
    // [Query] or [Header], other than the CancellationToken, is a route parameter: it binds each
    // {token} in the route with exactly its name, and is not sent anywhere else.
    internal static ParameterKind ClassifyParameter(IParameterSymbol param, out string? headerName, out string? queryName)
    {
        headerName = null;
        queryName = null;
        if (param.Type.ToDisplayString() == "System.Threading.CancellationToken")
            return ParameterKind.CancellationToken;

        foreach (var attr in param.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.ToDisplayString();
            if (attrClass == BodyAttr)
                return ParameterKind.Body;
            if (attrClass == FormBodyAttr)
                return ParameterKind.FormBody;
            if (attrClass == QueryAttr)
            {
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
                return ParameterKind.Query;
            }
            if (attrClass == HeaderAttr)
            {
                headerName = attr.ConstructorArguments.Length > 0
                    ? (string?)attr.ConstructorArguments[0].Value ?? param.Name
                    : param.Name;
                return ParameterKind.Header;
            }
        }
        return ParameterKind.Path;
    }

    private static MethodModel? ExtractMethod(
        IMethodSymbol method, ErrorMapperResolution mappers, List<DiagnosticInfo> diagnostics,
        bool clientHasQueryParameter, bool clientStreamsResponses, Compilation compilation, CancellationToken ct)
    {
        var httpAttr = FindHttpAttribute(method, out var httpMethod);
        var route = httpAttr is null ? null : RouteOf(httpAttr);

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

        if (httpAttr is null || httpMethod is null || route is null) return null;

        var returnType = method.ReturnType as INamedTypeSymbol;
        if (returnType is null) return null;

        bool returnsVoid = false;
        bool returnsResult = false;
        var returnsUnitResult = false;
        string? innerTypeName = null;
        ITypeSymbol? innerType = null;
        string? errorTypeName = null;
        string? declaredErrorTypeName = null;
        string? errorMapperTypeName = null;
        string? mapperErrorTypeName = null;
        var mappedErrorNeedsNullCheck = false;
        string returnTypeName = returnType.ToDisplayString();
        var location = LocationInfo.From(method.Locations[0]);

        if (returnType.TypeArguments.Length == 1)
        {
            innerType = returnType.TypeArguments[0];
            var inner = innerType as INamedTypeSymbol;
            innerTypeName = innerType.ToDisplayString();
            var innerDefinition = inner?.OriginalDefinition.ToDisplayString();
            returnsUnitResult = innerDefinition == UnitResultOpenType;
            returnsResult = returnsUnitResult || innerDefinition == ResultOpenType;

            ITypeSymbol? errorType = null;
            if (returnsUnitResult && inner?.TypeArguments.Length == 1)
            {
                innerTypeName = null;
                innerType = null;
                errorType = inner.TypeArguments[0];
            }
            else if (returnsResult && inner?.TypeArguments.Length == 2)
            {
                innerType = inner.TypeArguments[0];
                innerTypeName = innerType.ToDisplayString();
                errorType = inner.TypeArguments[1];
            }

            if (errorType is not null)
            {
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
                            Args(method.Name, errorType.ToDisplayString())));
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
            parameters, methodSerializer, ToEquatable(staticHeaders),
            location, errorTypeName, errorMapperTypeName,
            declaredErrorTypeName, mapperErrorTypeName, mappedErrorNeedsNullCheck,
            EvaluatedRouteTokens(method, route, clientHasQueryParameter, compilation, ct),
            returnsUnitResult,
            innerType?.IsValueType == true,
            innerType is not null && IsNullable(innerType),
            StreamResponsesOf(httpAttr) ?? clientStreamsResponses,
            innerType is not null && IsStreamType(innerType));
    }

    // System.IO.Stream itself, the type a raw response body is returned as.
    private static bool IsStreamType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "Stream", ContainingNamespace: { Name: "IO", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    // System.IO.Stream or a type derived from it, such as FileStream: a raw request body.
    private static bool IsStreamOrDerived(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsStreamType(current))
                return true;
        }
        return false;
    }

    // [Body(ContentType = "...")], or null when the attribute leaves it unset.
    private static string? BodyContentTypeOf(IParameterSymbol param)
    {
        foreach (var attr in param.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != BodyAttr)
                continue;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "ContentType" && namedArg.Value.Value is string contentType)
                    return contentType;
            }
        }
        return null;
    }

    // A nullable reference type, or Nullable<T>.
    private static bool IsNullable(ITypeSymbol type)
        => type.IsValueType
            ? type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            : type.NullableAnnotation == NullableAnnotation.Annotated;

    private static EquatableArray<string> EvaluatedRouteTokens(
        IMethodSymbol method, string route, bool clientHasQueryParameter, Compilation compilation, CancellationToken ct)
    {
        var evaluated = new List<string>();
        foreach (var token in RouteTokenBinding.Bind(method, route, clientHasQueryParameter, compilation, ct))
        {
            if (token.TokenKind == RouteTokenBinding.Kind.Evaluated)
                evaluated.Add(token.Name);
        }
        return ToEquatable(evaluated);
    }

    private static EquatableArray<ParameterModel> ExtractParameters(IMethodSymbol method)
    {
        var result = new List<ParameterModel>();
        foreach (var param in method.Parameters)
        {
            var typeName = param.Type.ToDisplayString();
            var kind = ClassifyParameter(param, out var headerName, out var queryName);
            if (kind == ParameterKind.CancellationToken)
            {
                result.Add(new ParameterModel(param.Name, typeName, ParameterKind.CancellationToken));
                continue;
            }

            // Non-nullable value types (int, bool, Guid…) can never be null at runtime.
            // Nullable value types (int?) have OriginalDefinition == System.Nullable<T>.
            bool isNullable = !param.Type.IsValueType
                || param.Type.OriginalDefinition.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Nullable_T;

            // A query or header collection sends each element as its own value, so its format is the
            // element's. String is IEnumerable<char> but is a single value.
            bool isCollection = false;
            if (kind is ParameterKind.Query or ParameterKind.Header
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

            var format = kind is ParameterKind.Path or ParameterKind.Query or ParameterKind.Header
                ? FormatOf(param.Type, isCollection)
                : null;
            var isStream = kind == ParameterKind.Body && IsStreamOrDerived(param.Type);
            var bodyContentType = kind == ParameterKind.Body ? BodyContentTypeOf(param) : null;
            result.Add(new ParameterModel(param.Name, typeName, kind, headerName, queryName ?? param.Name, isNullable, isCollection, format, isStream, bodyContentType));
        }
        return ToEquatable(result);
    }

    // The format of a value, or of a collection's elements, with Nullable<T> unwrapped. The enum
    // check comes before IFormattable, which enums also implement.
    private static ValueFormatModel FormatOf(ITypeSymbol type, bool isCollection)
    {
        var elementIsNullable = false;
        if (isCollection)
        {
            type = ElementType(type) ?? type;
            // A value-type element can never be null, and comparing it with null is CS8073 or
            // CS0472, errors under TreatWarningsAsErrors.
            elementIsNullable = !type.IsValueType
                || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return new ValueFormatModel(name, IsValueType: true, ValueFormat.Enum, EnumMembers(enumType), elementIsNullable,
                IsFlags: enumType.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == FlagsAttr),
                EnumUnderlyingType: enumType.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        var format = type.SpecialType switch
        {
            SpecialType.System_String => ValueFormat.String,
            SpecialType.System_Boolean => ValueFormat.Boolean,
            SpecialType.System_DateTime => ValueFormat.Iso8601,
            _ when name is "global::System.DateTimeOffset" or "global::System.DateOnly" or "global::System.TimeOnly" => ValueFormat.Iso8601,
            _ when type.AllInterfaces.Any(i => i.ToDisplayString() == FormattableInterface)
                => HasPublicFormattableToString(type) ? ValueFormat.Invariant : ValueFormat.ExplicitInvariant,
            _ => ValueFormat.Text,
        };
        return new ValueFormatModel(name, type.IsValueType, format, default, elementIsNullable);
    }

    // Whether `value.ToString(format, provider)` binds to a public method, or IFormattable is only
    // implemented explicitly and must be called through the interface.
    private static bool HasPublicFormattableToString(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("ToString").OfType<IMethodSymbol>())
            {
                if (method is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, Parameters.Length: 2 }
                    && method.Parameters[0].Type.SpecialType == SpecialType.System_String
                    && method.Parameters[1].Type.ToDisplayString() == "System.IFormatProvider")
                    return true;
            }
        }
        return false;
    }

    // One member per distinct value, with the name System.Text.Json writes for it, in the order its
    // JsonStringEnumConverter walks the members: by ascending value as a sign-extended ulong, the
    // order of Enum.GetValues, then stably by descending bit count. Among members sharing a value
    // the first declared is kept, as STJ keeps the first it sees.
    private static EquatableArray<EnumMemberModel> EnumMembers(INamedTypeSymbol type)
    {
        var fields = new List<(ulong Key, string Member, string Wire)>();
        foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.HasConstantValue || member.ConstantValue is null) continue;
            var wire = member.Name;
            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == JsonStringEnumMemberNameAttr
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string name)
                    wire = name;
            }
            fields.Add((ToUInt64(member.ConstantValue), member.Name, wire));
        }

        var ordered = fields
            .Select((field, index) => (field, index))
            .OrderBy(f => f.field.Key)
            .ThenBy(f => f.index)
            .Select(f => f.field)
            .ToList();
        var seen = new HashSet<ulong>();
        var result = new List<EnumMemberModel>();
        foreach (var field in ordered.Select((field, index) => (field, index))
                     .OrderBy(f => -PopCount(f.field.Key))
                     .ThenBy(f => f.index)
                     .Select(f => f.field))
        {
            if (seen.Add(field.Key))
                result.Add(new EnumMemberModel(field.Member, field.Wire, field.Key));
        }
        return ToEquatable(result);
    }

    private static ulong ToUInt64(object value) => value switch
    {
        sbyte v => unchecked((ulong)v),
        short v => unchecked((ulong)v),
        int v => unchecked((ulong)v),
        long v => unchecked((ulong)v),
        byte v => v,
        ushort v => v,
        uint v => v,
        ulong v => v,
        _ => 0,
    };

    private static int PopCount(ulong value)
    {
        var count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }

    private static ITypeSymbol? ElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == EnumerableOpenType)
            return named.TypeArguments[0];
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == EnumerableOpenType)
                return iface.TypeArguments[0];
        }
        return null;
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

    private static EquatableArray<T> ToEquatable<T>(List<T> items)
        where T : System.IEquatable<T>
        => new(items.ToImmutableArray());

    private static EquatableArray<string> Args(params string[] args) => new(ImmutableArray.Create(args));
}
