namespace ZeroAlloc.Rest.Tools;

// WireValue is the string sent for a string enum, or the invariant integer literal for an integer one.
internal sealed record EnumMemberModel(string Name, string WireValue);
