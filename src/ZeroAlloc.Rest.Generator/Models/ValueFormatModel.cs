namespace ZeroAlloc.Rest.Generator.Models;

internal enum ValueFormat { Text, String, Boolean, Iso8601, Invariant, Enum }

// How a route, query or header value is written into the request. TypeName is the value's type,
// or a collection's element type, without Nullable<T>, fully qualified. EnumMembers pairs each
// member that has a [JsonStringEnumMemberName] with that name. ElementIsNullable is true only for
// a collection whose element type can hold null, a reference type or Nullable<T>, so the per-item
// null check is emitted only where it compiles.
internal sealed record ValueFormatModel(
    string TypeName,
    bool IsValueType,
    ValueFormat Format,
    EquatableArray<(string Member, string Wire)> EnumMembers,
    bool ElementIsNullable = false);
