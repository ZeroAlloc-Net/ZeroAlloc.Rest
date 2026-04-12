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
    public string? Value { get; init; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FormBodyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public sealed class SerializerAttribute(Type serializerType) : Attribute
{
    public Type SerializerType { get; } = serializerType;
}
