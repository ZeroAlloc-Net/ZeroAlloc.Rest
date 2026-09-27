namespace ZeroAlloc.Rest.Tools;

// What a run generates besides the interface. The CLI's --models and the MSBuild item's
// GenerateModels metadata set GenerateModels.
internal sealed record GenerationOptions(bool GenerateModels)
{
    internal static GenerationOptions Default { get; } = new(GenerateModels: true);
}
