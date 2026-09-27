using System.Net;
using System.Net.Http;
using System.Text;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

public sealed record Thing(string Name);

[ZeroAllocRestClient]
public interface IEmptyBodyApi
{
    [Get("/thing")]
    Task<Result<Thing, HttpError>> GetResultAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Result<Thing?, HttpError>> GetMaybeResultAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<Result<int, HttpError>> GetCountResultAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<Result<int?, HttpError>> GetMaybeCountResultAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Thing> GetAsync(CancellationToken ct = default);

    [Get("/thing")]
    Task<Thing?> GetMaybeAsync(CancellationToken ct = default);

    [Get("/count")]
    Task<int> GetCountAsync(CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedEmptyBodyApi
{
    [Get("/thing")]
    Task<Result<Thing, DomainError>> GetAsync(CancellationToken ct = default);
}

// A 204, or a body of JSON null, has no value. A success type that accepts null gets null; one that
// does not gets a Deserialization failure, or an exception from a Task<T> method, never a null
// disguised as T.
public sealed class EmptyBodyTests
{
    public static TheoryData<HttpStatusCode, string?> EmptyBodies => new()
    {
        { HttpStatusCode.NoContent, null },
        { HttpStatusCode.OK, "null" },
    };

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task Result_NonNullableReference_IsADeserializationFailure(HttpStatusCode status, string? body)
    {
        var result = await Api(status, body).GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Equal(status, result.Error.StatusCode);
        Assert.Contains("empty or null", result.Error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(result.Error.Exception);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task Result_NullableReference_IsASuccessWithNull(HttpStatusCode status, string? body)
    {
        var result = await Api(status, body).GetMaybeResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Result_NonNullableValue_EmptyBody_IsADeserializationFailure()
    {
        var result = await Api(HttpStatusCode.NoContent, null).GetCountResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
        Assert.Contains("empty or null", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task Result_NullableValue_IsASuccessWithNull(HttpStatusCode status, string? body)
    {
        var result = await Api(status, body).GetMaybeCountResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task Task_NonNullableReference_Throws(HttpStatusCode status, string? body)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(status, body).GetAsync());

        Assert.Contains("GetAsync", error.Message, StringComparison.Ordinal);
        Assert.Contains("Thing?", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task Task_NullableReference_ReturnsNull(HttpStatusCode status, string? body)
        => Assert.Null(await Api(status, body).GetMaybeAsync());

    [Fact]
    public async Task Task_NonNullableValue_EmptyBody_Throws()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(HttpStatusCode.NoContent, null).GetCountAsync());

        Assert.Contains("GetCountAsync", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmptyBodies))]
    public async Task MappedResult_NonNullableReference_MapsADeserializationFailure(HttpStatusCode status, string? body)
    {
        using var http = Client(status, body);
        IMappedEmptyBodyApi api = new MappedEmptyBodyApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.GetAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Deserialization, result.Error.Kind);
    }

    [Fact]
    public async Task Bodies_StillRead()
    {
        var api = Api(HttpStatusCode.OK, "{\"name\":\"Rex\"}");

        Assert.Equal("Rex", (await api.GetResultAsync()).Value.Name);
        Assert.Equal("Rex", (await api.GetAsync()).Name);
    }

    private static IEmptyBodyApi Api(HttpStatusCode status, string? body)
        => new EmptyBodyApiClient(Client(status, body), new SystemTextJsonSerializer());

    private static HttpClient Client(HttpStatusCode status, string? body)
        => new(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = body is null ? new ByteArrayContent([]) : new StringContent(body, Encoding.UTF8, "application/json"),
        })))
        { BaseAddress = ResultErrorMapperTests.BaseAddress };
}
