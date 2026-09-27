namespace ZeroAlloc.Rest.Tools;

internal sealed record PropertyModel(string Name, string WireName, TypeRef Type, bool Required, bool Nullable, string? Description);
