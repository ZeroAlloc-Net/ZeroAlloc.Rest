using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Issue #300: Result<T, TError> with a user-defined error type, mapped by an [ErrorMapper].
public class GeneratorErrorMapperTests
{
    private static readonly MetadataReference[] References =
    [
        .. Basic.Reference.Assemblies.Net100.References.All,
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Rest.HttpError).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Results.Result<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions).Assembly.Location),
    ];

    private const string NotConstructible = "it must be a closed, non-abstract class with a public constructor";

    private const string Types = """
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record JevError(string Code);
        public sealed record OtherError(int Status);
        public sealed record SystemOneRequest(string Question);
        public sealed record SystemOneResponse(string Answer);
        public sealed class JevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new(error.StatusCode.ToString());
        }
        public sealed class AnotherJevErrorMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("another");
        }
        public sealed class OtherErrorMapper : IHttpErrorMapper<OtherError>
        {
            public OtherError Map(HttpError error) => new((int)error.StatusCode);
        }
        public sealed class NotAMapper { }
        public sealed class PrivateCtorMapper : IHttpErrorMapper<JevError>
        {
            private PrivateCtorMapper() { }
            public JevError Map(HttpError error) => new("x");
        }
        public sealed record ThirdError(string Code);
        public sealed class PrivateCtorThirdErrorMapper : IHttpErrorMapper<ThirdError>
        {
            private PrivateCtorThirdErrorMapper() { }
            public ThirdError Map(HttpError error) => new("x");
        }
        public abstract class AbstractMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("x");
        }
        public sealed class DerivedMapper : AbstractMapper { }
        public sealed class MultiMapper : IHttpErrorMapper<JevError>, IHttpErrorMapper<OtherError>
        {
            JevError IHttpErrorMapper<JevError>.Map(HttpError error) => new("x");
            OtherError IHttpErrorMapper<OtherError>.Map(HttpError error) => new(0);
        }
        public sealed class Gen<T> : IHttpErrorMapper<T>
        {
            public T Map(HttpError error) => default!;
        }
        public sealed class OpenJev<T> : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("x");
        }
        public struct StructMapper : IHttpErrorMapper<JevError>
        {
            public JevError Map(HttpError error) => new("x");
        }
        public sealed class TupleMapper : IHttpErrorMapper<(int A, string B)>
        {
            public (int A, string B) Map(HttpError error) => (0, "x");
        }

        """;

    private const string AskAsync =
        "ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);";

    private const string PingAsync = "Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);";

    private static string Api(string attributes, params string[] methods)
        => Types + "[ZeroAllocRestClient]\n" + attributes + "\npublic interface IJevApi\n{\n"
            + string.Concat(methods.Select(m => "    [Post(\"v1/x\")]\n    " + m + "\n")) + "}\n";

    [Fact]
    public void MissingMapper_ReportsZra002_AtTheMethod()
    {
        var source = Api("", AskAsync);

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("AskAsync", At(source, diagnostic));
        Assert.Equal(MissingMessage("AskAsync", "MyApp.JevError"), Message(diagnostic));
    }

    [Fact]
    public void MapperForAnotherType_StillReportsZra002()
    {
        var source = Api("[ErrorMapper(typeof(OtherErrorMapper))]", AskAsync);

        var diagnostic = Assert.Single(Run(source).GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
        Assert.Equal("AskAsync", At(source, diagnostic));
        Assert.Equal(MissingMessage("AskAsync", "MyApp.JevError"), Message(diagnostic));
    }

    [Fact]
    public void TypeImplementingNoMapperInterface_ReportsZra003_AtTheAttribute()
    {
        var source = Api("[ErrorMapper(typeof(NotAMapper))]", PingAsync);

        var diagnostic = Assert.Single(Run(source).GeneratorDiagnostics);
        Assert.Equal("ZRA003", diagnostic.Id);
        Assert.Equal("ErrorMapper(typeof(NotAMapper))", At(source, diagnostic));
        Assert.Equal(
            "'MyApp.NotAMapper' cannot be an error mapper: it implements no IHttpErrorMapper<TError> interface",
            Message(diagnostic));
    }

    [Theory]
    [InlineData("PrivateCtorMapper")]
    [InlineData("AbstractMapper")]
    [InlineData("StructMapper")]
    [InlineData("OpenJev<>")]
    [InlineData("ZeroAlloc.Rest.IHttpErrorMapper<JevError>")]
    public void MapperThatCannotBeConstructed_ReportsZra003_AndNotZra002(string mapper)
    {
        var source = Api($"[ErrorMapper(typeof({mapper}))]", AskAsync);

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA003", diagnostic.Id);
        Assert.Equal($"ErrorMapper(typeof({mapper}))", At(source, diagnostic));
        var display = mapper.Replace("JevError", "MyApp.JevError", StringComparison.Ordinal);
        if (!display.StartsWith("ZeroAlloc.", StringComparison.Ordinal))
            display = "MyApp." + display;
        Assert.Equal($"'{display}' cannot be an error mapper: {NotConstructible}", Message(diagnostic));
        // Every row is valid C#, so the stub must leave ZRA003 as the only error.
        Assert.Empty(run.CompileErrors);
        Assert.Contains("=> throw new global::System.NotSupportedException(", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void OpenGenericMapperOverItsOwnTypeParameter_ReportsZra003_AndZra002()
    {
        // Gen<T> : IHttpErrorMapper<T> names no concrete error type, so it claims nothing.
        var source = Api("[ErrorMapper(typeof(Gen<>))]", AskAsync);

        var diagnostics = Run(source).GeneratorDiagnostics;

        Assert.Collection(
            diagnostics,
            d =>
            {
                Assert.Equal("ZRA003", d.Id);
                Assert.Equal("ErrorMapper(typeof(Gen<>))", At(source, d));
                Assert.Equal($"'MyApp.Gen<>' cannot be an error mapper: {NotConstructible}", Message(d));
            },
            d =>
            {
                Assert.Equal("ZRA002", d.Id);
                Assert.Equal("AskAsync", At(source, d));
                Assert.Equal(MissingMessage("AskAsync", "MyApp.JevError"), Message(d));
            });
    }

    [Theory]
    [InlineData("typeof(JevErrorMapper[])", "MyApp.JevErrorMapper[]")]
    [InlineData("null", "null")]
    public void ArgumentThatIsNotANamedType_ReportsZra003(string argument, string display)
    {
        var source = Api($"[ErrorMapper({argument})]", PingAsync);

        var diagnostic = Assert.Single(Run(source).GeneratorDiagnostics);
        Assert.Equal("ZRA003", diagnostic.Id);
        Assert.Equal($"ErrorMapper({argument})", At(source, diagnostic));
        Assert.Equal($"'{display}' cannot be an error mapper: {NotConstructible}", Message(diagnostic));
    }

    [Fact]
    public void UnresolvedMapperType_LeavesItToTheCompiler()
    {
        var source = Api("[ErrorMapper(typeof(Missing))]", AskAsync);

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Contains(run.CompileErrors, d => d.Id == "CS0246");
    }

    [Fact]
    public void TwoMappersForOneErrorType_ReportZra004_AtTheSecondAttribute()
    {
        var source = Api("[ErrorMapper(typeof(JevErrorMapper))]\n[ErrorMapper(typeof(AnotherJevErrorMapper))]", AskAsync);

        var diagnostic = Assert.Single(Run(source).GeneratorDiagnostics);
        Assert.Equal("ZRA004", diagnostic.Id);
        Assert.Equal("ErrorMapper(typeof(AnotherJevErrorMapper))", At(source, diagnostic));
        Assert.Equal(
            "'MyApp.AnotherJevErrorMapper' maps 'MyApp.JevError', which 'MyApp.JevErrorMapper' already maps; "
                + "declare one [ErrorMapper] per error type",
            Message(diagnostic));
    }

    [Theory]
    [InlineData("MultiMapper")]
    [InlineData("DerivedMapper")]
    [InlineData("Gen<JevError>")]
    public void UsableMapper_ReportsNothing(string mapper)
    {
        var source = Api($"[ErrorMapper(typeof({mapper}))]", AskAsync);

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Fact]
    public void MapperImplementingSeveralInterfaces_MapsEachErrorType()
    {
        var source = Api(
            "[ErrorMapper(typeof(MultiMapper))]",
            AskAsync,
            "Task<Result<string, OtherError>> OtherAsync(CancellationToken ct = default);");

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Theory]
    [InlineData("", "JevError?")]
    [InlineData("using JevAlias = MyApp.JevError;\n", "JevAlias")]
    [InlineData("", "global::MyApp.JevError")]
    public void ErrorTypeSpelling_DoesNotMatter(string prefix, string errorType)
    {
        var source = "#nullable enable\n" + prefix + Api(
            "[ErrorMapper(typeof(JevErrorMapper))]",
            $"Task<Result<string, {errorType}>> AskAsync(CancellationToken ct = default);");

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Fact]
    public void TupleElementNames_DoNotMatter()
    {
        var source = Api(
            "[ErrorMapper(typeof(TupleMapper))]",
            "Task<Result<string, (int X, string Y)>> AskAsync(CancellationToken ct = default);");

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Fact]
    public void MapperWhoseErrorTypeNoMethodUses_IsAllowed()
    {
        var source = Api("[ErrorMapper(typeof(OtherErrorMapper))]", PingAsync);

        Assert.Empty(Run(source).GeneratorDiagnostics);
    }

    [Fact]
    public void HttpErrorMethods_NeedNoMapper()
    {
        var source = Api("", PingAsync);

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
    }

    private const string JevApi = """
        [ZeroAllocRestClient]
        [ErrorMapper(typeof(JevErrorMapper))]
        public interface IJevApi
        {
            [Post("v1/systemone")]
            ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
        }
        """;

    [Fact]
    public void MappedMethod_ConstructorTakesTheMapperInterface_CreateResolvesTheConcreteMapper()
    {
        var run = Run(Types + JevApi);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Contains("private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> _jevErrorMapper;", client);
        Assert.Contains(
            "public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper)",
            client);
        Assert.Contains("_jevErrorMapper = jevErrorMapper;", client);
        Assert.Contains(
            "        var __errorMapper0 = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::MyApp.JevErrorMapper>(services);\n",
            client);
        Assert.Contains("            __errorMapper0);\n", client);
        Assert.DoesNotContain("GetRequiredService<global::ZeroAlloc.Rest.IHttpErrorMapper", client);
    }

    [Fact]
    public void MappedMethod_MapsAllFourFailureKinds_Once_AfterTheTry()
    {
        var client = Run(Types + JevApi).Sources["IJevApi.g.cs"];

        Assert.Contains("global::ZeroAlloc.Rest.HttpError __httpError;", client);
        Assert.Contains(
            "__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Status, response, null, __errorBody.Body, __errorBody.Truncated);",
            client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Deserialization, response, __ex);", client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Timeout, null, __ex);", client);
        Assert.Contains("__httpError = __CreateHttpError(global::ZeroAlloc.Rest.HttpErrorKind.Transport, null, __ex);", client);
        Assert.Contains(
            "return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Failure(_jevErrorMapper.Map(__httpError));",
            client);
        Assert.Contains(
            "return ZeroAlloc.Results.Result<MyApp.SystemOneResponse, global::MyApp.JevError>.Success(content);",
            client);
        Assert.Equal(1, CountOccurrences(client, ".Map("));
        Assert.Contains("__RecordMapperFailure(__activity, __ex);", client);
        Assert.Contains("private static void __RecordMapperFailure(", client);
    }

    [Fact]
    public void HttpErrorMethod_IsEmittedExactlyAsWithoutAMapper()
    {
        const string HttpErrorMethod = """
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            """;
        var plain = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
            """ + "\n" + HttpErrorMethod + "\n}";
        var mapped = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            """ + "\n" + HttpErrorMethod + "\n}";

        var plainRun = Run(plain);
        var mappedRun = Run(mapped);

        Assert.Empty(plainRun.CompileErrors);
        Assert.Empty(mappedRun.CompileErrors);
        Assert.Equal(
            MethodText(plainRun.Sources["IJevApi.g.cs"], "PingAsync"),
            MethodText(mappedRun.Sources["IJevApi.g.cs"], "PingAsync"));
        Assert.DoesNotContain("__httpError", MethodText(mappedRun.Sources["IJevApi.g.cs"], "PingAsync"));
    }

    [Fact]
    public void TwoErrorTypes_GetTwoParameters_InOrderOfFirstUse()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(OtherErrorMapper))]
            [ErrorMapper(typeof(JevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);

                [Get("v1/status")]
                Task<Result<string, OtherError>> StatusAsync(CancellationToken ct = default);

                [Get("v1/again")]
                Task<Result<string, JevError>> AgainAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Contains(
            "ZeroAlloc.Rest.IRestSerializer serializer, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.OtherError> otherErrorMapper)",
            client);
        Assert.Equal(2, CountOccurrences(client, "_jevErrorMapper.Map(__httpError)"));
        Assert.Equal(1, CountOccurrences(client, "_otherErrorMapper.Map(__httpError)"));
    }

    [Fact]
    public void TwoTupleErrorTypes_GetValidDistinctFieldNames_AndCompile()
    {
        var source = Types + """
            public sealed class OtherTupleMapper : IHttpErrorMapper<(string Code, int Status)>
            {
                public (string Code, int Status) Map(HttpError error) => ("x", 0);
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(TupleMapper))]
            [ErrorMapper(typeof(OtherTupleMapper))]
            public interface IJevApi
            {
                [Get("v1/a")]
                Task<Result<string, (int X, string Y)>> FirstAsync(CancellationToken ct = default);

                [Get("v1/b")]
                Task<Result<string, (string C, int S)>> SecondAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Empty(run.GeneratedWarnings);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Contains(
            "private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::System.ValueTuple<int, string>> _valueTupleMapper;",
            client);
        Assert.Contains(
            "private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::System.ValueTuple<string, int>> _valueTupleMapper2;",
            client);
        Assert.Contains("global::ZeroAlloc.Rest.IHttpErrorMapper<global::System.ValueTuple<string, int>> valueTupleMapper2)", client);
        Assert.Equal(1, CountOccurrences(client, "_valueTupleMapper.Map(__httpError)"));
        Assert.Equal(1, CountOccurrences(client, "_valueTupleMapper2.Map(__httpError)"));
    }

    [Theory]
    [InlineData("JevError?", "JevError")]
    [InlineData("JevError", "JevError?")]
    [InlineData("JevError?", "JevError?")]
    [InlineData("System.Collections.Generic.List<string?>", "System.Collections.Generic.List<string?>")]
    [InlineData("System.Collections.Generic.List<string?>?", "System.Collections.Generic.List<string?>")]
    public void NullableAnnotations_OnTheMapperOrTheMethod_CompileWithoutWarnings(string mapperError, string methodError)
    {
        var source = "#nullable enable\n" + Types + $$"""
            public sealed class NullableJevErrorMapper : IHttpErrorMapper<{{mapperError}}>
            {
                public {{mapperError}} Map(HttpError error) => default!;
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(NullableJevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, {{methodError}}>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Empty(run.GeneratedWarnings);
        Assert.Equal(1, CountOccurrences(run.Sources["IJevApi.g.cs"], ".Map("));
    }

    [Fact]
    public void NestedNullabilityMismatch_ReportsZra002_AndCompilesWithoutWarnings()
    {
        // Only the top-level annotation is ignored. List<string?> does not convert to List<string>
        // without a warning, so the mapper does not map this method's error type.
        var source = "#nullable enable\n" + Types + """
            public sealed class ListMapper : IHttpErrorMapper<System.Collections.Generic.List<string?>>
            {
                public System.Collections.Generic.List<string?> Map(HttpError error) => new();
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(ListMapper))]
            public interface IJevApi
            {
                [Get("v1/list")]
                Task<Result<string, System.Collections.Generic.List<string>>> ListAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA002", diagnostic.Id);
        Assert.Equal("ListAsync", At(source, diagnostic));
        Assert.Empty(run.CompileErrors);
        Assert.Empty(run.GeneratedWarnings);
        Assert.Contains("=> throw new global::System.NotSupportedException(", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void ObliviousInterface_WithAMapperOverANullableErrorType_ChecksForNull_AndCompilesWithoutWarnings()
    {
        var source = "#nullable enable\n" + Types + """
            public sealed class NullableJevErrorMapper : IHttpErrorMapper<JevError?>
            {
                public JevError? Map(HttpError error) => null;
            }
            #nullable disable
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(NullableJevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Empty(run.GeneratedWarnings);
        Assert.Contains(".Failure(_jevErrorMapper.Map(__httpError) ?? throw new global::System.InvalidOperationException(", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void MapperServingTwoErrorTypes_IsResolvedOnce_AndCompiles()
    {
        var source = Api(
            "[ErrorMapper(typeof(MultiMapper))]",
            AskAsync,
            "Task<Result<string, OtherError>> OtherAsync(CancellationToken ct = default);");

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Empty(run.GeneratedWarnings);
        var client = run.Sources["IJevApi.g.cs"];
        Assert.Equal(1, CountOccurrences(client, "GetRequiredService<global::MyApp.MultiMapper>(services)"));
        Assert.Contains("            __errorMapper0,\n            __errorMapper0);\n", client);
        Assert.Contains(
            "global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError> jevErrorMapper, global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.OtherError> otherErrorMapper)",
            client);
    }

    [Fact]
    public void MapperThatMayReturnNull_ForANonNullableErrorType_ThrowsInsteadOfReturningNull()
    {
        var source = "#nullable enable\n" + Types + """
            public sealed class NullableJevErrorMapper : IHttpErrorMapper<JevError?>
            {
                public JevError? Map(HttpError error) => null;
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(NullableJevErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var client = Run(source).Sources["IJevApi.g.cs"];

        Assert.Contains("private readonly global::ZeroAlloc.Rest.IHttpErrorMapper<global::MyApp.JevError?> _jevErrorMapper;", client);
        Assert.Contains(".Failure(_jevErrorMapper.Map(__httpError) ?? throw new global::System.InvalidOperationException(", client);
    }

    [Fact]
    public void MapperForAnUnusedErrorType_AddsNoConstructorParameter()
    {
        var source = Types + """
            internal sealed record HiddenError(int Status);
            internal sealed class HiddenErrorMapper : IHttpErrorMapper<HiddenError>
            {
                public HiddenError Map(HttpError error) => new((int)error.StatusCode);
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(HiddenErrorMapper))]
            public interface IJevApi
            {
                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Contains(
            "public JevApiClient(System.Net.Http.HttpClient httpClient, ZeroAlloc.Rest.IRestSerializer serializer)",
            run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void InternalInterface_WithInternalMapperAndErrorType_Compiles()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest;
            using ZeroAlloc.Rest.Attributes;
            using ZeroAlloc.Results;
            namespace MyApp;
            internal sealed record JevError(string Code);
            internal sealed record SystemOneRequest(string Question);
            internal sealed record SystemOneResponse(string Answer);
            internal sealed class JevErrorMapper : IHttpErrorMapper<JevError>
            {
                public JevError Map(HttpError error) => new(error.StatusCode.ToString());
            }
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            internal interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompileErrors);
        Assert.Contains("internal sealed partial class JevApiClient", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void UnmappedMethod_ReportsOnlyZra002_AndTheClientStillCompiles()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);

                [Get("/ping")]
                Task<Result<string, HttpError>> PingAsync(CancellationToken ct = default);
            }
            """;

        var run = Run(source);

        Assert.Equal("ZRA002", Assert.Single(run.GeneratorDiagnostics).Id);
        Assert.Empty(run.CompileErrors);
        Assert.Contains("=> throw new global::System.NotSupportedException(", run.Sources["IJevApi.g.cs"]);
    }

    [Fact]
    public void AddSerializers_RegistersEveryUsableMapper_ByItsConcreteType()
    {
        var source = Types + """
            [ZeroAllocRestClient]
            [ErrorMapper(typeof(JevErrorMapper))]
            [ErrorMapper(typeof(OtherErrorMapper))]
            [ErrorMapper(typeof(NotAMapper))]
            [ErrorMapper(typeof(PrivateCtorThirdErrorMapper))]
            public interface IJevApi
            {
                [Post("v1/systemone")]
                ValueTask<Result<SystemOneResponse, JevError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);
            }
            """;

        var run = Run(source);

        Assert.Empty(run.CompileErrors);
        Assert.Equal(2, run.GeneratorDiagnostics.Count(d => d.Id == "ZRA003"));
        var client = run.Sources["IJevApi.g.cs"];
        const string TryAdd = "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton";
        Assert.Contains($"{TryAdd}<global::MyApp.JevErrorMapper>(services);", client);
        // No method uses OtherError: its mapper is registered, never called, and not a parameter.
        Assert.Contains($"{TryAdd}<global::MyApp.OtherErrorMapper>(services);", client);
        // NotAMapper is invalid (ZRA003, implements no IHttpErrorMapper<TError>): it never reaches DI.
        Assert.DoesNotContain($"{TryAdd}<global::MyApp.NotAMapper>(services);", client);
        // PrivateCtorThirdErrorMapper implements IHttpErrorMapper<ThirdError> but has no public
        // constructor (ZRA003 too): the risky branch, since it claims ThirdError before failing
        // the constructible check, so it must still never reach DI registration.
        Assert.DoesNotContain($"{TryAdd}<global::MyApp.PrivateCtorThirdErrorMapper>(services);", client);
        Assert.DoesNotContain("otherErrorMapper", client);
        Assert.DoesNotContain("IHttpErrorMapper<global::MyApp.JevError>>(services)", client);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string MethodText(string generated, string methodName)
    {
        var signature = generated.IndexOf($" {methodName}(", StringComparison.Ordinal);
        var start = generated.LastIndexOf("    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\"", signature, StringComparison.Ordinal);
        var end = generated.IndexOf("\n    }\n", signature, StringComparison.Ordinal);
        return generated.Substring(start, end - start);
    }

    private static string MissingMessage(string method, string errorType)
        => $"Method '{method}' returns a Result with error type '{errorType}', but no [ErrorMapper] on the "
            + $"interface implements IHttpErrorMapper<{errorType}>";

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    private static string At(string source, Diagnostic diagnostic)
        => source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static GeneratorRun Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "Api.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new RestClientGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var sources = driver.GetRunResult().Results[0].GeneratedSources
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString().Replace("\r\n", "\n"), StringComparer.Ordinal);
        var compileDiagnostics = output.GetDiagnostics();
        var compileErrors = compileDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        // Consumers build with TreatWarningsAsErrors, so a warning in generated code breaks them.
        var generatedWarnings = compileDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning
                && !string.Equals(d.Location.SourceTree?.FilePath, "Api.cs", StringComparison.Ordinal))
            .ToImmutableArray();
        return new GeneratorRun(sources, generatorDiagnostics, compileErrors, generatedWarnings);
    }

    private sealed record GeneratorRun(
        Dictionary<string, string> Sources,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompileErrors,
        ImmutableArray<Diagnostic> GeneratedWarnings);
}
