namespace ZeroAlloc.Rest.Tools;

// A C# type the generated code uses: its name as emitted and what kind of type it is. Framework
// types are global::-qualified, because a model named from the spec may shadow them. Nullability is
// not part of it; TypeMapper.Declare adds the ? a property or parameter needs.
internal sealed record TypeRef(string Name, TypeRefKind Kind, bool IsValueType)
{
    internal static TypeRef Int { get; } = new("int", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Long { get; } = new("long", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Float { get; } = new("float", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Double { get; } = new("double", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Decimal { get; } = new("decimal", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Bool { get; } = new("bool", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef String { get; } = new("string", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef DateTimeOffset { get; } = new("global::System.DateTimeOffset", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef DateOnly { get; } = new("global::System.DateOnly", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef TimeOnly { get; } = new("global::System.TimeOnly", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Guid { get; } = new("global::System.Guid", TypeRefKind.Primitive, IsValueType: true);
    internal static TypeRef Uri { get; } = new("global::System.Uri", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef Bytes { get; } = new("byte[]", TypeRefKind.Primitive, IsValueType: false);
    internal static TypeRef Stream { get; } = new("global::System.IO.Stream", TypeRefKind.Stream, IsValueType: false);
    internal static TypeRef JsonElement { get; } = new("global::System.Text.Json.JsonElement", TypeRefKind.JsonElement, IsValueType: true);
}
