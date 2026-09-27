using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;
using ZeroAlloc.Rest.MessagePack;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Rest 3.0: the serializer contract is AOT-neutral, so a generated client calls it without a trim
// suppression. The risk sits where reflection is chosen: the reflection-based constructors.
public class SerializerAotAnnotationTests
{
    public static TheoryData<Type> Serializers => new()
    {
        typeof(IRestSerializer),
        typeof(SystemTextJsonSerializer),
        typeof(MemoryPackRestSerializer),
        typeof(MessagePackRestSerializer),
        typeof(RestSerializerAdapter<string>),
    };

    [Theory]
    [MemberData(nameof(Serializers))]
    public void SerializerMethods_CarryNoTrimAnnotations(Type type)
    {
        foreach (var name in new[] { "SerializeAsync", "DeserializeAsync" })
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Null(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
            Assert.Null(method.GetCustomAttribute<RequiresDynamicCodeAttribute>());
        }
    }

    // A lone Type[] argument would bind to InlineData's params array itself, so the case is a bool.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectionBasedJsonConstructors_AreAnnotated(bool withOptions)
    {
        var constructor = typeof(SystemTextJsonSerializer).GetConstructor(
            withOptions ? [typeof(JsonSerializerOptions)] : Type.EmptyTypes);
        Assert.NotNull(constructor);
        Assert.NotNull(constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        Assert.NotNull(constructor.GetCustomAttribute<RequiresDynamicCodeAttribute>());
    }

    [Theory]
    [InlineData(typeof(System.Text.Json.Serialization.JsonSerializerContext))]
    [InlineData(typeof(System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver))]
    public void ContextAndResolverConstructors_AreNotAnnotated(Type parameter)
    {
        var constructor = typeof(SystemTextJsonSerializer).GetConstructors()
            .Single(c => c.GetParameters() is [var first, ..] && first.ParameterType == parameter);
        Assert.Null(constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
        Assert.Null(constructor.GetCustomAttribute<RequiresDynamicCodeAttribute>());
    }
}
