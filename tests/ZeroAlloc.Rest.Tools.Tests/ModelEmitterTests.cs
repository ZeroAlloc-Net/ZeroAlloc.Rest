using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §10.2 and §10.3: emitted models compile with no diagnostics and round-trip through the
// generated context.
public class ModelEmitterTests
{
    private const string PetSpec = """
                Pet:
                  type: object
                  description: A pet & its <owner>.
                  required: [id, owner]
                  properties:
                    id:
                      type: integer
                      format: int64
                    name:
                      type: string
                    owner:
                      type: string
                      nullable: true
                    status:
                      type: string
                      enum: [available, sold out]
                    priority:
                      type: integer
                      enum: [1, 2]
                    born:
                      type: string
                      format: date-time
            """;

    [Fact]
    public void Record_IsASealedRecord_WithWireNamesRequiredAndNullableMembers()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("/// A pet &amp; its &lt;owner&gt;.", code);
        Assert.Contains("public sealed record Pet", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonPropertyName(\"id\")]\n    public required long Id { get; init; }", Normalize(code));
        Assert.Contains("public required string? Owner { get; init; }", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]\n    public string? Name { get; init; }", Normalize(code));
        Assert.Contains("public PetStatus? Status { get; init; }", code);
    }

    [Fact]
    public void Enums_HaveStrictConverters()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonConverter(typeof(PetStatusConverter))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonStringEnumMemberName(\"sold out\")]\n    SoldOut,", Normalize(code));
        Assert.Contains("internal sealed class PetStatusConverter : global::System.Text.Json.Serialization.JsonConverter<PetStatus>", code);
        Assert.Contains("if (reader.ValueTextEquals(\"sold out\"))\n                return PetStatus.SoldOut;", Normalize(code));
        Assert.DoesNotContain("JsonStringEnumConverter", code);
        Assert.Contains("Value1 = 1,", code);
        Assert.Contains("internal sealed class PetPriorityConverter : global::System.Text.Json.Serialization.JsonConverter<PetPriority>", code);
    }

    [Fact]
    public void Context_RegistersEveryModel()
    {
        var code = ModelFixture.Emit(PetSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(global::System.Text.Json.JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(Pet))]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(PetStatus))]", code);
        Assert.Contains("public partial class MyApiJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext", code);
    }

    [Fact]
    public void EmittedModels_CompileWithNoDiagnostics()
        => GeneratedCode.Compile(ModelFixture.Emit(PetSpec)).AssertClean();

    [Fact]
    public void Record_RoundTrips_OmittingAbsentOptionalsAndKeepingRequiredNulls()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe("""
            var pet = JsonSerializer.Deserialize("{\"id\":1,\"owner\":null,\"status\":\"sold out\",\"priority\":2,\"born\":\"2026-09-27T10:30:00+02:00\"}", MyApiJsonContext.Default.Pet)!;
            return pet.Id + "|" + (pet.Owner ?? "none") + "|" + pet.Status + "|" + pet.Priority + "|" + JsonSerializer.Serialize(pet, MyApiJsonContext.Default.Pet);
            """));

        Assert.Equal(
            "1|none|SoldOut|Value2|{\"id\":1,\"owner\":null,\"status\":\"sold out\",\"priority\":2,\"born\":\"2026-09-27T10:30:00+02:00\"}",
            output.RunProbe());
    }

    [Theory]
    [InlineData("{\"id\":1,\"owner\":null}", "ok")]
    [InlineData("{\"owner\":null}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":\"lost\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":0}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"priority\":3}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":\"available, sold out\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":\"sold out,pending\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":1}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"status\":\"AVAILABLE\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"priority\":\"1\"}", "JsonException")]
    [InlineData("{\"id\":1,\"owner\":null,\"priority\":1e30}", "JsonException")]
    public void Deserialization_IsStrict(string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe($$"""
            try
            {
                JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Pet);
                return "ok";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Theory]
    [InlineData("\"available\"", "Available")]
    [InlineData("\"sold out\"", "SoldOut")]
    public void StringEnum_RoundTripsEveryDeclaredMember(string json, string member)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe($$"""
            var value = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.PetStatus);
            return value + "|" + JsonSerializer.Serialize(value, MyApiJsonContext.Default.PetStatus);
            """));

        Assert.Equal(member + "|" + json, output.RunProbe());
    }

    [Theory]
    [InlineData("1", "Value1")]
    [InlineData("2", "Value2")]
    public void IntegerEnum_RoundTripsEveryDeclaredMember(string json, string member)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe($$"""
            var value = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.PetPriority);
            return value + "|" + JsonSerializer.Serialize(value, MyApiJsonContext.Default.PetPriority);
            """));

        Assert.Equal(member + "|" + json, output.RunProbe());
    }

    // An undefined value has no wire form: writing it throws rather than sending what the API rejects.
    [Theory]
    [InlineData("PetStatus")]
    [InlineData("PetPriority")]
    public void Enum_WritingAnUndefinedValue_Throws(string enumName)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetSpec), Probe($$"""
            try
            {
                JsonSerializer.Serialize(({{enumName}})3, MyApiJsonContext.Default.{{enumName}});
                return "written";
            }
            catch (JsonException)
            {
                return "JsonException";
            }
            """));

        Assert.Equal("JsonException", output.RunProbe());
    }

    private const string PetsSpec = """
                Pet:
                  type: object
                  required: [petType, name]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                  discriminator:
                    propertyName: petType
                    mapping:
                      cat: '#/components/schemas/Cat'
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        lives:
                          type: integer
                Dog:
                  type: object
                  properties:
                    bark:
                      type: boolean
            """;

    [Fact]
    public void Polymorphic_IsAnAbstractBase_WithDerivedTypes()
    {
        var code = ModelFixture.Emit(PetsSpec);

        Assert.Contains("[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"petType\")]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(Cat), \"cat\")]", code);
        Assert.Contains("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(Dog), \"Dog\")]", code);
        Assert.Contains("public abstract record Pet", code);
        Assert.Contains("public sealed record Cat : Pet", code);
        GeneratedCode.Compile(code).AssertClean();
    }

    [Theory]
    [InlineData("{\"petType\":\"cat\",\"name\":\"Tom\",\"lives\":9}", "Cat:Tom:9")]
    [InlineData("{\"name\":\"Rex\",\"bark\":true,\"petType\":\"Dog\"}", "Dog:Rex:True")]
    public void Polymorphic_RoundTrips_WithTheDiscriminatorAnywhere(string json, string expected)
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetsSpec), Probe($$"""
            var pet = JsonSerializer.Deserialize({{Quote(json)}}, MyApiJsonContext.Default.Pet);
            var text = pet switch
            {
                Cat cat => "Cat:" + cat.Name + ":" + cat.Lives,
                Dog dog => "Dog:" + dog.Name + ":" + dog.Bark,
                _ => "none",
            };
            var again = JsonSerializer.Deserialize(JsonSerializer.Serialize(pet, MyApiJsonContext.Default.Pet), MyApiJsonContext.Default.Pet);
            return Equals(pet, again) ? text : "round trip changed it";
            """));

        Assert.Equal(expected, output.RunProbe());
    }

    [Fact]
    public void Polymorphic_WithAnUnknownDiscriminator_Throws()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(PetsSpec), Probe("""
            try
            {
                JsonSerializer.Deserialize("{\"petType\":\"bird\",\"name\":\"Tweety\"}", MyApiJsonContext.Default.Pet);
                return "read";
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return "rejected";
            }
            """));

        Assert.Equal("rejected", output.RunProbe());
    }

    // The STJ generator names a context property after its type, List<Pet> as ListPet and Guid? as
    // NullableGuid, and gives the context its own members and per-type helpers such as PetPropInit.
    // A model named like any of those would clash, so its property is renamed; the type keeps its name.
    private const string ClashSpec = """
                Pet:
                  type: object
                  properties:
                    friends:
                      type: array
                      items:
                        $ref: '#/components/schemas/Pet'
                    ownerId:
                      type: string
                      format: uuid
                ListPet:
                  type: object
                  properties:
                    size:
                      type: integer
                NullableGuid:
                  type: object
                  properties:
                    size:
                      type: integer
                PetPropInit:
                  type: object
                  properties:
                    size:
                      type: integer
                Default:
                  type: object
                  properties:
                    size:
                      type: integer
                Options:
                  type: object
                  properties:
                    size:
                      type: integer
                Guid:
                  type: object
                  properties:
                    size:
                      type: integer
                Boolean:
                  type: object
                  properties:
                    size:
                      type: integer
            """;

    [Theory]
    [InlineData("ListPet")]
    [InlineData("NullableGuid")]
    [InlineData("PetPropInit")]
    [InlineData("Default")]
    [InlineData("Options")]
    [InlineData("Guid")]
    public void Context_RenamesTheProperty_OfAModelNamedLikeAnotherContextMember(string model)
        => Assert.Contains(
            $"[global::System.Text.Json.Serialization.JsonSerializable(typeof({model}), TypeInfoPropertyName = \"{model}Model\")]",
            ModelFixture.Emit(ClashSpec));

    // No bool is reachable, so nothing else is named Boolean.
    [Fact]
    public void Context_KeepsTheProperty_OfAModelNamedLikeAnUnreachableType()
        => Assert.Contains("[global::System.Text.Json.Serialization.JsonSerializable(typeof(Boolean))]", ModelFixture.Emit(ClashSpec));

    [Fact]
    public void ModelsNamedLikeContextMembers_CompileClean_AndRoundTrip()
    {
        var output = GeneratedCode.Compile(ModelFixture.Emit(ClashSpec), Probe("""
            var pet = JsonSerializer.Deserialize("{\"friends\":[{\"ownerId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"}]}", MyApiJsonContext.Default.Pet)!;
            var list = JsonSerializer.Deserialize("{\"size\":1}", MyApiJsonContext.Default.ListPetModel)!;
            var @default = JsonSerializer.Deserialize("{\"size\":2}", MyApiJsonContext.Default.DefaultModel)!;
            var options = JsonSerializer.Deserialize("{\"size\":3}", MyApiJsonContext.Default.OptionsModel)!;
            return JsonSerializer.Serialize(pet, MyApiJsonContext.Default.Pet) + "|" + list.Size + @default.Size + options.Size;
            """));

        Assert.Equal("{\"friends\":[{\"ownerId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"}]}|123", output.RunProbe());
    }

    // A probe whose Run() body is the given statements, in namespace MyApp.
    internal static string Probe(string body) => $$"""
        using System;
        using System.Text.Json;
        using MyApp;

        public static class Probe
        {
            public static string Run()
            {
        {{body}}
            }
        }
        """;

    internal static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Normalize(string code) => code.Replace("\r\n", "\n", StringComparison.Ordinal);
}
