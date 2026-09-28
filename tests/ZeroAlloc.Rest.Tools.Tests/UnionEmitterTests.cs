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
    [InlineData("1.5")]
    public void NoMatchingVariant_Throws(string json)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe($$"""
            try
            {
                JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Result);
                return "read";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal("JsonException", output.RunProbe());
    }

    // No variant accepts JSON null, so the converter would throw on it: STJ reads null itself.
    [Fact]
    public void JsonNull_ReadsAsNullWithoutCallingTheConverter()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(Spec("oneOf")), Probe("""
            var result = JsonSerializer.Deserialize("null", MyApiJsonContext.Default.Result);
            return result is null ? "null" : "read";
            """));

        Assert.Equal("null", output.RunProbe());
    }

    // An enum variant reads by its JSON kind, a JsonElement variant takes whatever no earlier
    // variant does, and a union property holding JSON null is null.
    private const string MixedSpec = """
                Level:
                  type: integer
                  enum: [1, 2]
                Mixed:
                  anyOf:
                    - $ref: '#/components/schemas/Level'
                    - type: boolean
                    - {}
                Holder:
                  type: object
                  properties:
                    outcome:
                      $ref: '#/components/schemas/Mixed'
            """;

    [Theory]
    [InlineData("2", "Level:Value2")]
    [InlineData("true", "Boolean:True")]
    [InlineData("\"x\"", "JsonElement:\"x\"")]
    [InlineData("{\"a\":[1]}", "JsonElement:{\"a\":[1]}")]
    public void EnumAndJsonElementVariants_ReadAndRoundTrip(string json, string expected)
    {
        var output = GeneratedCode.Compile(EmitMixed(), Probe($$"""
            var mixed = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Mixed)!;
            var text = mixed.Match(
                level => "Level:" + level,
                boolean => "Boolean:" + boolean,
                jsonElement => "JsonElement:" + jsonElement.GetRawText());
            var written = JsonSerializer.Serialize(mixed, MyApiJsonContext.Default.Mixed);
            return written == {{Quote(json)}} ? text : "wrote " + written;
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Fact]
    public void UnionProperty_HoldingJsonNull_IsNull()
    {
        var output = GeneratedCode.Compile(EmitMixed(), Probe("""
            var holder = JsonSerializer.Deserialize("{\"outcome\":null}", MyApiJsonContext.Default.Holder)!;
            return holder.Outcome is null ? "null" : "read";
            """));

        Assert.Equal("null", output.RunProbe());
    }

    // The {} variant has no type, so it is a JsonElement, reported as ZRT002.
    private static string EmitMixed()
    {
        var (models, warnings) = ModelFixture.Build(MixedSpec);
        Assert.Equal("ZRT002", Assert.Single(warnings).Code);
        var sb = new System.Text.StringBuilder().AppendLine("#nullable enable").AppendLine("namespace MyApp;");
        ModelEmitter.Emit(sb, models);
        JsonContextEmitter.Emit(sb, ModelFixture.ContextName, ModelEmitter.SerializableTypes(models), models);
        return sb.ToString();
    }

    // Match's type parameter is named so that no model can take its name: a model named TResult
    // would otherwise be shadowed inside Match.
    [Fact]
    public void ModelNamedTResult_IsNotShadowedByMatch()
    {
        var code = ModelFixture.Emit("""
                TResult:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                Outcome:
                  oneOf:
                    - $ref: '#/components/schemas/TResult'
                    - type: string
            """);

        var output = GeneratedCode.Compile(code, Probe("""
            var outcome = JsonSerializer.Deserialize("{\"id\":3}", MyApiJsonContext.Default.Outcome)!;
            return outcome.Match(result => "TResult:" + result.Id, text => "String:" + text);
            """));

        Assert.Equal("TResult:3", output.RunProbe());
    }

    // A converter whose variants require no properties has no Required arrays, and its body starts
    // with Read, not with a blank line.
    [Fact]
    public void ConverterWithoutRequiredArrays_HasNoLeadingBlankLine()
    {
        var code = ModelFixture.Emit("""
                Scalar:
                  oneOf:
                    - type: string
                    - type: boolean
            """).ReplaceLineEndings("\n");

        Assert.Contains(
            "internal sealed class ScalarConverter : global::System.Text.Json.Serialization.JsonConverter<Scalar>\n{\n    public override Scalar Read(",
            code);
    }

    // Issue #360: variants that require the same properties are told apart by a property with a
    // single-value enum. A candidate whose such property holds another value is dropped before the
    // required-property score. The value may be a string, through a $ref or inline, an integer or
    // a boolean.
    private static string DiscriminatedSpec(string keyword) => DiscriminatedTemplate.Replace("{keyword}", keyword, StringComparison.Ordinal);

    private const string DiscriminatedTemplate = """
                LabeledName:
                  type: string
                  enum: [labeled]
                Labeled:
                  type: object
                  required: [event, id]
                  properties:
                    event:
                      $ref: '#/components/schemas/LabeledName'
                    id:
                      type: integer
                Unlabeled:
                  type: object
                  required: [event, id]
                  properties:
                    event:
                      type: string
                      enum: [unlabeled]
                    id:
                      type: integer
                Timeline:
                  {keyword}:
                    - $ref: '#/components/schemas/Labeled'
                    - $ref: '#/components/schemas/Unlabeled'
                V1:
                  type: object
                  required: [version]
                  properties:
                    version:
                      type: integer
                      enum: [1]
                V2:
                  type: object
                  required: [version]
                  properties:
                    version:
                      type: integer
                      enum: [2]
                Versioned:
                  {keyword}:
                    - $ref: '#/components/schemas/V1'
                    - $ref: '#/components/schemas/V2'
                On:
                  type: object
                  required: [enabled]
                  properties:
                    enabled:
                      type: boolean
                      enum: [true]
                Off:
                  type: object
                  required: [enabled]
                  properties:
                    enabled:
                      type: boolean
                      enum: [false]
                Toggle:
                  {keyword}:
                    - $ref: '#/components/schemas/On'
                    - $ref: '#/components/schemas/Off'
            """;

    [Theory]
    [InlineData("oneOf", "Timeline", "{\"event\":\"labeled\",\"id\":1}", "Labeled:1")]
    [InlineData("oneOf", "Timeline", "{\"event\":\"unlabeled\",\"id\":2}", "Unlabeled:2")]
    [InlineData("anyOf", "Timeline", "{\"event\":\"unlabeled\",\"id\":2}", "Unlabeled:2")]
    [InlineData("oneOf", "Versioned", "{\"version\":2}", "V2")]
    [InlineData("anyOf", "Versioned", "{\"version\":1}", "V1")]
    [InlineData("oneOf", "Toggle", "{\"enabled\":false}", "Off")]
    [InlineData("anyOf", "Toggle", "{\"enabled\":true}", "On")]
    public void SingleValueEnum_PicksTheVariantWhoseValueMatches(string keyword, string union, string json, string expected)
    {
        var match = union switch
        {
            "Timeline" => """value.Match(labeled => "Labeled:" + labeled.Id, unlabeled => "Unlabeled:" + unlabeled.Id)""",
            "Versioned" => """value.Match(v1 => "V1", v2 => "V2")""",
            _ => """value.Match(on => "On", off => "Off")""",
        };
        var output = GeneratedCode.Compile(ModelFixture.Emit(DiscriminatedSpec(keyword)), Probe($$"""
            var value = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.{{union}})!;
            var text = {{match}};
            var written = JsonSerializer.Serialize(value, MyApiJsonContext.Default.{{union}});
            return written == {{Quote(json)}} ? text : "wrote " + written;
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    // A value no variant declares, or a value of another JSON kind, matches no variant.
    [Theory]
    [InlineData("{\"event\":\"closed\",\"id\":1}")]
    [InlineData("{\"event\":1,\"id\":1}")]
    public void SingleValueEnum_WithAnotherValue_MatchesNoVariant(string json)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(DiscriminatedSpec("anyOf")), Probe($$"""
            try
            {
                JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Timeline);
                return "read";
            }
            catch (JsonException exception)
            {
                return exception.Message;
            }
            """));

        Assert.Equal("The JSON value matches no variant of Timeline.", output.RunProbe());
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
