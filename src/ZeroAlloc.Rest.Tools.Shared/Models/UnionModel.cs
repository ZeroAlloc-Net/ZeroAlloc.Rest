namespace ZeroAlloc.Rest.Tools;

// Spec §5.6: a sealed record with one As{Variant} property per variant and a generated converter.
internal sealed record UnionModel(string Name, string? Description, bool IsOneOf, EquatableList<UnionVariantModel> Variants)
    : ModelDefinition(Name, Description);
