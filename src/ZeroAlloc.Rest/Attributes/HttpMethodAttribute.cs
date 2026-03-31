namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public abstract class HttpMethodAttribute(string method, string route) : Attribute
{
    public string Method { get; } = method;
    public string Route { get; } = route;
}

public sealed class GetAttribute(string route) : HttpMethodAttribute("GET", route) { }
public sealed class PostAttribute(string route) : HttpMethodAttribute("POST", route) { }
public sealed class PutAttribute(string route) : HttpMethodAttribute("PUT", route) { }
public sealed class PatchAttribute(string route) : HttpMethodAttribute("PATCH", route) { }
public sealed class DeleteAttribute(string route) : HttpMethodAttribute("DELETE", route) { }
