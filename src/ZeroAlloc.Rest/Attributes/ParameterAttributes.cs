namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class QueryAttribute : Attribute
{
    public string? Name { get; init; }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HeaderAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    /// <summary>
    /// When set on a <b>method</b>, emits a compile-time static header with this value.
    /// When used on a <b>parameter</b>, this property is ignored — the parameter value is used at runtime.
    /// If <see cref="Value"/> is <see langword="null"/> on a method-level attribute, the attribute is silently ignored.
    /// </summary>
    public string? Value { get; init; }
}

/// <summary>
/// Marks a parameter as the form-encoded body of the request.
/// The parameter type must be assignable to <see cref="System.Collections.Generic.IEnumerable{T}"/> of
/// <see cref="System.Collections.Generic.KeyValuePair{TKey,TValue}"/> (e.g. <c>Dictionary&lt;string, string&gt;</c>).
/// The generated client emits <c>FormUrlEncodedContent</c> directly — no serializer is used.
/// Only one of <see cref="BodyAttribute"/> or <see cref="FormBodyAttribute"/> may appear on a single method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FormBodyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public sealed class SerializerAttribute(Type serializerType) : Attribute
{
    public Type SerializerType { get; } = serializerType;
}
