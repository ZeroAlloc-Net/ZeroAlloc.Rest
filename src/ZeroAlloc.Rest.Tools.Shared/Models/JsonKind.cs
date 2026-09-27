namespace ZeroAlloc.Rest.Tools;

// The JSON value kind a union variant accepts. Any is a variant the tool could not type, such as a
// nested union or a JsonElement, which accepts every kind.
internal enum JsonKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Any,
}
