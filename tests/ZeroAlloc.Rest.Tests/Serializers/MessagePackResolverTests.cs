using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Xunit;
using ZeroAlloc.Rest.MessagePack;

namespace ZeroAlloc.Rest.Tests.Serializers;

// MessagePack's source generator fills this in with formatters for the [MessagePackObject] types in
// this assembly, MsgPackTestDto among them.
[GeneratedMessagePackResolver]
internal sealed partial class MsgPackTestResolver;

// The resolver constructor is the Native AOT path: it serializes through the caller's resolver and
// never falls back to MessagePackSerializerOptions.Standard.
public class MessagePackResolverTests
{
    private static readonly IFormatterResolver s_resolver = CompositeResolver.Create(
        Array.Empty<IMessagePackFormatter>(),
        [MsgPackTestResolver.Instance, BuiltinResolver.Instance]);

    [Fact]
    public async Task RoundTrip_ThroughSourceGeneratedResolver()
    {
        var sut = new MessagePackRestSerializer(s_resolver);
        var dto = new MsgPackTestDto { Name = "Dora", Age = 41 };
        using var stream = new MemoryStream();
        await sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await sut.DeserializeAsync<MsgPackTestDto>(stream);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result.Name);
        Assert.Equal(dto.Age, result.Age);
    }

    [Fact]
    public async Task TypeTheResolverDoesNotCover_Throws()
    {
        var sut = new MessagePackRestSerializer(s_resolver);
        using var stream = new MemoryStream();
        var ex = await Assert.ThrowsAsync<MessagePackSerializationException>(
            () => sut.SerializeAsync(stream, Uncovered()).AsTask());
        Assert.Contains("Queue", ex.ToString(), StringComparison.Ordinal);

        // StandardResolver, the fallback this constructor must not reach, does serialize it.
        using var standard = new MemoryStream();
        await new MessagePackRestSerializer(MessagePackSerializerOptions.Standard).SerializeAsync(standard, Uncovered());
        Assert.True(standard.Length > 0);
    }

    [Fact]
    public async Task Options_AreKept_WithTheResolverReplaced()
    {
        // Lz4BlockArray output starts with the ext header MessagePack writes for compressed data,
        // so the compression setting must survive the resolver swap.
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
        var sut = new MessagePackRestSerializer(s_resolver, options);
        var dto = new MsgPackTestDto { Name = new string('x', 200), Age = 3 };

        using var compressed = new MemoryStream();
        await sut.SerializeAsync(compressed, dto);
        using var plain = new MemoryStream();
        await new MessagePackRestSerializer(s_resolver).SerializeAsync(plain, dto);
        Assert.True(compressed.Length < plain.Length);

        compressed.Position = 0;
        var result = await sut.DeserializeAsync<MsgPackTestDto>(compressed);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result.Name);

        // The options passed in still have their own resolver; only the serializer's copy changed.
        Assert.Same(StandardResolver.Instance, options.Resolver);
        using var uncovered = new MemoryStream();
        await Assert.ThrowsAsync<MessagePackSerializationException>(
            () => sut.SerializeAsync(uncovered, Uncovered()).AsTask());
    }

    // StandardResolver builds a Queue<T> formatter through DynamicGenericResolver, with MakeGenericType;
    // neither the source-generated resolver nor BuiltinResolver has one.
    private static Queue<MsgPackTestDto> Uncovered() => new([new MsgPackTestDto { Name = "Eve", Age = 9 }]);

    [Fact]
    public void NullResolver_Throws()
        => Assert.Throws<ArgumentNullException>("resolver", () => new MessagePackRestSerializer(null!, null));
}
