using System;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest;
using ZeroAlloc.Rest.AotSmoke;
using ZeroAlloc.Rest.Resilience;

// Verify that generator-emitted clients compile, publish and run under PublishAot=true with no
// trim or AOT warning. ILC analyses every emitted proxy; the smoke also resolves clients through
// DI, drives the Result paths through in-process handlers, and fires real HTTP requests over a
// loopback socket through a client generated from petstore.yaml, serializing through its
// generated System.Text.Json context.

if (typeof(IUserApi) is null)
{
    Console.Error.WriteLine("AOT smoke: FAIL — IUserApi type should be resolvable");
    return 1;
}

// The generator emits a UserApiClient implementation. Its existence in the
// compiled assembly is what ILC analyses; referencing it here forces the
// linker to keep the type alive during trim.
var clientType = Type.GetType("ZeroAlloc.Rest.AotSmoke.UserApiClient");
if (clientType is null)
{
    Console.Error.WriteLine("AOT smoke: FAIL — generator-emitted UserApiClient type not found");
    return 1;
}

if (!typeof(IUserApi).IsAssignableFrom(clientType))
{
    Console.Error.WriteLine("AOT smoke: FAIL — UserApiClient should implement IUserApi");
    return 1;
}

// Resolve clients under ILC through both entry points: the generated Add{I} with a per-client
// UseSerializer instance, and the Resilience bridge for an interface-level [Serializer] with no
// manual registration and for a client on the app-wide default.
var services = new ServiceCollection();
services.AddRestSerializer<SmokeSerializer>();
services.AddIUserApi(o =>
{
    o.BaseAddress = new Uri("http://localhost/");
    o.UseSerializer(new SmokeSerializer());
});
services.AddIQuoteApi(o =>
{
    o.BaseAddress = new Uri("http://localhost/");
    o.UseSerializer(new SmokeSerializer());
});
services.AddOrderApiResiliencePolicies();
services.AddRestResilience<IOrderApi, OrderApiClient, IOrderApiResilienceProxy>(
    (inner, sp) => new IOrderApiResilienceProxy(inner, sp.GetRequiredService<OrderApiResiliencePolicies>()),
    o => o.BaseAddress = new Uri("http://localhost/"));
services.AddStatusApiResiliencePolicies();
services.AddRestResilience<IStatusApi, StatusApiClient, IStatusApiResilienceProxy>(
    (inner, sp) => new IStatusApiResilienceProxy(inner, sp.GetRequiredService<StatusApiResiliencePolicies>()),
    o => o.BaseAddress = new Uri("http://localhost/"));
using (var provider = services.BuildServiceProvider())
{
    if (provider.GetRequiredService<IUserApi>() is not UserApiClient)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — IUserApi should resolve to UserApiClient");
        return 1;
    }

    if (provider.GetRequiredService<IOrderApi>() is not IOrderApiResilienceProxy)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — IOrderApi should resolve to its resilience proxy");
        return 1;
    }

    if (provider.GetRequiredService<IStatusApi>() is not IStatusApiResilienceProxy)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — IStatusApi should resolve to its resilience proxy");
        return 1;
    }

    if (provider.GetRequiredService<IQuoteApi>() is not QuoteApiClient)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — IQuoteApi should resolve to QuoteApiClient");
        return 1;
    }
}

// A Result method returns a transport failure instead of throwing. No network is involved: the
// handler throws as a refused connection would.
using (var failingHttp = new System.Net.Http.HttpClient(new RefusingHandler()) { BaseAddress = new Uri("http://localhost/") })
{
    IUserApi failing = new UserApiClient(failingHttp, new SmokeSerializer());
    var result = await failing.TryGetUserAsync(1).ConfigureAwait(false);
    if (!result.IsFailure || result.Error.Kind != HttpErrorKind.Transport || result.Error.Exception is null)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a transport failure should return HttpErrorKind.Transport");
        return 1;
    }
}

// A non-success status carries its body: the generated client reads it through
// GeneratedRestClient.ReadErrorBodyAsync before disposing the response.
using (var rejectingHttp = new System.Net.Http.HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("http://localhost/") })
{
    IUserApi rejecting = new UserApiClient(rejectingHttp, new SmokeSerializer());
    var result = await rejecting.TryGetUserAsync(1).ConfigureAwait(false);
    if (!result.IsFailure
        || result.Error.Kind != HttpErrorKind.Status
        || result.Error.Body.Length != UnprocessableHandler.Body.Length
        || !string.Equals(result.Error.ContentType, "application/problem+json", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 422 should carry its body and media type");
        return 1;
    }
}

// A mapped error type: the 422 body and a refused connection both reach the mapper.
using (var rejectingHttp = new System.Net.Http.HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("http://localhost/") })
using (var refusingHttp = new System.Net.Http.HttpClient(new RefusingHandler()) { BaseAddress = new Uri("http://localhost/") })
{
    IQuoteApi rejecting = new QuoteApiClient(rejectingHttp, new SmokeSerializer(), new QuoteErrorMapper());
    var status = await rejecting.TryGetQuoteAsync(1).ConfigureAwait(false);
    if (!status.IsFailure
        || status.Error.Kind != HttpErrorKind.Status
        || status.Error.Status != 422
        || status.Error.BodyLength != UnprocessableHandler.Body.Length)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 422 should map to QuoteError with its body");
        return 1;
    }

    IQuoteApi refusing = new QuoteApiClient(refusingHttp, new SmokeSerializer(), new QuoteErrorMapper());
    var transport = await refusing.TryGetQuoteAsync(1).ConfigureAwait(false);
    if (!transport.IsFailure || transport.Error.Kind != HttpErrorKind.Transport)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a transport failure should map to QuoteError");
        return 1;
    }
}

// Issue #318: [Get] with no path sends the request to the HttpClient's BaseAddress itself,
// trailing path segment included.
{
    var baseAddress = new Uri("http://localhost/api/");
    var capturingHandler = new RequestUriCapturingHandler();
    using var pingHttp = new System.Net.Http.HttpClient(capturingHandler) { BaseAddress = baseAddress };
    IUserApi pinging = new UserApiClient(pingHttp, new SmokeSerializer());
    await pinging.PingAsync().ConfigureAwait(false);
    if (capturingHandler.CapturedUri != baseAddress)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a pathless [Get] should request the BaseAddress itself");
        return 1;
    }
}

// Spec §10.5: a client generated from petstore.yaml, serializing through its generated context
// over a real loopback connection, with no reflection anywhere under ILC.
{
    using var server = new StubServer();
    using var http = new System.Net.Http.HttpClient { BaseAddress = server.BaseAddress };
    ZeroAlloc.Rest.AotSmoke.PetStore.IPetStoreClient pets = new ZeroAlloc.Rest.AotSmoke.PetStore.PetStoreClientClient(
        http, new ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer(ZeroAlloc.Rest.AotSmoke.PetStore.PetStoreClientJsonContext.Default));
    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

    var serving = server.ServeAsync(200, "{\"id\":7,\"name\":\"Rex\",\"status\":\"sold\"}", timeout.Token);
    var pet = await pets.GetPetAsync(7, timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!pet.IsSuccess || !string.Equals(pet.Value.Name, "Rex", StringComparison.Ordinal) || pet.Value.Status != ZeroAlloc.Rest.AotSmoke.PetStore.PetStatus.Sold
        || !server.LastRequest.StartsWith("GET /pets/7 ", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — the generated client should read a Pet over a real connection");
        return 1;
    }

    serving = server.ServeAsync(201, "{\"code\":\"duplicate\"}", timeout.Token);
    var added = await pets.AddPetAsync(new ZeroAlloc.Rest.AotSmoke.PetStore.Pet { Id = 8, Name = "Tom" }, timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!added.IsSuccess || !string.Equals(added.Value.AsError?.Code, "duplicate", StringComparison.Ordinal)
        || !server.LastRequest.EndsWith("{\"id\":8,\"name\":\"Tom\"}", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("AOT smoke: FAIL — the body and the oneOf response should round-trip through the context");
        return 1;
    }

    serving = server.ServeAsync(200, "{\"id\":7,\"name\":\"Rex\",\"status\":\"lost\"}", timeout.Token);
    var unknown = await pets.GetPetAsync(7, timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!unknown.IsFailure || unknown.Error.Kind != HttpErrorKind.Deserialization)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — an unknown enum value should be a Deserialization error");
        return 1;
    }

    serving = server.ServeAsync(204, "", timeout.Token);
    var deleted = await pets.DeletePetAsync(7, timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!deleted.IsSuccess)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a 204 should be a UnitResult success");
        return 1;
    }
}

// Value-type responses over a real connection, read through a source-generated context: a
// nullable int holding a value and holding null, and a long.
{
    using var server = new StubServer();
    using var http = new System.Net.Http.HttpClient { BaseAddress = server.BaseAddress };
    ICountApi counts = new CountApiClient(http, new ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer(CountJsonContext.Default));
    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

    var serving = server.ServeAsync(200, "5", timeout.Token);
    var count = await counts.TryGetCountAsync(timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!count.IsSuccess || count.Value != 5)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — an int? body of 5 should be a success of 5");
        return 1;
    }

    serving = server.ServeAsync(200, "null", timeout.Token);
    count = await counts.TryGetCountAsync(timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (!count.IsSuccess || count.Value is not null)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — an int? body of JSON null should be a success of null");
        return 1;
    }

    serving = server.ServeAsync(200, "9007199254740993", timeout.Token);
    var total = await counts.GetTotalAsync(timeout.Token).ConfigureAwait(false);
    await serving.ConfigureAwait(false);
    if (total != 9007199254740993L)
    {
        Console.Error.WriteLine("AOT smoke: FAIL — a long body should read exactly");
        return 1;
    }
}

// Issue #362: StreamResponses over a real connection, with and without a Content-Length. A body of
// unknown length goes through the read-ahead that finds an empty one. JSON null and an empty body
// take the empty-body paths, an unreadable body is a Deserialization failure, and a 404 keeps its
// error body.
{
    using var server = new StubServer();
    using var http = new System.Net.Http.HttpClient { BaseAddress = server.BaseAddress };
    ICountApi counts = new CountApiClient(http, new ZeroAlloc.Rest.SystemTextJson.SystemTextJsonSerializer(CountJsonContext.Default));
    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

    foreach (var chunked in new[] { false, true })
    {
        var serving = server.ServeAsync(200, "5", chunked, timeout.Token);
        var count = await counts.TryGetCountStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (!count.IsSuccess || count.Value != 5)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — a streamed int? body of 5 should be a success of 5, chunked: {chunked}");
            return 1;
        }

        serving = server.ServeAsync(200, "null", chunked, timeout.Token);
        count = await counts.TryGetCountStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (!count.IsSuccess || count.Value is not null)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — a streamed int? body of JSON null should be a success of null, chunked: {chunked}");
            return 1;
        }

        serving = server.ServeAsync(200, "", chunked, timeout.Token);
        count = await counts.TryGetCountStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (!count.IsSuccess || count.Value is not null)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — an empty streamed int? body should be a success of null, chunked: {chunked}");
            return 1;
        }

        serving = server.ServeAsync(200, "not json", chunked, timeout.Token);
        count = await counts.TryGetCountStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (!count.IsFailure || count.Error.Kind != HttpErrorKind.Deserialization)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — an unreadable streamed body should be a Deserialization error, chunked: {chunked}");
            return 1;
        }

        const string Missing = "{\"code\":\"missing\"}";
        serving = server.ServeAsync(404, Missing, chunked, timeout.Token);
        count = await counts.TryGetCountStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (!count.IsFailure || count.Error.Kind != HttpErrorKind.Status || count.Error.Body.Length != Missing.Length)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — a streamed 404 should carry its body, chunked: {chunked}");
            return 1;
        }

        serving = server.ServeAsync(200, "9007199254740993", chunked, timeout.Token);
        var total = await counts.GetTotalStreamedAsync(timeout.Token).ConfigureAwait(false);
        await serving.ConfigureAwait(false);
        if (total != 9007199254740993L)
        {
            Console.Error.WriteLine($"AOT smoke: FAIL — a streamed long body should read exactly, chunked: {chunked}");
            return 1;
        }
    }
}

Console.WriteLine("AOT smoke: PASS");
return 0;
