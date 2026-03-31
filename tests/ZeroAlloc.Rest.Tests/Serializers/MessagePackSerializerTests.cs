using MessagePack;
using Xunit;
using ZeroAlloc.Rest.MessagePack;

namespace ZeroAlloc.Rest.Tests.Serializers;

[MessagePackObject]
public sealed class MsgPackTestDto
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int Age { get; set; }
}

public class MessagePackSerializerTests
{
    private readonly MessagePackRestSerializer _sut = new();

    [Fact]
    public void ContentType_IsMessagePack()
        => Assert.Equal("application/x-msgpack", _sut.ContentType);

    [Fact]
    public async Task Serialize_WritesBytes()
    {
        var dto = new MsgPackTestDto { Name = "Alice", Age = 30 };
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task Deserialize_ReadsBytes()
    {
        var dto = new MsgPackTestDto { Name = "Bob", Age = 25 };
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MsgPackTestDto>(stream);
        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new MsgPackTestDto { Name = "Carol", Age = 28 };
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MsgPackTestDto>(stream);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result.Name);
        Assert.Equal(dto.Age, result.Age);
    }

    [Fact]
    public void Constructor_AcceptsCustomOptions()
    {
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        var serializer = new MessagePackRestSerializer(options);
        Assert.NotNull(serializer);
    }
}
