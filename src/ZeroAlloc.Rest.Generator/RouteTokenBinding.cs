using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

// How each {token} of a method's route reaches the URL. Shared by the generator, which emits it,
// and RouteTemplateAnalyzer, which reports ZRA005 from it, so the two cannot disagree.
//
// Before ZRA005, a method with any route or query parameter built its URL as one interpolated
// string, and every token no route parameter replaced became a hole holding the token's text as a
// C# expression. Some of those produced working URLs: a {id} that a [Query] or [Header] parameter
// named id filled, {id:D4}, {page + 1}, or {ApiInfo.Version} naming a constant. They keep working:
// such a token is Evaluated, emitted as the same hole, byte for byte. Everything else is Literal.
// That covers a token that did not compile (CS0103 for a typo), a method with neither route nor
// query parameters, whose route was a plain string, and a token that only bound to the generated
// client's own fields, locals or this, whose value was an implementation detail such as a type name
// or a timestamp.
internal static class RouteTokenBinding
{
    internal enum Kind { RouteParameter, Evaluated, Literal }

    internal sealed class BoundToken
    {
        internal BoundToken(string name, Kind kind, string? source, List<string> referencedParameters)
        {
            Name = name;
            TokenKind = kind;
            Source = source;
            ReferencedParameters = referencedParameters;
        }

        internal string Name { get; }
        internal Kind TokenKind { get; }

        // For an Evaluated token, what it takes its value from, as ZRA005's message says it.
        internal string? Source { get; }

        // For an Evaluated token, the method parameters it reads.
        internal List<string> ReferencedParameters { get; }
    }

    // The usings every generated client file declares, as ClientEmitter writes them. A hole was
    // bound against these, in the interface's namespace.
    private static readonly string[] GeneratedUsings =
        { "System", "System.Net.Http", "System.Net.Http.Headers", "System.Threading", "System.Threading.Tasks" };

    // One entry per distinct token name, in order of first appearance.
    internal static List<BoundToken> Bind(
        IMethodSymbol method, string route, bool clientHasQueryParameter, Compilation compilation, CancellationToken ct)
    {
        var kinds = new Dictionary<string, ParameterKind>(System.StringComparer.Ordinal);
        var interpolated = false;
        foreach (var parameter in method.Parameters)
        {
            var kind = ModelExtractor.ClassifyParameter(parameter, out _, out _);
            kinds[parameter.Name] = kind;
            if (kind is ParameterKind.Path or ParameterKind.Query)
                interpolated = true;
        }

        var result = new List<BoundToken>();
        var probed = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var token in RouteTemplate.Tokens(route))
        {
            if (!seen.Add(token.Name))
                continue;
            if (kinds.TryGetValue(token.Name, out var kind) && kind == ParameterKind.Path)
                result.Add(new BoundToken(token.Name, Kind.RouteParameter, null, new List<string>()));
            else if (interpolated && IsProbeable(token.Name))
                probed.Add(token.Name);
            else
                result.Add(new BoundToken(token.Name, Kind.Literal, null, new List<string>()));
        }

        if (probed.Count > 0)
            result.AddRange(Probe(method, probed, kinds, clientHasQueryParameter, compilation, ct));
        return result;
    }

    // Whether any method the generator implements on the interface has a [Query] parameter. The
    // generated file then also declares using ZeroAlloc.Collections.
    internal static bool ClientHasQueryParameter(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method || method.ReturnType is not INamedTypeSymbol
                || ModelExtractor.FindHttpAttribute(method, out _) is null)
                continue;
            foreach (var parameter in method.Parameters)
            {
                if (ModelExtractor.ClassifyParameter(parameter, out _, out _) == ParameterKind.Query)
                    return true;
            }
        }
        return false;
    }

    // A quote, backslash or line break in a hole never compiled: the route was pasted into the
    // generated string literal as it is.
    private static bool IsProbeable(string name)
    {
        foreach (var c in name)
        {
            if (c is '"' or '\\' or '\r' or '\n')
                return false;
        }
        return true;
    }

    // Compiles each token as the hole it used to be, in a probe that declares what the generated
    // method had in scope apart from the client's own members: the interface's namespace, the
    // generated usings and the method's parameters. A token that compiles there is Evaluated. Each
    // token gets its own tree, so a comment or stray text in one cannot affect another.
    private static IEnumerable<BoundToken> Probe(
        IMethodSymbol method, List<string> names, Dictionary<string, ParameterKind> kinds,
        bool clientHasQueryParameter, Compilation compilation, CancellationToken ct)
    {
        var prefix = new StringBuilder();
        foreach (var u in GeneratedUsings)
            prefix.Append("using ").Append(u).Append(";\n");
        if (clientHasQueryParameter)
            prefix.Append("using ZeroAlloc.Collections;\n");
        prefix.Append("using ZeroAlloc.Rest;\n");
        var ns = method.ContainingType.ContainingNamespace;
        if (!ns.IsGlobalNamespace)
            prefix.Append("namespace ").Append(ns.ToDisplayString()).Append(" {\n");
        prefix.Append("internal static class __ZeroAllocRestRouteProbe {\n");
        prefix.Append("static string __Token");
        if (method.TypeParameters.Length > 0)
        {
            prefix.Append('<');
            for (var i = 0; i < method.TypeParameters.Length; i++)
                prefix.Append(i > 0 ? ", @" : "@").Append(method.TypeParameters[i].Name);
            prefix.Append('>');
        }
        prefix.Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            prefix.Append(i > 0 ? ", " : "")
                .Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(" @").Append(parameter.Name);
        }
        prefix.Append(") => $\"{");
        var suffix = ns.IsGlobalNamespace ? "}\";\n}\n" : "}\";\n}\n}\n";

        var parseOptions = method.Locations[0].SourceTree?.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
        foreach (var name in names)
        {
            var tree = CSharpSyntaxTree.ParseText(prefix + name + suffix, parseOptions, cancellationToken: ct);
            var model = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            MethodDeclarationSyntax? declaration = null;
            foreach (var node in tree.GetRoot(ct).DescendantNodes())
            {
                if (node is MethodDeclarationSyntax m)
                {
                    declaration = m;
                    break;
                }
            }

            var hole = declaration?.ExpressionBody?.Expression is InterpolatedStringExpressionSyntax s
                && s.Contents.Count == 1 && s.Contents[0] is InterpolationSyntax interpolation
                ? interpolation
                : null;
            if (hole is null || HasError(model, tree, ct))
            {
                yield return new BoundToken(name, Kind.Literal, null, new List<string>());
                continue;
            }

            var referenced = new List<string>();
            foreach (var node in hole.Expression.DescendantNodesAndSelf())
            {
                if (node is IdentifierNameSyntax identifier
                    && model.GetSymbolInfo(identifier, ct).Symbol is IParameterSymbol p
                    && !referenced.Contains(p.Name))
                    referenced.Add(p.Name);
            }

            yield return new BoundToken(name, Kind.Evaluated, Describe(hole, referenced, kinds), referenced);
        }
    }

    // Errors anywhere in the probe but its using directives: a using of a namespace the
    // compilation lacks never mattered to the hole.
    private static bool HasError(SemanticModel model, SyntaxTree tree, CancellationToken ct)
    {
        foreach (var diagnostic in model.GetDiagnostics(null, ct))
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            var node = tree.GetRoot(ct).FindNode(diagnostic.Location.SourceSpan);
            if (node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is null)
                return true;
        }
        return false;
    }

    private static string Describe(InterpolationSyntax hole, List<string> referenced, Dictionary<string, ParameterKind> kinds)
    {
        if (referenced.Count == 0)
            return "the C# expression '" + hole.Expression + "'";

        var sb = new StringBuilder();
        for (var i = 0; i < referenced.Count; i++)
        {
            if (i > 0)
                sb.Append(" and ");
            sb.Append("the ").Append(KindName(kinds[referenced[i]])).Append(" parameter '").Append(referenced[i]).Append('\'');
        }
        return sb.ToString();
    }

    private static string KindName(ParameterKind kind) => kind switch
    {
        ParameterKind.Query => "query",
        ParameterKind.Header => "header",
        ParameterKind.Body => "body",
        ParameterKind.FormBody => "form body",
        ParameterKind.CancellationToken => "cancellation token",
        _ => "route",
    };
}
