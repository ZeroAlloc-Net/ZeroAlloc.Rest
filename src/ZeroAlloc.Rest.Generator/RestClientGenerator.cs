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
        var dependencyInjection = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(DependencyInjectionMarkerName) is not null)
            .WithTrackingName("DependencyInjectionEnabled");

        context.RegisterSourceOutput(clientModels.Combine(dependencyInjection), static (ctx, pair) =>
        {
            var (model, dependencyInjectionEnabled) = pair;
            ClientEmitter.Emit(ctx, model, dependencyInjectionEnabled);
            if (dependencyInjectionEnabled)
                DiEmitter.Emit(ctx, model);
        });
    }
}
