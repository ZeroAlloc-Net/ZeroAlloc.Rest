using Microsoft.OpenApi.Models;
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.1: one test per row, plus nullability.
public class TypeMapperTests
{
    [Theory]
    [InlineData("integer", null, "int")]
    [InlineData("integer", "int32", "int")]
    [InlineData("integer", "int64", "long")]
    [InlineData("number", null, "double")]
    [InlineData("number", "double", "double")]
    [InlineData("number", "float", "float")]
    [InlineData("number", "decimal", "decimal")]
    [InlineData("string", null, "string")]
    [InlineData("string", "date-time", "global::System.DateTimeOffset")]
    [InlineData("string", "date", "global::System.DateOnly")]
    [InlineData("string", "time", "global::System.TimeOnly")]
    [InlineData("string", "uuid", "global::System.Guid")]
    [InlineData("string", "uri", "global::System.Uri")]
    [InlineData("string", "byte", "byte[]")]
    [InlineData("string", "binary", "string")]
    [InlineData("boolean", null, "bool")]
    public void Primitive_MapsByTypeAndFormat(string type, string? format, string expected)
    {
        var namer = new RecordingNamer();

        var mapped = TypeMapper.Map(new OpenApiSchema { Type = type, Format = format }, "Ctx", "#/x", namer);

        Assert.Equal(expected, mapped.Name);
        Assert.Empty(namer.Calls);
        Assert.Empty(namer.Reports);
    }

    [Fact]
    public void Binary_InABody_IsAStream()
    {
        var mapped = TypeMapper.Map(new OpenApiSchema { Type = "string", Format = "binary" }, "Ctx", "#/x", new RecordingNamer(), isBody: true);

        Assert.Equal(TypeRef.Stream, mapped);
    }

    [Fact]
    public void Array_IsAListOfItsItems()
    {
        var schema = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "integer", Format = "int64" } };

        Assert.Equal("global::System.Collections.Generic.List<long>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void Array_OfNullableValueTypes_AnnotatesTheElement()
    {
        var schema = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "integer", Nullable = true } };

        Assert.Equal("global::System.Collections.Generic.List<int?>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void ObjectWithAdditionalPropertiesOnly_IsADictionary()
    {
        var schema = new OpenApiSchema { Type = "object", AdditionalProperties = new OpenApiSchema { Type = "string" } };

        Assert.Equal("global::System.Collections.Generic.Dictionary<string, string>", TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()).Name);
    }

    [Fact]
    public void InlineObjectWithoutProperties_IsADictionaryOfJsonElement()
    {
        var mapped = TypeMapper.Map(new OpenApiSchema { Type = "object" }, "Ctx", "#/x", new RecordingNamer());

        Assert.Equal("global::System.Collections.Generic.Dictionary<string, global::System.Text.Json.JsonElement>", mapped.Name);
    }

    [Fact]
    public void Ref_IsNamedByTheNamer()
    {
        var namer = new RecordingNamer();
        var schema = new OpenApiSchema
        {
            Type = "object",
            Reference = new OpenApiReference { Id = "Pet", Type = ReferenceType.Schema },
            Properties = { ["id"] = new OpenApiSchema { Type = "integer" } },
        };

        var mapped = TypeMapper.Map(schema, "Ctx", "#/x", namer);

        Assert.Equal("Pet", mapped.Name);
        Assert.Equal(("Ctx", "#/x"), Assert.Single(namer.Calls));
    }

    [Fact]
    public void RefToAPrimitive_MapsToThePrimitive()
    {
        var schema = new OpenApiSchema
        {
            Type = "string",
            Format = "uuid",
            Reference = new OpenApiReference { Id = "Id", Type = ReferenceType.Schema },
        };

        Assert.Equal(TypeRef.Guid, TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    public void Enum_IsNamedByTheNamer(string type)
    {
        var namer = new RecordingNamer();
        var schema = new OpenApiSchema { Type = type, Enum = { new Microsoft.OpenApi.Any.OpenApiString("a") } };

        TypeMapper.Map(schema, "Ctx", "#/x", namer);

        Assert.Single(namer.Calls);
    }

    [Fact]
    public void SingleAllOfWrapper_MapsToItsPart()
    {
        var schema = new OpenApiSchema { Nullable = true, AllOf = { new OpenApiSchema { Type = "boolean" } } };

        Assert.Equal(TypeRef.Bool, TypeMapper.Map(schema, "Ctx", "#/x", new RecordingNamer()));
    }

    [Fact]
    public void Not_IsJsonElement_AndReported()
    {
        var namer = new RecordingNamer();

        var mapped = TypeMapper.Map(new OpenApiSchema { Not = new OpenApiSchema { Type = "string" } }, "Ctx", "#/x", namer);

        Assert.Equal(TypeRef.JsonElement, mapped);
        Assert.Equal(("#/x", "it uses 'not', which has no C# equivalent"), Assert.Single(namer.Reports));
    }

    [Fact]
    public void NoTypeAndNoComposition_IsJsonElement_AndReported()
    {
        var namer = new RecordingNamer();

        Assert.Equal(TypeRef.JsonElement, TypeMapper.Map(new OpenApiSchema(), "Ctx", "#/x", namer));
        Assert.Equal(("#/x", "it has neither a type nor a composition"), Assert.Single(namer.Reports));
    }

    [Theory]
    [InlineData(true, false, "long")]
    [InlineData(true, true, "long?")]
    [InlineData(false, false, "long?")]
    [InlineData(false, true, "long?")]
    public void Declare_AppliesRequiredAndNullable(bool required, bool nullable, string expected)
        => Assert.Equal(expected, TypeMapper.Declare(TypeRef.Long, required, nullable));

    [Fact]
    public void Declare_AnnotatesReferenceTypes()
        => Assert.Equal("string?", TypeMapper.Declare(TypeRef.String, required: false, nullable: false));

    // Records what TypeMapper asks for: Calls for each schema it wants named, Reports for each ZRT002.
    private sealed class RecordingNamer : ISchemaTypeNamer
    {
        public List<(string ContextName, string Path)> Calls { get; } = [];

        public List<(string Path, string Reason)> Reports { get; } = [];

        public TypeRef Named(OpenApiSchema schema, string contextName, string path)
        {
            Calls.Add((contextName, path));
            return new TypeRef(schema.Reference?.Id ?? contextName, TypeRefKind.Model, TypeMapper.IsEnum(schema));
        }

        public void Unsupported(string path, string reason) => Reports.Add((path, reason));
    }
}
