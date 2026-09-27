using MemoryPack;
using Xunit;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.Tests.Serializers;

[MemoryPackable]
public partial record MemPackTestDto(string Name, int Age);

// Never passed to types.Add<T>(): every serializer here must refuse it.
[MemoryPackable]
public partial record MemPackUnregisteredDto(string Name);

public class MemoryPackSerializerTests
{
    private readonly MemoryPackRestSerializer _sut = new(types => types.Add<MemPackTestDto>());

    [Fact]
    public void ContentType_IsMemoryPack()
        => Assert.Equal("application/x-memorypack", _sut.ContentType);

    [Fact]
    public void Add_ReturnsSameBuilder_SoCallsChain()
    {
        MemoryPackRestTypes? seen = null;
        MemoryPackRestTypes? returned = null;
        _ = new MemoryPackRestSerializer(types =>
        {
            seen = types;
            returned = types.Add<MemPackTestDto>();
        });
        Assert.NotNull(seen);
        Assert.Same(seen, returned);
    }

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
    public async Task RoundTrip_RegisteredType()
    {
        var dto = new MemPackTestDto("Carol", 28);
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dto);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);
        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task RoundTrip_ArrayOfRegisteredType()
    {
        MemPackTestDto[] dtos = [new("Dan", 40), new("Eve", 41)];
        using var stream = new MemoryStream();
        await _sut.SerializeAsync(stream, dtos);
        stream.Position = 0;
        var result = await _sut.DeserializeAsync<MemPackTestDto[]>(stream);
        Assert.Equal(dtos, result);
    }

    [Fact]
    public async Task Deserialize_ReturnsNull_ForEmptyStream()
    {
        using var stream = new MemoryStream();
        var result = await _sut.DeserializeAsync<MemPackTestDto>(stream);
        Assert.Null(result);
    }

    [Fact]
    public async Task Deserialize_UnregisteredType_ThrowsNamingTypeAndFix()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeserializeAsync<MemPackUnregisteredDto>(stream).AsTask());
        Assert.Contains(nameof(MemPackUnregisteredDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("types.Add<", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serialize_UnregisteredType_ThrowsNamingTypeAndFix()
    {
        // Creating the instance runs the type's static constructor, which registers its formatter
        // with MemoryPack globally. The serializer must still refuse it: it was not added here.
        using var stream = new MemoryStream();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SerializeAsync(stream, new MemPackUnregisteredDto("Frank")).AsTask());
        Assert.Contains(nameof(MemPackUnregisteredDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("types.Add<", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task Deserialize_ArrayOfUnregisteredType_ThrowsNamingElementType()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeserializeAsync<MemPackUnregisteredDto[]>(stream).AsTask());
        Assert.Contains($"types.Add<{nameof(MemPackUnregisteredDto)}>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deserialize_TypeWithoutFormatter_Throws()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeserializeAsync<List<MemPackTestDto>>(stream).AsTask());
        Assert.Contains("List<MemPackTestDto>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoundTrip_BuiltInTypes_NeedNoRegistration()
    {
        var sut = new MemoryPackRestSerializer(_ => { });

        Assert.Equal(42, await RoundTripAsync(sut, 42));
        Assert.Equal("hello", await RoundTripAsync(sut, "hello"));
        Assert.Equal(new[] { 1, 2, 3 }, await RoundTripAsync(sut, new[] { 1, 2, 3 }));
        Assert.Equal(new Guid("5b1d3a52-1c4e-4c9b-9a44-5e3f1f2d7a10"),
            await RoundTripAsync(sut, new Guid("5b1d3a52-1c4e-4c9b-9a44-5e3f1f2d7a10")));
    }

    [Fact]
    public async Task ParameterlessConstructor_ServesBuiltInTypes()
    {
        var sut = new MemoryPackRestSerializer();

        Assert.Equal(42, await RoundTripAsync(sut, 42));
        Assert.Equal("hello", await RoundTripAsync(sut, "hello"));
    }

    [Fact]
    public async Task ParameterlessConstructor_RefusesMemoryPackableType_NamingTheFix()
    {
        var sut = new MemoryPackRestSerializer();
        using var stream = new MemoryStream([1, 2, 3, 4]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeserializeAsync<MemPackTestDto>(stream).AsTask());
        Assert.Contains($"new MemoryPackRestSerializer(types => types.Add<{nameof(MemPackTestDto)}>())", ex.Message, StringComparison.Ordinal);
    }

    private static async Task<T?> RoundTripAsync<T>(MemoryPackRestSerializer sut, T value)
    {
        using var stream = new MemoryStream();
        await sut.SerializeAsync(stream, value).ConfigureAwait(false);
        stream.Position = 0;
        return await sut.DeserializeAsync<T>(stream).ConfigureAwait(false);
    }
}
