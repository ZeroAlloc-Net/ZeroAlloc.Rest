using Xunit;
using static ZeroAlloc.Rest.Tools.Tests.ModelEmitterTests;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.6: the converter filters by JSON kind, then by required properties, prefers the variant
// matching the most required properties, and applies the oneOf or anyOf rule.
public class UnionEmitterTests
{
    private static string Spec(string keyword) => $"""
                Pet:
                  type: object
                  required: [id, name]
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
                Named:
                  type: object
                  required: [name]
                  properties:
                    name:
                      type: string
                Result:
                  {keyword}:
                    - $ref: '#/components/schemas/Named'
                    - $ref: '#/components/schemas/Pet'
                    - type: integer
                      format: int64
                    - type: string
                    - type: boolean
                    - type: array
                      items:
                        type: integer
            """;

    private const string Describe = """
            var text = result.Match(
                named => "Named:" + named.Name,
                pet => "Pet:" + pet.Id,
                int64 => "Int64:" + int64,
                @string => "String:" + @string,
                boolean => "Boolean:" + boolean,
                int32List => "Int32List:" + int32List.Count);
        """;

    [Fact]
    public void Union_IsASealedRecord_WithAConverter()
    {
        var code = ModelFixture.Emit(Spec("oneOf"));

        Assert.Contains("[global::System.Text.Json.Serialization.JsonConverter(typeof(ResultConverter))]", code);
        Assert.Contains("public sealed record Result", code);
        Assert.Contains("public Pet? AsPet { get; init; }", code);
        Assert.Contains("public long? AsInt64 { get; init; }", code);
        Assert.Contains("internal sealed class ResultConverter : global::System.Text.Json.Serialization.JsonConverter<Result>", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(long))]", code);
        GeneratedCode.Compile(code).AssertClean();
    }

    [Theory]
    [InlineData("oneOf", "{\"name\":\"n\"}", "Named:n")]
    [InlineData("oneOf", "42", "Int64:42")]
    [InlineData("oneOf", "\"x\"", "String:x")]
    [InlineData("oneOf", "true", "Boolean:True")]
    [InlineData("oneOf", "[1,2,3]", "Int32List:3")]
    [InlineData("anyOf", "{\"id\":1,\"name\":\"n\"}", "Pet:1")]
    public void EachJsonKind_ReadsItsVariant_AndRoundTrips(string keyword, string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec(keyword)), Probe($$"""
            var result = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Result)!;
            {{Describe}}
            var written = JsonSerializer.Serialize(result, MyApiJsonContext.Default.Result);
            return written == {{Quote(json)}} ? text : "wrote " + written;
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Fact]
    public void OneOf_MatchingTwoVariants_IsAmbiguous()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe("""
            try
            {
                JsonSerializer.Deserialize("{\"id\":1,\"name\":\"n\"}", MyApiJsonContext.Default.Result);
                return "read";
            }
            catch (JsonException exception)
            {
                return exception.Message;
            }
            """));

        Assert.Equal("The JSON value matches more than one variant of oneOf Result.", output.RunProbe());
    }

    [Fact]
    public void AnyOf_PrefersTheVariantMatchingTheMostRequiredProperties()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("anyOf")), Probe($$"""
            var result = JsonSerializer.Deserialize("{\"id\":1,\"name\":\"n\"}", MyApiJsonContext.Default.Result)!;
            {{Describe}}
            return text;
            """));

        Assert.Equal("Pet:1", output.RunProbe());
    }

    [Theory]
    [InlineData("{\"other\":1}")]
    [InlineData("null")]
    [InlineData("1.5")]
    public void NoMatchingVariant_Throws(string json)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe($$"""
            try
            {
                var result = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Result);
                return result is null ? "null" : "read";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal(string.Equals(json, "null", StringComparison.Ordinal) ? "null" : "JsonException", output.RunProbe());
    }

    [Fact]
    public void Writing_RequiresExactlyOneValue()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe("""
            string Try(Result value)
            {
                try
                {
                    return JsonSerializer.Serialize(value, MyApiJsonContext.Default.Result);
                }
                catch (JsonException)
                {
                    return "JsonException";
                }
            }
            return Try(new Result()) + "|" + Try(new Result { AsInt64 = 1, AsBoolean = true }) + "|" + Try(new Result { AsString = "s" });
            """));

        Assert.Equal("JsonException|JsonException|\"s\"", output.RunProbe());
    }
}
