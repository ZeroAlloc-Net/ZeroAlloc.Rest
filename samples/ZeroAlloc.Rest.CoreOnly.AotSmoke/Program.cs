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

// Value-type responses through the generated client: each case reads through its own generic
// instantiation, and a nullable value type through the branch that accepts null.
var values = new ValueStubHandler();
using var valueHttp = new HttpClient(values) { BaseAddress = new Uri("http://localhost/") };
IPingApi valueClient = new PingApiClient(valueHttp, new JsonContextSerializer());

values.Body = "7";
if (await valueClient.GetCountAsync().ConfigureAwait(false) != 7)
    return Fail("an int? body of 7 should read as 7");

values.Body = "null";
if (await valueClient.GetCountAsync().ConfigureAwait(false) is not null)
    return Fail("an int? body of JSON null should read as null");

values.Body = "42";
var total = await valueClient.TryGetTotalAsync().ConfigureAwait(false);
if (!total.IsSuccess || total.Value != 42L)
    return Fail("a Result<long, HttpError> body of 42 should be a success of 42");

// An empty success body has no value, and long does not accept null: a Deserialization failure
// that names the problem, never a success of 0.
values.Body = null;
total = await valueClient.TryGetTotalAsync().ConfigureAwait(false);
if (!total.IsFailure
    || total.Error.Kind != ZeroAlloc.Rest.HttpErrorKind.Deserialization
    || total.Error.StatusCode != System.Net.HttpStatusCode.OK
    || total.Error.Message?.Contains("empty or null", StringComparison.Ordinal) != true)
{
    return Fail("an empty 200 body for Result<long, HttpError> should be a Deserialization failure");
}

values.Body = "2";
if (await valueClient.GetLevelAsync().ConfigureAwait(false) != PingLevel.High)
    return Fail("an enum body of 2 should read as PingLevel.High");

values.Body = """{"x":3,"y":-4}""";
var origin = await valueClient.TryGetOriginAsync().ConfigureAwait(false);
if (!origin.IsSuccess || origin.Value != new PingPoint(3, -4))
    return Fail("a nullable struct body should be a success holding the struct");

values.Body = "null";
origin = await valueClient.TryGetOriginAsync().ConfigureAwait(false);
if (!origin.IsSuccess || origin.Value is not null)
    return Fail("a nullable struct body of JSON null should be a success holding null");

// JsonContextSerializer must not fall back to reflection for a type PingJsonContext does not
// cover: it throws instead of silently resolving one through JsonSerializerOptions.
try
{
    await new JsonContextSerializer().SerializeAsync(Stream.Null, Guid.Empty).ConfigureAwait(false);
    Console.Error.WriteLine("core-only smoke: FAIL — an unregistered type should throw NotSupportedException");
    return 1;
}
catch (NotSupportedException)
{
}

Console.WriteLine("core-only smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine("core-only smoke: FAIL — " + message);
    return 1;
}
