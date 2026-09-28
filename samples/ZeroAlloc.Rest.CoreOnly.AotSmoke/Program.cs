using ZeroAlloc.Rest.CoreOnly.AotSmoke;

// A consumer of ZeroAlloc.Rest core alone, without ZeroAlloc.Rest.DependencyInjection: it builds
// the generated PingApiClient by hand from the two-parameter constructor the generator emits
// without the DI package, and serializes through a hand-authored JsonSerializerContext, not
// through JsonSerializerOptions or reflection. Runs under JIT in CI and is published with Native
// AOT; see #336.

using var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost/") };
IPingApi client = new PingApiClient(http, new JsonContextSerializer());

var pong = await client.GetAsync().ConfigureAwait(false);
if (!string.Equals(pong.Message, StubHandler.ExpectedMessage, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"core-only smoke: FAIL — expected '{StubHandler.ExpectedMessage}', got '{pong.Message}'");
    return 1;
}

// JsonContextSerializer must not fall back to reflection for a type PingJsonContext does not
// cover: it throws instead of silently resolving one through JsonSerializerOptions.
try
{
    await new JsonContextSerializer().SerializeAsync(Stream.Null, 42).ConfigureAwait(false);
    Console.Error.WriteLine("core-only smoke: FAIL — an unregistered type should throw NotSupportedException");
    return 1;
}
catch (NotSupportedException)
{
}

Console.WriteLine("core-only smoke: PASS");
return 0;
