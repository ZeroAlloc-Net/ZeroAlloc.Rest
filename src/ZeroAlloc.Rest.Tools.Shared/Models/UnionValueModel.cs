namespace ZeroAlloc.Rest.Tools;

// Issue #360: a property of an object variant whose schema is a single-value enum. A JSON object
// in which the property holds another value is not this variant. Kind is String, Number or
// Boolean; Value is the string itself, the integer in invariant digits, or true or false.
internal sealed record UnionValueModel(string WireName, JsonKind Kind, string Value);
