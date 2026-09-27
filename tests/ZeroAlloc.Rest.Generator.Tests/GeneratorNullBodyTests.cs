using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// A body that deserializes to null, an empty 204 or JSON null, is checked against the declared
// success type instead of being forced through with the null-forgiving operator.
public class GeneratorNullBodyTests
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;

        public sealed record Pet(string Name);

        public sealed record DomainError(string Code);

        public sealed class DomainErrorMapper : IHttpErrorMapper<DomainError>
        {
            public DomainError Map(HttpError error) => new(error.Kind.ToString());
        }

        [ZeroAllocRestClient]
        [ErrorMapper(typeof(DomainErrorMapper))]
        public interface IPetApi
        {
            [Get("/a")] Task<Result<Pet, HttpError>> ResultRefAsync(CancellationToken ct = default);
            [Get("/b")] Task<Result<Pet?, HttpError>> ResultNullableRefAsync(CancellationToken ct = default);
            [Get("/c")] Task<Result<int, HttpError>> ResultValueAsync(CancellationToken ct = default);
            [Get("/d")] Task<Result<int?, HttpError>> ResultNullableValueAsync(CancellationToken ct = default);
            [Get("/e")] Task<Result<Pet, DomainError>> MappedRefAsync(CancellationToken ct = default);
            [Get("/f")] Task<Pet> TaskRefAsync(CancellationToken ct = default);
            [Get("/g")] Task<Pet?> TaskNullableRefAsync(CancellationToken ct = default);
            [Get("/h")] Task<int> TaskValueAsync(CancellationToken ct = default);
            [Get("/i")] Task<int?> TaskNullableValueAsync(CancellationToken ct = default);
            [Get("/j")] Task<Pet[]> TaskArrayAsync(CancellationToken ct = default);
        }
        """;

    private static GeneratorHarness.GeneratorHarnessRun Run() => GeneratorHarness.Run(Source, "IPetApi.g.cs");

    [Fact]
    public void Generated_CompilesClean_WithNoNullForgivingDeserialize()
    {
        var run = Run();

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.True(run.Problems.IsEmpty, run.GeneratedSource + "\n" + string.Join("\n", run.Problems));
        Assert.DoesNotContain(")!", run.GeneratedSource);
    }

    [Theory]
    [InlineData("ResultRefAsync", "Pet")]
    [InlineData("MappedRefAsync", "Pet")]
    [InlineData("TaskRefAsync", "Pet")]
    [InlineData("TaskArrayAsync", "Pet[]")]
    public void NonNullableReference_RejectsANullBody(string method, string type)
    {
        var body = MethodBody(Run().GeneratedSource, method);

        Assert.Contains(
            $"?? throw new global::System.InvalidOperationException(\"{method} received an empty or null response body, "
                + $"but its success type MyApp.{type} does not accept null. Declare MyApp.{type}? to accept an empty body.\")",
            body);
    }

    [Theory]
    [InlineData("ResultValueAsync")]
    [InlineData("TaskValueAsync")]
    public void NonNullableValue_RejectsAnEmptyBody(string method)
    {
        var body = MethodBody(Run().GeneratedSource, method);

        Assert.Contains("if (__responseStream.CanSeek && __responseStream.Position >= __responseStream.Length)", body);
        Assert.Contains("throw new global::System.InvalidOperationException(", body);
        Assert.DoesNotContain("?? throw", body);
    }

    [Theory]
    [InlineData("ResultNullableRefAsync")]
    [InlineData("ResultNullableValueAsync")]
    [InlineData("TaskNullableRefAsync")]
    [InlineData("TaskNullableValueAsync")]
    public void NullableT_AcceptsANullBody(string method)
    {
        var body = MethodBody(Run().GeneratedSource, method);

        Assert.Contains("DeserializeAsync<", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    // The text of one generated method: from its signature up to the next method's.
    private static string MethodBody(string source, string method)
    {
        var start = source.IndexOf(" " + method + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, source);
        var end = source.IndexOf("public async ", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }
}
