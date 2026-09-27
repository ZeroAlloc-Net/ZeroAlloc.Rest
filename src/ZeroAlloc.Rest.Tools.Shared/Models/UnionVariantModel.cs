namespace ZeroAlloc.Rest.Tools;

// RequiredWireNames, sorted, decide whether a JSON object can be this variant; empty unless Kind
// is Object.
internal sealed record UnionVariantModel(string Name, TypeRef Type, JsonKind Kind, EquatableList<string> RequiredWireNames);
