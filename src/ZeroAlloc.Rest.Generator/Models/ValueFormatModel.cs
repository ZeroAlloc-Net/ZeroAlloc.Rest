namespace ZeroAlloc.Rest.Generator.Models;

// Invariant calls the type's public ToString(string, IFormatProvider); ExplicitInvariant is for a type
// that implements IFormattable explicitly, and calls it through an IFormattable-constrained helper.
internal enum ValueFormat { Text, String, Boolean, Iso8601, Invariant, ExplicitInvariant, Enum }

// How a route, query or header value is written into the request. TypeName is the value's type,
// or a collection's element type, without Nullable<T>, fully qualified. ElementIsNullable is true
// only for a collection whose element type can hold null, a reference type or Nullable<T>, so the
// per-item null check is emitted only where it compiles.
//
// For an enum, EnumMembers holds one member per distinct value with the name System.Text.Json
// writes for it: its [JsonStringEnumMemberName], or else its C# name. They are in the order STJ's
// JsonStringEnumConverter walks them when it writes a [Flags] combination: most bits set first,
// then by ascending value. EnumUnderlyingType is the enum's underlying type, for writing a value
// that no member or member combination names as a number, as STJ does.
internal sealed record ValueFormatModel(
    string TypeName,
    bool IsValueType,
    ValueFormat Format,
    EquatableArray<EnumMemberModel> EnumMembers,
    bool ElementIsNullable = false,
    bool IsFlags = false,
    string? EnumUnderlyingType = null);

// Key is the member's value as STJ compares it: sign-extended to ulong.
internal sealed record EnumMemberModel(string Member, string Wire, ulong Key);
