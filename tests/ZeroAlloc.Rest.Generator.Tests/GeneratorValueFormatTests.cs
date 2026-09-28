using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// Design decision 3 of the OpenAPI models plan: route, query and header values are written in
// their wire format through one __FormatValue overload per value type.
public class GeneratorValueFormatTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.Rest.Attributes;
        namespace MyApp;
        public enum Mood { [JsonStringEnumMemberName("very-happy")] VeryHappy, Sad }
        [ZeroAllocRestClient]
        public interface IFormatApi
        {
            [Get("/at/{when}")]
            Task<string> AtAsync(DateTimeOffset when, [Query] bool active, [Query] Mood mood,
                [Query] int? limit, [Query] List<DateOnly> days, [Header("X-Trace")] string? trace,
                CancellationToken ct = default);
        }
        """;

    [Fact]
    public void EachValueType_GetsOneFormatHelper()
    {
        var client = Generate(Source);

        Assert.Contains("private static string __FormatValue(global::System.DateTimeOffset value) => value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture);", client);
        Assert.Contains("private static string __FormatValue(bool value) => value ? \"true\" : \"false\";", client);
        Assert.Contains("global::MyApp.Mood.VeryHappy => \"very-happy\",", client);
        Assert.Contains("private static string __FormatValue(int? value) => value.HasValue ? __FormatValue(value.GetValueOrDefault()) : string.Empty;", client);
        Assert.Contains("private static string __FormatValue(global::System.DateOnly value)", client);
        Assert.Contains("private static string __FormatValue(string value) => value;", client);
        Assert.Equal(1, Occurrences(client, "__FormatValue(bool value)"));
    }

    [Fact]
    public void CallSites_UseTheHelper()
    {
        var client = Generate(Source);

        Assert.Contains("{(global::System.Uri.EscapeDataString(__FormatValue(when)))}", client);
        Assert.Contains("global::System.Uri.EscapeDataString(__FormatValue(__item))", client);
        Assert.Contains("global::System.Uri.EscapeDataString(__FormatValue(limit))", client);
        Assert.Contains("global::System.Uri.EscapeDataString(__FormatValue(active))", client);
        Assert.Contains("if (trace is not null)\n            __request.Headers.TryAddWithoutValidation(\"X-Trace\", __FormatValue(trace));", client);
        Assert.DoesNotContain(".ToString()!", client);
    }

    // Ruling F1: a value-type element can never be null, and `__item == null` on it is CS8073 or
    // CS0472, an error under TreatWarningsAsErrors. Only an element that can hold null is checked.
    [Fact]
    public void ValueTypeCollectionElements_GetNoNullCheck_AndCompileWithoutWarnings()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IListApi
            {
                [Get("/a")] Task<string> IntsAsync([Query] List<int> ids);
                [Get("/b")] Task<string> NullableIntsAsync([Query] int?[] maybe);
                [Get("/c")] Task<string> StringsAsync([Query] IEnumerable<string?> tags);
            }
            """, "IListApi.g.cs");

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        var ints = Section(client, "IntsAsync", "NullableIntsAsync");
        Assert.DoesNotContain("if (__item == null) continue;", ints);
        Assert.Contains("if (__item == null) continue;", Section(client, "NullableIntsAsync", "StringsAsync"));
        Assert.Contains("if (__item == null) continue;", Section(client, "StringsAsync", "__FormatValue("));
    }

    // Issue #356: a collection-typed [Header] parameter sent its collection's type name, such as
    // System.String[], as the header value. Each element is added as its own value of the header,
    // formatted like a single value; a null collection or a null element adds nothing.
    [Fact]
    public void CollectionHeaders_AddOneValuePerElement_AndCompileWithoutWarnings()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            [ZeroAllocRestClient]
            public interface IHeaderListApi
            {
                [Get("/a")] Task<string> IntsAsync([Header("X-Ids")] List<int> ids);
                [Get("/b")] Task<string> NullableIntsAsync([Header("X-Maybe")] int?[]? maybe);
                [Get("/c")] Task<string> StringsAsync([Header("X-Tags")] IEnumerable<string?> tags);
            }
            """, "IHeaderListApi.g.cs");

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        Assert.DoesNotContain("__FormatValue(ids)", client);
        Assert.DoesNotContain("__FormatValue(tags)", client);
        Assert.DoesNotContain("List<int> value)", client);
        Assert.DoesNotContain("[] value)", client);
        Assert.Contains("private static string __FormatValue(int value)", client);
        Assert.Contains("private static string __FormatValue(string value) => value;", client);

        var ints = Section(client, "IntsAsync", "NullableIntsAsync");
        Assert.Contains("foreach (var __item in ids)", ints);
        Assert.Contains("__request.Headers.TryAddWithoutValidation(\"X-Ids\", __FormatValue(__item));", ints);
        Assert.DoesNotContain("if (__item == null) continue;", ints);
        var maybe = Section(client, "NullableIntsAsync", "StringsAsync");
        Assert.Contains("if (maybe != null)", maybe);
        Assert.Contains("if (__item == null) continue;", maybe);
        var strings = Section(client, "StringsAsync", "private static string __FormatValue(");
        Assert.Contains("if (__item == null) continue;", strings);
        Assert.Contains("__request.Headers.TryAddWithoutValidation(\"X-Tags\", __FormatValue(__item));", strings);
    }

    // Ruling F7: a spec can define a model named Uri in the client's namespace, so the escape call
    // is fully qualified. Enum members with a keyword name or sharing a value also compile.
    [Fact]
    public void ModelNamedUri_KeywordEnumMember_AndAliasedEnumMembers_Compile()
    {
        var run = GeneratorHarness.Run("""
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            public sealed class Uri { }
            public enum Kind
            {
                [JsonStringEnumMemberName("class")] @class,
                [JsonStringEnumMemberName("first")] First = 1,
                [JsonStringEnumMemberName("alias")] Alias = 1,
            }
            [ZeroAllocRestClient]
            public interface IKindApi
            {
                [Get("/k/{kind}")] Task<string> GetAsync(Kind kind, [Query] Kind other, [Header("X-Kind")] Kind? header);
            }
            """, "IKindApi.g.cs");

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("global::MyApp.Kind.@class => \"class\",", run.GeneratedSource);
        Assert.Contains("global::MyApp.Kind.First => \"first\",", run.GeneratedSource);
        Assert.DoesNotContain("global::MyApp.Kind.Alias", run.GeneratedSource);
    }

    // Fix round 1: a plain struct binds ValueType.ToString, annotated string?, so the helper
    // coalesces; an explicit IFormattable is called through the interface; a [Flags] combination is
    // built from the member names; nullable route and query values bind the Nullable<T> overload.
    [Fact]
    public void PlainStruct_ExplicitFormattable_FlagsEnum_AndNullableValues_CompileWithoutWarnings()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using ZeroAlloc.Rest.Attributes;
            namespace MyApp;
            public struct Plain { public int X; }
            public struct Code : IFormattable
            {
                string IFormattable.ToString(string? format, IFormatProvider? provider) => "code";
            }
            public sealed class Ref : IFormattable
            {
                string IFormattable.ToString(string? format, IFormatProvider? provider) => "ref";
            }
            [Flags]
            public enum Perm : byte { None = 0, [JsonStringEnumMemberName("r")] Read = 1, Write = 2, [JsonStringEnumMemberName("rw")] ReadWrite = 3, Exec = 4 }
            [ZeroAllocRestClient]
            public interface IMixApi
            {
                [Get("/p/{plain}")] Task<string> PlainAsync(Plain plain, [Query] Plain other);
                [Get("/c/{code}")] Task<string> CodeAsync(Code code, [Query] Code? maybe, [Header("X-Ref")] Ref? reference);
                [Get("/f")] Task<string> FlagsAsync([Query] Perm perm, [Query] Perm[] perms);
                [Get("/n/{when}")] Task<string> NullableAsync(DateTimeOffset? when, [Query] bool? flag);
            }
            """, "IMixApi.g.cs");

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = run.GeneratedSource;
        Assert.Contains("private static string __FormatValue(global::MyApp.Plain value) => value.ToString() ?? string.Empty;", client);
        Assert.Contains("private static string __FormatValue(global::MyApp.Code value) => __FormatFormattable(value);", client);
        Assert.Contains("private static string __FormatValue(global::MyApp.Ref value) => __FormatFormattable(value);", client);
        Assert.Contains("private static string __FormatFormattable<T>(T value) where T : global::System.IFormattable", client);
        Assert.Contains("private static string __FormatValue(global::MyApp.Perm value) => __FormatFlags(value);", client);
        Assert.Contains("case global::MyApp.Perm.ReadWrite: return \"rw\";", client);
        Assert.Contains("return __bits == 0 && __text is not null ? __text : ((byte)value).ToString(global::System.Globalization.CultureInfo.InvariantCulture);", client);
        Assert.Contains("{(global::System.Uri.EscapeDataString(__FormatValue(when)))}", client);
        Assert.Contains("private static string __FormatValue(bool? value)", client);
    }

    private static string Generate(string source)
    {
        var run = GeneratorHarness.Run(source, "IFormatApi.g.cs");
        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        return run.GeneratedSource;
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, System.StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Section(string text, string from, string to)
    {
        var start = text.IndexOf(from, System.StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, System.StringComparison.Ordinal);
        return text.Substring(start, end - start);
    }
}
