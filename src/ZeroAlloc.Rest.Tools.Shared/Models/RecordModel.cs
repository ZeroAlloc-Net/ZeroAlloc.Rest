namespace ZeroAlloc.Rest.Tools;

// A public sealed record. BaseName is set for a variant of a discriminated hierarchy.
internal sealed record RecordModel(string Name, string? Description, EquatableList<PropertyModel> Properties, string? BaseName = null)
    : ModelDefinition(Name, Description);
