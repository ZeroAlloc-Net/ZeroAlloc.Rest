using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Rest.Generator;

[Generator]
public sealed class RestClientGenerator : IIncrementalGenerator
{
    internal const string ZeroAllocRestClientAttributeName =
        "ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute";

    // Declared by ZeroAlloc.Rest.DependencyInjection. When it resolves, the generator emits the
    // Add{I} registration and the client's IGeneratedRestClient members; without it, generated
    // clients need nothing from Microsoft.Extensions.
    internal const string DependencyInjectionMarkerName =
        "ZeroAlloc.Rest.DependencyInjection.DependencyInjectionMarker";

    private const string GeneratedRestClientInterfaceName = "ZeroAlloc.Rest.IGeneratedRestClient`1";
    private const string HttpClientNameMemberName = "HttpClientName";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var clientModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZeroAllocRestClientAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName("ClientModels");

        // Reruns on every compilation, but yields an equal bool, so the outputs stay cached.
        // GetTypeByMetadataName returns null when the name resolves in more than one referenced
        // assembly, which would silently turn DI emission off; GetTypesByMetadataName reports every
        // match instead, and the result stays a plain bool for caching.
        var dependencyInjection = context.CompilationProvider
            .Select(static (compilation, _) => !compilation.GetTypesByMetadataName(DependencyInjectionMarkerName).IsEmpty)
            .WithTrackingName("DependencyInjectionEnabled");

        // The clients whose named HttpClient would share its name with another client's, and so
        // get a namespace-qualified one (#395). Only the small keys are collected, and the result is
        // an equal array unless a name changes, so editing one client leaves the others cached.
        var qualifiedHttpClientNames = clientModels
            .Select(static (m, _) => m.NameKey)
            .Collect()
            .Select(static (keys, _) => HttpClientNames.Qualified(keys))
            .WithTrackingName("QualifiedHttpClientNames");

        // Whether the referenced ZeroAlloc.Rest.DependencyInjection declares
        // IGeneratedRestClient<TSelf>.HttpClientName. An older one does not, and a client declaring
        // it there would not compile, so only Add{I} is qualified then.
        var httpClientNameMember = context.CompilationProvider
            .Select(static (compilation, _) => HasHttpClientNameMember(compilation))
            .WithTrackingName("HttpClientNameMember");

        context.RegisterSourceOutput(
            clientModels.Combine(dependencyInjection).Combine(qualifiedHttpClientNames).Combine(httpClientNameMember),
            static (ctx, input) =>
            {
                var (((model, dependencyInjectionEnabled), qualified), nameMember) = input;
                var qualifiedName = qualified.Contains(model.HintNameStem) ? model.QualifiedDisplayName : null;
                ClientEmitter.Emit(ctx, model, dependencyInjectionEnabled, nameMember ? qualifiedName : null);
                if (dependencyInjectionEnabled)
                    DiEmitter.Emit(ctx, model, qualifiedName);
            });
    }

    private static bool HasHttpClientNameMember(Compilation compilation)
    {
        foreach (var type in compilation.GetTypesByMetadataName(GeneratedRestClientInterfaceName))
        {
            if (!type.GetMembers(HttpClientNameMemberName).IsEmpty)
                return true;
        }
        return false;
    }
}
