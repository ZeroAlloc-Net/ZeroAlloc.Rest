using System.Text;
using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.SystemTextJson;

namespace ZeroAlloc.Rest.Tests.Serializers;

file record TestDto(string Name, int Age);

public class SystemTextJsonSerializerTests
{
    private readonly SystemTextJsonSerializer _sut = new();

    [Fact]
    public void ContentType_IsApplicationJson()
        => Assert.Equal("application/json", _sut.ContentType);

    [Fact]
    public async Task Serialize_WritesValidJson()
    {
        var dto = new TestDto("Alice", 30);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();
        Assert.Contains("\"Alice\"", json);
        Assert.Contains("30", json);
    }

    [Fact]
    public async Task Deserialize_ReadsValidJson()
    {
        var json = """{"Name":"Bob","Age":25}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task RoundTrip_SerializeDeserialize()
    {
        var dto = new TestDto("Carol", 28);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task Deserialize_ReturnsNull_ForEmptyStream()
    {
        using var stream = new MemoryStream();
        var result = await _sut.DeserializeAsync<TestDto>(stream);
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_AcceptsCustomOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var serializer = new SystemTextJsonSerializer(options);
        Assert.NotNull(serializer);
    }
}
