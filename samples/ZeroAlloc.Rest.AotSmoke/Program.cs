using System;
using ZeroAlloc.Rest.AotSmoke;

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

Console.WriteLine("AOT smoke: PASS");
return 0;
