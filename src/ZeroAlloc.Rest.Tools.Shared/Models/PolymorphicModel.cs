namespace ZeroAlloc.Rest.Tools;

// Spec §5.5: an abstract record with [JsonPolymorphic] and one [JsonDerivedType] per variant.
internal sealed record PolymorphicModel(
    string Name,
    string? Description,
    string DiscriminatorWireName,
    EquatableList<PropertyModel> Properties,
    EquatableList<DerivedTypeModel> Variants)
    : ModelDefinition(Name, Description);
