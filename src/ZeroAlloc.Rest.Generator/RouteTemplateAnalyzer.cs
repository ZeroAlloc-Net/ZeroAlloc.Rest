using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

/// <summary>
/// Reports ZRA005 when a method's route template and its route parameters do not match. A route
/// parameter with no {token} of its name is never sent, and a {token} no route parameter binds is
/// sent as literal text.
/// </summary>
/// <remarks>
/// An analyzer rather than the generator reports it, so that it is a warning in source like any
/// other: <c>#pragma warning disable</c>, <c>[SuppressMessage]</c> and .editorconfig all apply.
/// A generator's diagnostic has to be rebuilt from the cached model's path and span, which the
/// compiler does not treat as a source location, so <c>#pragma</c> would not suppress it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RouteTemplateAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.RouteParameterWithoutToken, DiagnosticDescriptors.RouteTokenWithoutParameter);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || !IsRestClientInterface(method.ContainingType))
            return;

        // The same methods the generator implements: an HTTP method attribute and a named return type.
        var httpAttr = ModelExtractor.FindHttpAttribute(method, out _);
        if (httpAttr is null || method.ReturnType is not INamedTypeSymbol)
            return;

        var route = ModelExtractor.RouteOf(httpAttr);
        var tokens = RouteTemplate.Tokens(route);
        var tokenNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var token in tokens)
            tokenNames.Add(token.Name);

        // Parameters bound as [Query], [Header], [Body] or [FormBody], and the CancellationToken, are
        // sent elsewhere: they never bind a token, and are never reported.
        var routeParameterNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var parameter in method.Parameters)
        {
            if (ModelExtractor.ClassifyParameter(parameter, out _, out _) != ParameterKind.Path)
                continue;
            routeParameterNames.Add(parameter.Name);
            if (!tokenNames.Contains(parameter.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RouteParameterWithoutToken,
                    parameter.Locations[0], parameter.Name, method.Name, route));
            }
        }

        var attributeLocation = httpAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? method.Locations[0];
        var reported = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!routeParameterNames.Contains(token.Name) && reported.Add(token.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RouteTokenWithoutParameter,
                    attributeLocation, route, method.Name, token.Name));
            }
        }
    }

    private static bool IsRestClientInterface(INamedTypeSymbol? type)
    {
        if (type is not { TypeKind: TypeKind.Interface })
            return false;
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == RestClientGenerator.ZeroAllocRestClientAttributeName)
                return true;
        }
        return false;
    }
}
