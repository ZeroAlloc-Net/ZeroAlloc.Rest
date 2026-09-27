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
        ImmutableArray.Create(
            DiagnosticDescriptors.RouteParameterWithoutToken,
            DiagnosticDescriptors.RouteTokenWithoutParameter,
            DiagnosticDescriptors.RouteTokenEvaluated);

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
        var tokens = RouteTokenBinding.Bind(method, route,
            RouteTokenBinding.ClientHasQueryParameter(method.ContainingType), context.Compilation, context.CancellationToken);

        // A route parameter reaches the URL through a token of exactly its name, or through an
        // evaluated token that reads it, such as {id:D4}. Parameters bound as [Query], [Header],
        // [Body] or [FormBody], and the CancellationToken, are sent elsewhere and never reported.
        var usedNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (token.TokenKind == RouteTokenBinding.Kind.RouteParameter)
                usedNames.Add(token.Name);
            foreach (var name in token.ReferencedParameters)
                usedNames.Add(name);
        }

        foreach (var parameter in method.Parameters)
        {
            if (ModelExtractor.ClassifyParameter(parameter, out _, out _) == ParameterKind.Path
                && !usedNames.Contains(parameter.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RouteParameterWithoutToken,
                    parameter.Locations[0], parameter.Name, method.Name, route));
            }
        }

        var attributeLocation = httpAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? method.Locations[0];
        foreach (var token in tokens)
        {
            if (token.TokenKind == RouteTokenBinding.Kind.Evaluated)
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RouteTokenEvaluated,
                    attributeLocation, route, method.Name, token.Name, token.Source));
            }
            else if (token.TokenKind == RouteTokenBinding.Kind.Literal)
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
