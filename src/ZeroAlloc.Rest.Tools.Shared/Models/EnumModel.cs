namespace ZeroAlloc.Rest.Tools;

// A string enum, written by name, or an integer enum, written by value. UnderlyingType is int or long.
internal sealed record EnumModel(string Name, string? Description, bool IsString, string UnderlyingType, EquatableList<EnumMemberModel> Members)
    : ModelDefinition(Name, Description);
