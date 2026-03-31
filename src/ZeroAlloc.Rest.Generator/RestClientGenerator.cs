using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Rest.Generator;

[Generator]
public sealed class RestClientGenerator : IIncrementalGenerator
{
    private const string ZeroAllocRestClientAttributeName =
        "ZeroAlloc.Rest.Attributes.ZeroAllocRestClientAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var clientModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZeroAllocRestClientAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(clientModels, static (ctx, model) =>
            ClientEmitter.Emit(ctx, model));
    }
}
