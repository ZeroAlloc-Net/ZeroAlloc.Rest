using System.Net;
using System.Net.Http;
using System.Text;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;
using Xunit;

namespace ZeroAlloc.Rest.Integration.Tests;

[ZeroAllocRestClient]
public interface IUnitApi
{
    [Delete("/things/{id}")]
    Task<UnitResult<HttpError>> DeleteThingAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedUnitApi
{
    [Delete("/things/{id}")]
    Task<UnitResult<DomainError>> DeleteThingAsync(int id, CancellationToken ct = default);
}

public sealed class UnitResultTests
{
    [Theory]
    [InlineData(HttpStatusCode.NoContent, "")]
    [InlineData(HttpStatusCode.OK, "{ not json")]
    public async Task Success_ReadsNoBody(HttpStatusCode status, string body)
    {
        using var http = Client((_, _) => Respond(status, body));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Status_IsAFailureWithTheBody()
    {
        using var http = Client((_, _) => Respond(HttpStatusCode.NotFound, "gone"));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
        Assert.Equal("gone", Encoding.UTF8.GetString(result.Error.Body.Span));
    }

    [Fact]
    public async Task Transport_IsAFailure()
    {
        using var http = Client((_, _) => throw new HttpRequestException("refused"));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        var result = await api.DeleteThingAsync(1);

        Assert.Equal(HttpErrorKind.Transport, result.Error.Kind);
    }

    [Fact]
    public async Task CallerCancellation_StillThrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = Client((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        IUnitApi api = new UnitApiClient(http, new SystemTextJsonSerializer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.DeleteThingAsync(1, cts.Token));
    }

    [Fact]
    public async Task MappedStatus_ReachesTheMapper()
    {
        using var http = Client((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson));
        IMappedUnitApi api = new MappedUnitApiClient(http, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await api.DeleteThingAsync(1);

        Assert.Equal(
            new DomainError(HttpErrorKind.Status, HttpStatusCode.UnprocessableEntity, "field_required", "library"),
            result.Error);
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = ResultErrorMapperTests.BaseAddress };

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body)
        => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}
