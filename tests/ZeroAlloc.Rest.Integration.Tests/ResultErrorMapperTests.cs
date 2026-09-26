using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Rest.Integration.Tests.TestInterfaces;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Rest.Integration.Tests;

// Issue #300: a method returning Result<T, TError> maps every failure through the interface's
// [ErrorMapper]. A mapper's own exception propagates unchanged and is never mapped again.

public sealed record DomainError(HttpErrorKind Kind, HttpStatusCode Status, string Code, string Source);

public sealed class DomainErrorMapper : IHttpErrorMapper<DomainError>
{
    public DomainError Map(HttpError error) => new(error.Kind, error.StatusCode, ReadCode(error), "library");

    private static string ReadCode(HttpError error)
    {
        if (error.Body.IsEmpty || error.BodyTruncated)
            return "none";
        try
        {
            using var document = JsonDocument.Parse(error.Body);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "none" : "none";
        }
        catch (JsonException)
        {
            return "unparsable";
        }
    }
}

public sealed class ThrowingMapper : IHttpErrorMapper<DomainError>
{
    public int Calls { get; private set; }

    // An HttpRequestException by default, on purpose: if the mapper ran inside the method's try,
    // the Transport catch would take it and map it a second time. An OperationCanceledException the
    // caller did not ask for would be taken by the Timeout catch the same way.
    public Exception ToThrow { get; init; } = new HttpRequestException("mapper broke");

    public DomainError Map(HttpError error)
    {
        Calls++;
        throw ToThrow;
    }
}

// Declared over DomainError? while the method returns a non-nullable DomainError. This project
// builds with TreatWarningsAsErrors, so the generated client compiling here is part of the test.
public sealed class NullReturningMapper : IHttpErrorMapper<DomainError?>
{
    public DomainError? Map(HttpError error) => null;
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(DomainErrorMapper))]
public interface IMappedApi
{
    [Get("/things/{id}")]
    Task<Result<UserDto, DomainError>> GetThingAsync(int id, CancellationToken ct = default);

    [Get("/things/{id}/plain")]
    Task<Result<UserDto, HttpError>> GetPlainThingAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(ThrowingMapper))]
public interface IThrowingMapperApi
{
    [Get("/things/{id}")]
    Task<Result<UserDto, DomainError>> GetThingAsync(int id, CancellationToken ct = default);
}

[ZeroAllocRestClient]
[ErrorMapper(typeof(NullReturningMapper))]
public interface INullReturningMapperApi
{
    [Get("/things/{id}")]
    Task<Result<UserDto, DomainError>> GetThingAsync(int id, CancellationToken ct = default);
}

public sealed class ResultErrorMapperTests
{
    internal const string ProblemJson = """{"code":"field_required","field":"name"}""";

    internal static readonly Uri BaseAddress = new("http://stub.local/");

    [Fact]
    public async Task StatusError_BodyIsMappedToTheDomainError()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new DomainError(HttpErrorKind.Status, HttpStatusCode.UnprocessableEntity, "field_required", "library"),
            result.Error);
    }

    [Fact]
    public async Task Timeout_IsMapped()
    {
        using var httpClient = CreateHttpClient(HangAsync, TimeSpan.FromMilliseconds(50));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Timeout, (HttpStatusCode)0, "none", "library"), result.Error);
    }

    [Fact]
    public async Task TransportError_IsMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => throw new HttpRequestException("refused"));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Transport, (HttpStatusCode)0, "none", "library"), result.Error);
    }

    [Fact]
    public async Task DeserializationError_IsMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, "{ not json"));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.Equal(new DomainError(HttpErrorKind.Deserialization, HttpStatusCode.OK, "none", "library"), result.Error);
    }

    [Fact]
    public async Task Success_PassesThrough()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.OK, """{"id":1,"name":"Ada"}"""));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetThingAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada", result.Value.Name);
    }

    [Fact]
    public async Task HttpErrorMethod_OnTheSameInterface_IsNotMapped()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        IMappedApi client = new MappedApiClient(httpClient, new SystemTextJsonSerializer(), new DomainErrorMapper());

        var result = await client.GetPlainThingAsync(1);

        Assert.Equal(HttpErrorKind.Status, result.Error.Kind);
        Assert.Equal(Encoding.UTF8.GetBytes(ProblemJson), result.Error.Body.ToArray());
    }

    [Fact]
    public async Task CallerCancellation_StillThrows_AndIsNotMapped()
    {
        using var httpClient = CreateHttpClient(HangAsync);
        var mapper = new ThrowingMapper();
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), mapper);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetThingAsync(1, cts.Token));
        Assert.Equal(0, mapper.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ThrowingMapper_ExceptionPropagates_AndIsNotMappedAgain(bool transportFailure, bool mapperThrowsCancellation)
    {
        using var httpClient = CreateHttpClient((_, _) => transportFailure
            ? throw new HttpRequestException("refused")
            : Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        Exception toThrow = mapperThrowsCancellation
            ? new OperationCanceledException("mapper cancelled")
            : new HttpRequestException("mapper broke");
        var mapper = new ThrowingMapper { ToThrow = toThrow };
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), mapper);

        // The caller's token is never cancelled.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetThingAsync(1, CancellationToken.None));

        Assert.Same(toThrow, ex);
        Assert.Equal(1, mapper.Calls);
    }

    [Fact]
    public async Task MapperReturningNull_ForANonNullableErrorType_Throws()
    {
        using var httpClient = CreateHttpClient((_, _) => Respond(HttpStatusCode.UnprocessableEntity, ProblemJson));
        INullReturningMapperApi client = new NullReturningMapperApiClient(httpClient, new SystemTextJsonSerializer(), new NullReturningMapper());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetThingAsync(1));

        Assert.Contains("returned null", ex.Message, StringComparison.Ordinal);
    }

    internal static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHandler(send)) { BaseAddress = BaseAddress };
        if (timeout is { } t)
            httpClient.Timeout = t;
        return httpClient;
    }

    internal static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });

    private static async Task<HttpResponseMessage> HangAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        throw new InvalidOperationException("unreachable");
    }
}

// Process-wide ActivityListener: kept out of parallel runs, like TelemetryTests.
[Collection("rest-telemetry-non-parallel")]
public sealed class ResultErrorMapperTelemetryTests
{
    [Fact]
    public async Task ThrowingMapper_MarksTheSpanFailed()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "ZeroAlloc.Rest", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                    stopped.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var httpClient = ResultErrorMapperTests.CreateHttpClient((_, _) =>
            ResultErrorMapperTests.Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson));
        IThrowingMapperApi client = new ThrowingMapperApiClient(httpClient, new SystemTextJsonSerializer(), new ThrowingMapper());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetThingAsync(1));

        var span = Assert.Single(stopped, a => string.Equals(a.DisplayName, "IThrowingMapperApi.GetThingAsync", StringComparison.Ordinal));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("mapper broke", span.StatusDescription);
    }
}

public sealed class HostDomainErrorMapper : IHttpErrorMapper<DomainError>
{
    public DomainError Map(HttpError error) => new(error.Kind, error.StatusCode, "host", "host");
}

public sealed class ResultErrorMapperDiTests
{
    [Fact]
    public void Add_RegistersTheMapper()
    {
        var services = new ServiceCollection();
        services.AddIMappedApi(o => o.UseSerializer(new SystemTextJsonSerializer()));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<DomainErrorMapper>());
        Assert.IsType<MappedApiClient>(provider.GetRequiredService<IMappedApi>());
    }

    [Fact]
    public async Task HostRegistrationOfTheMapperInterface_DoesNotReplaceTheLibraryMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpErrorMapper<DomainError>, HostDomainErrorMapper>();
        services.AddIMappedApi(o =>
            {
                o.BaseAddress = ResultErrorMapperTests.BaseAddress;
                o.UseSerializer(new SystemTextJsonSerializer());
            })
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                ResultErrorMapperTests.Respond(HttpStatusCode.UnprocessableEntity, ResultErrorMapperTests.ProblemJson)));

        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IMappedApi>().GetThingAsync(1);

        Assert.True(result.IsFailure);
        Assert.Equal("library", result.Error.Source);
        Assert.Equal("field_required", result.Error.Code);
    }
}
