using System;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Rest;
using ZeroAlloc.Rest.AotSmoke;
using ZeroAlloc.Rest.Resilience;

// Verify that the generator-emitted UserApiClient type (emitted for the
// [ZeroAllocRestClient] interface) compiles and publishes cleanly under
// PublishAot=true. We don't fire an actual HTTP request — that path requires
// an IRestSerializer + HttpClient setup that duplicates the integration tests.
// The compile-time guarantee (ILC analyses the emitted proxy) is the AOT signal
// we want from this smoke.

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

Console.WriteLine("AOT smoke: PASS");
return 0;
