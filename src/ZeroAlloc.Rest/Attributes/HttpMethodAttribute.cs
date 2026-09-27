namespace ZeroAlloc.Rest.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public abstract class HttpMethodAttribute(string method, string route) : Attribute
{
    /// <summary>
    /// An empty route sends the request to the <see cref="System.Net.Http.HttpClient"/>'s
    /// BaseAddress itself. Used by every method attribute's parameterless constructor.
    /// </summary>
    protected HttpMethodAttribute(string method) : this(method, string.Empty) { }

    public string Method { get; } = method;
    public string Route { get; } = route;
}

public sealed class GetAttribute : HttpMethodAttribute
{
    public GetAttribute() : base("GET") { }
    public GetAttribute(string route) : base("GET", route) { }
}

public sealed class PostAttribute : HttpMethodAttribute
{
    public PostAttribute() : base("POST") { }
    public PostAttribute(string route) : base("POST", route) { }
}

public sealed class PutAttribute : HttpMethodAttribute
{
    public PutAttribute() : base("PUT") { }
    public PutAttribute(string route) : base("PUT", route) { }
}

public sealed class PatchAttribute : HttpMethodAttribute
{
    public PatchAttribute() : base("PATCH") { }
    public PatchAttribute(string route) : base("PATCH", route) { }
}

public sealed class DeleteAttribute : HttpMethodAttribute
{
    public DeleteAttribute() : base("DELETE") { }
    public DeleteAttribute(string route) : base("DELETE", route) { }
}
