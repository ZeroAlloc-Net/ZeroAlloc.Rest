using Xunit;
using static ZeroAlloc.Rest.Tools.Tests.ModelFixture;

namespace ZeroAlloc.Rest.Tools.Tests;

// Spec §5.2 to §5.7 and §10.1: the intermediate model is value-equal, so each test states the
// whole model it expects.
public class SchemaModelBuilderTests
{
    [Fact]
    public void ObjectSchema_IsARecord_WithRequiredOptionalAndNullableProperties()
    {
        var (models, warnings) = Build("""
                Pet:
                  type: object
                  description: A pet.
                  required: [id, owner]
                  properties:
                    id:
                      type: integer
                      format: int64
                    name:
                      type: string
                      description: Its <name>.
                    owner:
                      type: string
                      nullable: true
            """);

        Assert.Empty(warnings);
        Assert.Equal(
            List<ModelDefinition>(new RecordModel("Pet", "A pet.", List(
                Required("Id", "id", TypeRef.Long),
                new PropertyModel("Name", "name", TypeRef.String, Required: false, Nullable: false, Description: "Its <name>."),
                new PropertyModel("Owner", "owner", TypeRef.String, Required: true, Nullable: true, Description: null)))),
            models);
    }

    [Fact]
    public void InlineObject_IsNamedFromItsPath()
    {
        var (models, _) = Build("""
                User:
                  type: object
                  properties:
                    address:
                      type: object
                      properties:
                        city:
                          type: string
            """);

        Assert.Equal(
            List<ModelDefinition>(
                new RecordModel("User", null, List(Optional("Address", "address", Model("UserAddress")))),
                new RecordModel("UserAddress", null, List(Optional("City", "city", TypeRef.String)))),
            models);
    }

    [Fact]
    public void NameCollisions_GetANumericSuffix()
    {
        var (models, _) = Build("""
                User:
                  type: object
                  properties:
                    address:
                      type: object
                      properties:
                        city:
                          type: string
                UserAddress:
                  type: object
                  properties:
                    line:
                      type: string
                MyApiJsonContext:
                  type: object
                  properties:
                    x:
                      type: string
            """);

        Assert.Equal(
            new[] { "User", "UserAddress", "MyApiJsonContext2", "UserAddress2" },
            models.Select(m => m.Name),
            StringComparer.Ordinal);
    }

    [Fact]
    public void PropertyNamedLikeItsRecordOrARecordMember_GetsANumericSuffix()
    {
        var (models, _) = Build("""
                Error:
                  type: object
                  properties:
                    error:
                      type: string
                    toString:
                      type: string
            """);

        var record = Assert.IsType<RecordModel>(Assert.Single(models));
        Assert.Equal(new[] { "Error2", "ToString2" }, record.Properties.Select(p => p.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void EmptyComponentObject_IsAnEmptyRecord()
    {
        var (models, _) = Build("""
                Marker:
                  type: object
            """);

        Assert.Equal(List<ModelDefinition>(new RecordModel("Marker", null, EquatableList<PropertyModel>.Empty)), models);
    }

    [Fact]
    public void StringEnum_KeepsWireValues_AndSanitisesNames()
    {
        var (models, _) = Build("""
                Status:
                  type: string
                  enum: [available, pending review, "", "2fa"]
            """);

        Assert.Equal(
            List<ModelDefinition>(new EnumModel("Status", null, IsString: true, "int", List(
                new EnumMemberModel("Available", "available"),
                new EnumMemberModel("PendingReview", "pending review"),
                new EnumMemberModel("Empty", ""),
                new EnumMemberModel("_2fa", "2fa")))),
            models);
    }

    [Fact]
    public void IntegerEnum_NamesMembersByValue_OrByXEnumVarnames()
    {
        var (models, _) = Build("""
                Priority:
                  type: integer
                  enum: [1, 2, -3]
                Level:
                  type: integer
                  format: int64
                  enum: [10, 20]
                  x-enum-varnames: [Low, High]
            """);

        Assert.Equal(
            List<ModelDefinition>(
                new EnumModel("Priority", null, IsString: false, "int", List(
                    new EnumMemberModel("Value1", "1"),
                    new EnumMemberModel("Value2", "2"),
                    new EnumMemberModel("ValueMinus3", "-3"))),
                new EnumModel("Level", null, IsString: false, "long", List(
                    new EnumMemberModel("Low", "10"),
                    new EnumMemberModel("High", "20")))),
            models);
    }

    [Fact]
    public void EnumProperty_IsAValueTypeModel()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  properties:
                    status:
                      type: string
                      enum: [a, b]
            """);

        Assert.Equal(Optional("Status", "status", Enum("PetStatus")), ((RecordModel)models[0]).Properties[0]);
        Assert.IsType<EnumModel>(models[1]);
    }

    [Fact]
    public void UnsupportedSchema_IsJsonElement_WithZrt002()
    {
        var (models, warnings) = Build("""
                Holder:
                  type: object
                  properties:
                    anything: {}
                    notString:
                      not:
                        type: string
            """);

        Assert.Equal(
            new[] { TypeRef.JsonElement, TypeRef.JsonElement },
            ((RecordModel)models[0]).Properties.Select(p => p.Type));
        Assert.Equal(new[] { "ZRT002", "ZRT002" }, warnings.Select(w => w.Code), StringComparer.Ordinal);
        Assert.Equal(
            "Schema '#/components/schemas/Holder/properties/anything' is mapped to JsonElement, because it has neither a type nor a composition.",
            warnings[0].Message);
    }

    [Fact]
    public void Models_AreValueEqual_AcrossBuilds()
    {
        const string Spec = """
                Pet:
                  type: object
                  properties:
                    tags:
                      type: array
                      items:
                        type: string
            """;

        Assert.Equal(Build(Spec).Models, Build(Spec).Models);
    }

    [Fact]
    public void AllOf_IsFlattened_PartsFirst_RequiredIfAnyPartRequiresIt()
    {
        var (models, _) = Build("""
                NewPet:
                  type: object
                  required: [name]
                  properties:
                    name:
                      type: string
                    tag:
                      type: string
                Pet:
                  allOf:
                    - $ref: '#/components/schemas/NewPet'
                    - type: object
                      required: [id, tag]
                      properties:
                        id:
                          type: integer
                          format: int64
            """);

        Assert.Equal(
            new RecordModel("Pet", null, List(
                Required("Name", "name", TypeRef.String),
                Required("Tag", "tag", TypeRef.String),
                Required("Id", "id", TypeRef.Long))),
            models[1]);
    }

    [Fact]
    public void AllOf_WithConflictingPropertyTypes_IsAGenerationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("""
                A:
                  type: object
                  properties:
                    id:
                      type: integer
                B:
                  allOf:
                    - $ref: '#/components/schemas/A'
                    - type: object
                      properties:
                        id:
                          type: string
            """));

        Assert.Equal(
            "Schema 'B': property 'id' has conflicting types in its allOf parts, 'integer' and 'string'.",
            error.Message);
    }

    [Fact]
    public void AllOf_WithTheSamePropertyTwice_KeepsOne()
    {
        var (models, _) = Build("""
                B:
                  allOf:
                    - type: object
                      properties:
                        id:
                          type: integer
                    - type: object
                      required: [id]
                      properties:
                        id:
                          type: integer
            """);

        Assert.Equal(List(Required("Id", "id", TypeRef.Int)), ((RecordModel)models[0]).Properties);
    }

    [Fact]
    public void RecursiveAllOf_IsJsonElement_WithZrt002()
    {
        var (models, warnings) = Build("""
                Holder:
                  type: object
                  properties:
                    loop:
                      $ref: '#/components/schemas/A'
                A:
                  allOf:
                    - $ref: '#/components/schemas/B'
                B:
                  allOf:
                    - $ref: '#/components/schemas/A'
            """);

        Assert.Equal(TypeRef.JsonElement, ((RecordModel)models[0]).Properties[0].Type);
        Assert.DoesNotContain(models, m => m.Name is "A" or "B");
        Assert.Contains(warnings, w => w.Message.Contains("its allOf refers back to itself", StringComparison.Ordinal));
    }
}
