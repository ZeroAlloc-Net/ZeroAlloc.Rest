using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// ZeroAlloc-Net/.github#46: every ZRA diagnostic the generator reports is a source location bound to
// the user's syntax tree, so the IDE can point at the code and #pragma warning disable can suppress
// it. A location rebuilt from the file path alone is an external-file location, which the compiler
// shows but never suppresses. A source marked with [| and |] gives the expected span; the markers are
// removed before it runs.
public class GeneratorDiagnosticLocationTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.StatusCode.ToString());
        }
        public sealed class AnotherJevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("another");
        }
        public sealed class NotAMapper { }

        """;

    [Theory]
    // ZRA001: the identifier of the method with both a [Body] and a [FormBody] parameter.
    [InlineData("ZRA001", """
        [ZeroAllocRestClient]
        public interface IApi
        {
            [Post("/token")]
            Task<string> [|BadAsync|]([Body] string body, [FormBody] Dictionary<string, string> form);
        }
        """)]
    // ZRA002: the identifier of the method whose error type no mapper maps.
    [InlineData("ZRA002", """
        [ZeroAllocRestClient]
        public interface IApi
        {
            [Get("/users")]
            Task<Result<string, JevError>> [|GetAsync|](CancellationToken ct = default);
        }
        """)]
    // ZRA003: the [ErrorMapper] attribute that names the invalid mapper.
    [InlineData("ZRA003", """
        [ZeroAllocRestClient]
        [[|ErrorMapper(typeof(NotAMapper))|]]
        public interface IApi
        {
            [Get("/users")]
            Task<string> GetAsync(CancellationToken ct = default);
        }
        """)]
    // ZRA004: the later [ErrorMapper] attribute, the one that claims an already mapped error type.
    [InlineData("ZRA004", """
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        [[|ErrorMapper(typeof(AnotherJevErrorMapper))|]]
        public interface IApi
        {
            [Get("/users")]
            Task<Result<string, JevError>> GetAsync(CancellationToken ct = default);
        }
        """)]
    public void Diagnostic_IsReportedAtItsSourceLocation(string id, string markedSource)
    {
        var (source, span) = Unmark(Prelude + markedSource);

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics, d => d.Id == id);
        AssertAt(diagnostic.Location, source, span);
    }

    [Fact]
    public void PragmaAroundOneMethod_SuppressesThatDiagnosticOnly()
    {
        var source = Prelude + """
            [ZeroAllocRestClient]
            public interface IApi
            {
            #pragma warning disable ZRA002
                [Get("/quiet")]
                Task<Result<string, JevError>> QuietAsync(CancellationToken ct = default);
            #pragma warning restore ZRA002

                [Get("/loud")]
                Task<Result<string, JevError>> LoudAsync(CancellationToken ct = default);
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        var zra002 = run.GeneratorDiagnostics.Where(d => d.Id == "ZRA002").ToList();
        Assert.Equal(2, zra002.Count);
        Assert.True(Single(zra002, "QuietAsync").IsSuppressed);
        Assert.False(Single(zra002, "LoudAsync").IsSuppressed);

        static Diagnostic Single(List<Diagnostic> list, string methodName) =>
            Assert.Single(list, d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("'" + methodName + "'", System.StringComparison.Ordinal));
    }

    internal static void AssertAt(Location location, string source, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        Assert.Equal(LocationKind.SourceFile, location.Kind);
        Assert.NotNull(location.SourceTree);
        Assert.Equal("Api.cs", location.SourceTree!.FilePath);
        Assert.Equal(expected, location.SourceSpan);

        var lineSpan = location.GetLineSpan();
        Assert.Equal("Api.cs", lineSpan.Path);
        Assert.Equal(SourceText.From(source).Lines.GetLinePositionSpan(expected), lineSpan.Span);
    }

    private static (string Source, TextSpan Span) Unmark(string marked)
    {
        var start = marked.IndexOf("[|", System.StringComparison.Ordinal);
        var end = marked.IndexOf("|]", System.StringComparison.Ordinal);
        var sb = new StringBuilder(marked.Length);
        sb.Append(marked, 0, start);
        sb.Append(marked, start + 2, end - start - 2);
        sb.Append(marked, end + 2, marked.Length - end - 2);
        return (sb.ToString(), TextSpan.FromBounds(start, end - 2));
    }
}
