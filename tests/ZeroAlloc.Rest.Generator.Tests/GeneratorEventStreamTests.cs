using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Server-sent events: a method returning IAsyncEnumerable<T> with [EventStream] reads the response
// as text/event-stream. Shape one, IAsyncEnumerable<ServerSentEvent<T>>, deserializes every event as
// T. Shape two, IAsyncEnumerable<TBase>, deserializes each event named by an [EventType] as its
// mapped type and skips the rest. Every other method is emitted exactly as before.
public class GeneratorEventStreamTests
{
    private const string HeadersRead = "global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead";

    private const string Types = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest;
        using ZeroAlloc.Rest.Attributes;
        using ZeroAlloc.Results;
        namespace MyApp;
        public sealed record Thing(string Name);
        public sealed record Request(string Prompt);
        public abstract record StreamEvent;
        public sealed record StartEvent(string Id) : StreamEvent;
        public sealed record DeltaEvent(string Text) : StreamEvent;
        public sealed record Unrelated(int Value);
        public sealed record ApiError(string Code);
        public sealed class ApiErrorMapper : IHttpErrorMapper<ApiError>
        {
            public ApiError Map(HttpError error) => new(error.StatusCode.ToString());
        }
        public sealed class NullableApiErrorMapper : IHttpErrorMapper<ApiError?>
        {
            public ApiError? Map(HttpError error) => null;
        }
        """;

    private static string Api(string members, string interfaceAttributes = "")
        => $$"""
            {{Types}}
            [ZeroAllocRestClient]
            {{interfaceAttributes}}
            public interface IThingApi
            {
            {{members}}
            }
            """;

    private const string ShapeTwo = """
            [Post("v1/x")]
            [EventStream]
            [EventType("start", typeof(StartEvent))]
            [EventType("delta", typeof(DeltaEvent))]
            IAsyncEnumerable<StreamEvent> StreamAsync([Body] Request body, CancellationToken ct = default);
        """;

    private const string ShapeOne = """
            [Get("/things/stream")]
            [EventStream]
            IAsyncEnumerable<ServerSentEvent<Thing>> StreamThingsAsync(CancellationToken ct = default);
        """;

    private const string Plain = """
            [Get("/things/{id}")]
            Task<Thing> GetAsync(int id, CancellationToken ct = default);

            [Get("/things/{id}/result")]
            Task<Result<Thing, HttpError>> GetResultAsync(int id, CancellationToken ct = default);
        """;

    [Fact]
    public void ShapeTwo_compiles_without_diagnostics()
    {
        var run = Run(Api(ShapeTwo));

        // Red: report ZRA008's no-[EventType] message for every shape two method.
        Assert.Empty(run.GeneratorDiagnostics);
        // Red: skip the event stream branch in EmitMethod, so the Task<T> body is emitted; CS1622.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ShapeTwo_sends_with_ResponseHeadersRead()
    {
        var client = Run(Api(ShapeTwo)).GeneratedSource;

        // Red: drop ResponseHeadersRead from the iterator's SendAsync.
        Assert.Contains($"SendAsync(__request, {HeadersRead}, ct)", client);
    }

    [Fact]
    public void ShapeTwo_accepts_text_event_stream_only()
    {
        var client = Run(Api(ShapeTwo)).GeneratedSource;

        // Red: send the serializer's Accept for an event stream.
        Assert.Contains("__request.Headers.TryAddWithoutValidation(\"Accept\", \"text/event-stream\");", client);
        // Red: send the serializer's Accept beside text/event-stream.
        Assert.DoesNotContain("_serializer.ContentType", client);
    }

    [Fact]
    public void ShapeTwo_compares_event_names_as_UTF8_literals()
    {
        var client = Run(Api(ShapeTwo)).GeneratedSource;

        // Red: compare the decoded name, __reader.EventName == "start".
        Assert.Contains("__reader.EventNameEquals(\"start\"u8)", client);
        // Red: emit only the first [EventType]'s branch.
        Assert.Contains("__reader.EventNameEquals(\"delta\"u8)", client);
    }

    [Fact]
    public void The_iterator_token_is_marked_EnumeratorCancellation()
    {
        var client = Run(Api(ShapeTwo)).GeneratedSource;

        // Red: drop [EnumeratorCancellation] from the iterator's token.
        Assert.Contains("[global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken ct)", client);
    }

    [Fact]
    public void ShapeOne_compiles_without_diagnostics()
    {
        var run = Run(Api(ShapeOne));

        // Red: report ZRA008's shape one message whatever the [EventType] count.
        Assert.Empty(run.GeneratorDiagnostics);
        // Red: emit a shape one method through shape two's branch, which has no names to test.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ShapeOne_yields_the_event_name_and_the_last_event_id()
    {
        var client = Run(Api(ShapeOne)).GeneratedSource;

        // Red: swap EventName and LastEventId in the ServerSentEvent constructor call.
        Assert.Contains("new global::ZeroAlloc.Rest.ServerSentEvent<global::MyApp.Thing>(__reader.EventName, __reader.LastEventId,", client);
    }

    [Fact]
    public void A_method_without_a_CancellationToken_compiles()
    {
        const string members = """
                [Get("/things/stream")]
                [EventStream]
                IAsyncEnumerable<ServerSentEvent<Thing>> StreamThingsAsync();

                [Get("/things/stream")]
                [EventStream]
                IAsyncEnumerable<ServerSentEvent<Thing>> StreamThingsAsync(CancellationToken ct);
            """;

        // Red: name the iterator after the method alone; the overloads' iterators then clash, since
        // the one without a token gains one; CS0111.
        Assert.Empty(Run(Api(members)).Problems);
    }

    private const string Mapped = """
            [Post("v1/x")]
            [EventStream(ErrorType = typeof(ApiError))]
            [EventType("start", typeof(StartEvent))]
            IAsyncEnumerable<StreamEvent> StreamAsync([Body] Request body, CancellationToken ct = default);
        """;

    [Fact]
    public void ErrorType_with_a_mapper_compiles_without_diagnostics()
    {
        var run = Run(Api(Mapped, "[ErrorMapper(typeof(ApiErrorMapper))]"));

        // Red: report ZRA002 for an ErrorType whatever the mappers.
        Assert.Empty(run.GeneratorDiagnostics);
        // Red: construct HttpErrorException<E> from the mapped error alone; CS1729.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ErrorType_with_a_mapper_throws_the_mapped_exception()
    {
        var client = Run(Api(Mapped, "[ErrorMapper(typeof(ApiErrorMapper))]")).GeneratedSource;

        // Red: leave event streams out of MapsError, so the ErrorType's mapper is never used.
        Assert.Contains("throw new global::ZeroAlloc.Rest.HttpErrorException<global::MyApp.ApiError>(__httpError, _apiErrorMapper.Map(__httpError));", client);
    }

    [Fact]
    public void ErrorType_with_a_mapper_over_a_nullable_type_checks_for_null()
    {
        // Red: pass the mapper's ApiError? to HttpErrorException<ApiError> without the null check;
        // CS8604.
        Assert.Empty(Run(Api(Mapped, "[ErrorMapper(typeof(NullableApiErrorMapper))]")).Problems);
    }

    [Fact]
    public void ZRA007_an_IAsyncEnumerable_method_without_EventStream()
    {
        const string members = """
                [Get("/things/stream")]
                IAsyncEnumerable<Thing> StreamAsync(CancellationToken ct = default);
            """;

        var run = Run(Api(members));

        // Red: do not report a missing [EventStream].
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        // Red: report it with ZRA008's no-[EventType] descriptor.
        Assert.Equal("ZRA007", diagnostic.Id);
        // Red: declare ZRA007 a Warning.
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // Red: leave the method's name out of the message arguments.
        Assert.Contains("'StreamAsync'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // Red: keep the method valid without [EventStream], so the iterator is emitted with no
        // names to test; CS1525.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ZRA007_EventStream_on_a_method_that_does_not_return_IAsyncEnumerable()
    {
        const string members = """
                [Get("/things/{id}")]
                [EventStream]
                Task<Thing> GetAsync(int id, CancellationToken ct = default);
            """;

        // Red: ignore [EventStream] on a Task<T> method.
        Assert.Equal("ZRA007", Assert.Single(Run(Api(members)).GeneratorDiagnostics).Id);
    }

    [Fact]
    public void ZRA008_EventType_not_assignable_to_the_element_type()
    {
        const string members = """
                [Post("v1/x")]
                [EventStream]
                [EventType("start", typeof(StartEvent))]
                [EventType("other", typeof(Unrelated))]
                IAsyncEnumerable<StreamEvent> StreamAsync([Body] Request body, CancellationToken ct = default);
            """;

        var run = Run(Api(members));

        // Red: skip the conversion check.
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        // Red: report it with ZRA007's AsyncEnumerableWithoutEventStream descriptor.
        Assert.Equal("ZRA008", diagnostic.Id);
        // Red: declare ZRA008 a Warning.
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // Red: pass the element type for the type argument of the message.
        Assert.Contains("typeof(MyApp.Unrelated)", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // Red: keep the method valid, and the mapping, after the conversion check fails; CS0029.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ZRA008_duplicate_event_name()
    {
        const string members = """
                [Post("v1/x")]
                [EventStream]
                [EventType("start", typeof(StartEvent))]
                [EventType("start", typeof(DeltaEvent))]
                IAsyncEnumerable<StreamEvent> StreamAsync([Body] Request body, CancellationToken ct = default);
            """;

        // Red: skip the duplicate-name check.
        var diagnostic = Assert.Single(Run(Api(members)).GeneratorDiagnostics);
        // Red: report it with ZRA007's AsyncEnumerableWithoutEventStream descriptor.
        Assert.Equal("ZRA008", diagnostic.Id);
        // Red: pass the method's name for the event name of the message.
        Assert.Contains("\"start\" more than once", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ZRA008_EventType_on_a_shape_one_method()
    {
        const string members = """
                [Get("/things/stream")]
                [EventStream]
                [EventType("start", typeof(Thing))]
                IAsyncEnumerable<ServerSentEvent<Thing>> StreamThingsAsync(CancellationToken ct = default);
            """;

        // Red: accept [EventType] on a shape one method.
        Assert.Equal("ZRA008", Assert.Single(Run(Api(members)).GeneratorDiagnostics).Id);
    }

    [Fact]
    public void ZRA008_a_shape_two_method_without_EventType()
    {
        const string members = """
                [Post("v1/x")]
                [EventStream]
                IAsyncEnumerable<StreamEvent> StreamAsync([Body] Request body, CancellationToken ct = default);
            """;

        var run = Run(Api(members));

        // Red: accept a shape two method with no [EventType], which could never yield an event.
        Assert.Equal("ZRA008", Assert.Single(run.GeneratorDiagnostics).Id);
        // Red: keep the method valid without [EventType]; the iterator then has no names to test.
        Assert.Empty(run.Problems);
    }

    [Fact]
    public void ZRA002_EventStream_ErrorType_without_a_mapper()
    {
        // Red: skip the mapper lookup for an [EventStream] ErrorType.
        var diagnostic = Assert.Single(Run(Api(Mapped)).GeneratorDiagnostics);
        // Red: report it with ZRA007's AsyncEnumerableWithoutEventStream descriptor.
        Assert.Equal("ZRA002", diagnostic.Id);
        // Red: report it with the Result message, MissingErrorMapper.
        Assert.Contains("[EventStream(ErrorType = typeof(MyApp.ApiError))]", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Existing_emission_is_unchanged()
    {
        var without = Run(Api(Plain)).GeneratedSource;
        var with = Run(Api(Plain + "\n" + ShapeTwo + "\n" + ShapeOne)).GeneratedSource;

        // Red: send Accept: text/event-stream from every method of a client with an event stream.
        Assert.Equal(
            MethodBody(without, "GetAsync") + MethodBody(without, "GetResultAsync"),
            MethodBody(with, "GetAsync") + MethodBody(with, "GetResultAsync"));
    }

    private static GeneratorHarness.GeneratorHarnessRun Run(string source)
        => GeneratorHarness.Run(source, "MyApp.IThingApi.g.cs");

    // The text of one generated method, from its signature to the next member.
    private static string MethodBody(string client, string methodName)
    {
        var start = client.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{methodName} is not generated.");
        var end = client.IndexOf("\n    p", start, StringComparison.Ordinal);
        return end < 0 ? client[start..] : client[start..end];
    }
}
