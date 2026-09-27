namespace ZeroAlloc.Rest.Tools;

// A type the tool generates. The intermediate model holds no source text, so it is unit-tested
// on its own, and the emitters are pure functions of it.
internal abstract record ModelDefinition(string Name, string? Description);
