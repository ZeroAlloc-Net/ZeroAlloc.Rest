using MemoryPack;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.Tests.Serializers;

[MemoryPackable]
public partial record MemPackTestDto(string Name, int Age);

public class MemoryPackSerializerTests
{
    private readonly MemoryPackRestSerializer _sut = new();

    [Fact]
    public void ContentType_IsMemoryPack()
        => Assert.Equal("application/x-memorypack", _sut.ContentType);

    [Fact]
    public async Task Serialize_WritesBytes()
    {
        var dto = new MemPackTestDto("Alice", 30);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task Deserialize_ReadsBytes()
    {
        var dto = new MemPackTestDto("Bob", 25);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);
        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new MemPackTestDto("Carol", 28);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);
        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task Deserialize_ReturnsNull_ForEmptyStream()
    {
        using var stream = new MemoryStream();
        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);
        Assert.Null(result);
    }
}
