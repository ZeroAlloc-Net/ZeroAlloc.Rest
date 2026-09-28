namespace ZeroAlloc.Rest.Tools;

// RequiredWireNames, sorted, and Values, sorted by wire name then value, decide whether a JSON
// object can be this variant; both are empty unless Kind is Object.
internal sealed record UnionVariantModel(
    string Name, TypeRef Type, JsonKind Kind, EquatableList<string> RequiredWireNames, EquatableList<UnionValueModel> Values);
