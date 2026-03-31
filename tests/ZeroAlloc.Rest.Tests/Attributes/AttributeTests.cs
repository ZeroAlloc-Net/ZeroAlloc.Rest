using Xunit;
using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.Rest.Tests.Attributes;

public class AttributeTests
{
    [Fact]
    public void ZeroAllocRestClientAttribute_IsAnAttribute()
    {
        var attr = new ZeroAllocRestClientAttribute();
        Assert.IsAssignableFrom<Attribute>(attr);
    }

    [Fact]
    public void GetAttribute_StoresRoute()
    {
        var attr = new GetAttribute("/users/{id}");
        Assert.Equal("/users/{id}", attr.Route);
        Assert.Equal("GET", attr.Method);
    }

    [Fact]
    public void PostAttribute_StoresRoute()
    {
        var attr = new PostAttribute("/users");
        Assert.Equal("/users", attr.Route);
        Assert.Equal("POST", attr.Method);
    }

    [Fact]
    public void PutAttribute_StoresRoute()
    {
        var attr = new PutAttribute("/users/{id}");
        Assert.Equal("PUT", attr.Method);
    }

    [Fact]
    public void PatchAttribute_StoresRoute()
    {
        var attr = new PatchAttribute("/users/{id}");
        Assert.Equal("PATCH", attr.Method);
    }

    [Fact]
    public void DeleteAttribute_StoresRoute()
    {
        var attr = new DeleteAttribute("/users/{id}");
        Assert.Equal("DELETE", attr.Method);
    }

    [Fact]
    public void HeaderAttribute_StoresName()
    {
        var attr = new HeaderAttribute("X-Api-Key");
        Assert.Equal("X-Api-Key", attr.Name);
    }

    [Fact]
    public void SerializerAttribute_StoresType()
    {
        var attr = new SerializerAttribute(typeof(object));
        Assert.Equal(typeof(object), attr.SerializerType);
    }
}
