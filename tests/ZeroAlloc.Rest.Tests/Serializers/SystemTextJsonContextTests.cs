using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

// Spec §7: a context or resolver constructor serializes through JsonTypeInfo<T> only, and never
// falls back to reflection.
public class SystemTextJsonContextTests
{
    public static TheoryData<string> Constructions => new() { "context", "resolver", "resolver with options" };

    private static SystemTextJsonSerializer Create(string construction) => construction switch
    {
        "context" => new SystemTextJsonSerializer(WidgetJsonContext.Default),
        "resolver" => new SystemTextJsonSerializer((IJsonTypeInfoResolver)WidgetJsonContext.Default),
        _ => new SystemTextJsonSerializer(WidgetJsonContext.Default, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    [Theory]
    [MemberData(nameof(Constructions))]
    public async Task RegisteredType_RoundTrips(string construction)
    {
        var serializer = Create(construction);
        using var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, new Widget(7, "gear"));
        stream.Position = 0;
        var json = Encoding.UTF8.GetString(stream.ToArray());
        var read = await serializer.DeserializeAsync<Widget>(stream);

        Assert.Equal("""{"id":7,"name":"gear"}""", json);
        Assert.Equal(new Widget(7, "gear"), read);
    }

    [Theory]
    [MemberData(nameof(Constructions))]
    public async Task UnregisteredType_ThrowsNamingTheTypeAndTheContext(string construction)
    {
        var serializer = Create(construction);
        using var stream = new MemoryStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => serializer.SerializeAsync(stream, new Unregistered(1)).AsTask());

        Assert.Contains("ZeroAlloc.Rest.Tests.Serializers.Unregistered", error.Message, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(Unregistered))]", error.Message, StringComparison.Ordinal);
        Assert.Contains("generated JsonContext", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyBody_DeserializesToDefault()
    {
        var serializer = new SystemTextJsonSerializer(WidgetJsonContext.Default);
        using var stream = new MemoryStream();

        Assert.Null(await serializer.DeserializeAsync<Widget>(stream));
    }

    [Fact]
    public void ResolverConstructor_DoesNotLockTheCallersOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _ = new SystemTextJsonSerializer(WidgetJsonContext.Default, options);

        Assert.False(options.IsReadOnly);
    }
}
