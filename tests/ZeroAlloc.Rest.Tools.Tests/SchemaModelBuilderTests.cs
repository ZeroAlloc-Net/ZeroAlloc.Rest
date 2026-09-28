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

    // A named schema is reported at its own path, not at the operation or property that first
    // reaches it.
    [Fact]
    public void RecursiveAllOf_IsReportedAtTheNamedSchemasPath()
    {
        const string Spec = """
            openapi: 3.0.0
            info:
              title: Loop
              version: "1"
            paths:
              /a:
                get:
                  operationId: getA
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/A'
            components:
              schemas:
                A:
                  allOf:
                    - $ref: '#/components/schemas/B'
                B:
                  allOf:
                    - $ref: '#/components/schemas/A'
            """;
        var warnings = new List<OpenApiDiagnostic>();

        OpenApiInterfaceGenerator.Generate(Spec, "MyApp", "ILoopApi", warnings, GenerationOptions.Default);

        Assert.Equal(
            "Schema '#/components/schemas/A' is mapped to JsonElement, because its allOf refers back to itself.",
            Assert.Single(warnings).Message);
    }

    // A variant inherits every property of its base, those of the base's allOf parts included, so it
    // never declares one again.
    [Fact]
    public void Variant_DoesNotRedeclareAPropertyTheBaseTakesFromAnAllOfPart()
    {
        const string Spec = """
                Pet:
                  type: object
                  required: [kind]
                  allOf:
                    - $ref: '#/components/schemas/Named'
                  properties:
                    kind:
                      type: string
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                Named:
                  type: object
                  properties:
                    name:
                      type: string
                Cat:
                  type: object
                  properties:
                    name:
                      type: string
                    meow:
                      type: boolean
            """;

        var (models, _) = Build(Spec);

        Assert.Equal(
            new RecordModel("Cat", null, List(Optional("Meow", "meow", TypeRef.Bool)), BaseName: "Pet"),
            models.Single(m => string.Equals(m.Name, "Cat", StringComparison.Ordinal)));
        GeneratedCode.Compile(Emit(Spec)).AssertClean();
    }

    private const string Pets = """
                Pet:
                  type: object
                  required: [petType]
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
                    petType:
                      type: string
                    bark:
                      type: boolean
            """;

    [Fact]
    public void Discriminator_MakesAnAbstractBase_WithOneDerivedTypePerVariant()
    {
        var (models, _) = Build(Pets);

        Assert.Equal(
            new PolymorphicModel("Pet", null, "petType",
                List(Optional("Name", "name", TypeRef.String)),
                List(new DerivedTypeModel("Cat", "cat"), new DerivedTypeModel("Dog", "Dog"))),
            models[0]);
    }

    [Fact]
    public void Variants_DeriveFromTheBase_WithoutTheDiscriminatorOrInheritedProperties()
    {
        var (models, _) = Build(Pets);

        Assert.Equal(new RecordModel("Cat", null, List(Optional("Lives", "lives", TypeRef.Int)), BaseName: "Pet"), models[1]);
        Assert.Equal(new RecordModel("Dog", null, List(Optional("Bark", "bark", TypeRef.Bool)), BaseName: "Pet"), models[2]);
    }

    [Fact]
    public void Variant_ReferencedBeforeItsBase_StillDerivesFromIt()
    {
        var document = ModelFixture.Parse(Pets);
        var builder = new SchemaModelBuilder(document, ModelFixture.ReservedNames, []);

        TypeMapper.Map(document.Components.Schemas["Dog"], "Dog", "#/components/schemas/Dog", builder);
        var models = builder.Build();

        Assert.Equal("Pet", Assert.IsType<RecordModel>(models[0]).BaseName);
        Assert.Contains(models, m => m is PolymorphicModel { Name: "Pet" });
    }

    [Fact]
    public void VariantOfTwoBases_IsAGenerationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("""
                A:
                  required: [kind]
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/V'
                B:
                  required: [kind]
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/V'
                V:
                  type: object
                  properties:
                    x:
                      type: string
            """));

        Assert.Equal("Schema 'V' is a variant of both 'A' and 'B'; a C# record has one base type.", error.Message);
    }

    [Fact]
    public void OneOfWithoutDiscriminator_IsAUnion_WithKindsAndRequiredProperties()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  required: [name, id]
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
                Error:
                  type: object
                  required: [code]
                  properties:
                    code:
                      type: string
                Result:
                  oneOf:
                    - $ref: '#/components/schemas/Pet'
                    - $ref: '#/components/schemas/Error'
                    - type: integer
                      format: int64
                    - type: array
                      items:
                        type: string
            """);

        Assert.Equal(
            new UnionModel("Result", null, IsOneOf: true, List(
                new UnionVariantModel("Pet", Model("Pet"), JsonKind.Object, List("id", "name"), NoValues),
                new UnionVariantModel("Error", Model("Error"), JsonKind.Object, List("code"), NoValues),
                new UnionVariantModel("Int64", TypeRef.Long, JsonKind.Number, EquatableList<string>.Empty, NoValues),
                new UnionVariantModel("StringList", new TypeRef("global::System.Collections.Generic.List<string>", TypeRefKind.List, false), JsonKind.Array, EquatableList<string>.Empty, NoValues))),
            models[2]);
    }

    // A map variant is named after its value type, the way a list variant is: Int32Map.
    [Fact]
    public void MapVariant_IsNamedAfterItsValueType()
    {
        var (models, _) = Build("""
                Counts:
                  oneOf:
                    - type: object
                      additionalProperties:
                        type: integer
                    - type: string
            """);

        var union = Assert.IsType<UnionModel>(models.Single());
        Assert.Equal(new[] { "Int32Map", "String" }, union.Variants.Select(v => v.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void InlineUnionOfRefs_IsNamedAfterItsVariants()
    {
        var (models, _) = Build("""
                Pet:
                  type: object
                  properties:
                    id:
                      type: integer
                Error:
                  type: object
                  properties:
                    code:
                      type: string
                Holder:
                  type: object
                  properties:
                    outcome:
                      anyOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal("PetOrError", union.Name);
        Assert.False(union.IsOneOf);
    }

    [Fact]
    public void UnionVariant_RequiredPropertiesIncludeThoseOfItsAllOfParts()
    {
        var (models, _) = Build("""
                Base:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                Derived:
                  allOf:
                    - $ref: '#/components/schemas/Base'
                    - type: object
                      required: [extra]
                      properties:
                        extra:
                          type: string
                Either:
                  oneOf:
                    - $ref: '#/components/schemas/Derived'
                    - type: string
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(List("extra", "id"), union.Variants[0].RequiredWireNames);
        Assert.Equal(new UnionVariantModel("String", TypeRef.String, JsonKind.String, EquatableList<string>.Empty, NoValues), union.Variants[1]);
    }

    // A single-part wrapper around a $ref stands for it, but the properties it requires still decide
    // whether a JSON object matches the variant.
    [Theory]
    [InlineData("allOf")]
    [InlineData("anyOf")]
    public void UnionVariant_RequiredPropertiesIncludeThoseItsWrapperAdds(string wrapper)
    {
        var (models, _) = Build($$"""
                Pet:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
                Either:
                  oneOf:
                    - {{wrapper}}:
                        - $ref: '#/components/schemas/Pet'
                      required: [name]
                    - type: string
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(new UnionVariantModel("Pet", Model("Pet"), JsonKind.Object, List("id", "name"), NoValues), union.Variants[0]);
    }

    [Fact]
    public void EnumVariant_TakesItsJsonKindFromItsType()
    {
        var (models, _) = Build("""
                Code:
                  type: integer
                  enum: [1, 2]
                Either:
                  oneOf:
                    - $ref: '#/components/schemas/Code'
                    - type: boolean
            """);

        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(new[] { JsonKind.Number, JsonKind.Boolean }, union.Variants.Select(v => v.Kind));
    }

    // Issue #359: structurally identical inline unions share one type. The first occurrence names it.
    private const string PetAndError = """
                Pet:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: integer
                Error:
                  type: object
                  required: [code]
                  properties:
                    code:
                      type: string
            """;

    [Fact]
    public void IdenticalInlineUnionsOfRefs_ShareOneType()
    {
        var (models, warnings) = Build(PetAndError + Environment.NewLine + """
                A:
                  type: object
                  properties:
                    outcome:
                      oneOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
                B:
                  type: object
                  description: B.
                  properties:
                    result:
                      oneOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
            """);

        Assert.Empty(warnings);
        var union = Assert.IsType<UnionModel>(Assert.Single(models, m => m is UnionModel));
        Assert.Equal("PetOrError", union.Name);
        Assert.Equal(Model("PetOrError"), Record(models, "A").Properties.Single().Type);
        Assert.Equal(Model("PetOrError"), Record(models, "B").Properties.Single().Type);
    }

    // The union's own name comes from the first path that reaches it; a later path refers to it.
    [Fact]
    public void IdenticalInlineUnions_AreNamedByTheFirstOccurrence()
    {
        var (models, _) = Build("""
                A:
                  type: object
                  properties:
                    value:
                      description: First.
                      anyOf:
                        - type: string
                        - type: integer
                B:
                  type: object
                  properties:
                    value:
                      description: Second.
                      nullable: true
                      anyOf:
                        - type: string
                        - type: integer
            """);

        var union = Assert.IsType<UnionModel>(Assert.Single(models, m => m is UnionModel));
        Assert.Equal("AValue", union.Name);
        Assert.Equal("First.", union.Description);
        Assert.Equal(Model("AValue"), Record(models, "B").Properties.Single().Type);
        Assert.True(Record(models, "B").Properties.Single().Nullable);
    }

    [Theory]
    [InlineData("oneOf", "anyOf")]
    [InlineData("oneOf", "oneOf")]
    public void InlineUnions_DifferingInKindOrOrder_StayApart(string first, string second)
    {
        var reversed = string.Equals(first, second, StringComparison.Ordinal);
        var (models, _) = Build(PetAndError + Environment.NewLine + $$"""
                A:
                  type: object
                  properties:
                    outcome:
                      {{first}}:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
                B:
                  type: object
                  properties:
                    outcome:
                      {{second}}:
                        - $ref: '#/components/schemas/{{(reversed ? "Error" : "Pet")}}'
                        - $ref: '#/components/schemas/{{(reversed ? "Pet" : "Error")}}'
            """);

        Assert.Equal(2, models.Count(m => m is UnionModel));
    }

    // A variant that is a wrapper around a $ref may add required properties, which change how the
    // converter matches a JSON object. Such a union is never merged with the plain one.
    [Fact]
    public void InlineUnions_DifferingInRequiredProperties_StayApart()
    {
        var (models, _) = Build(PetAndError + Environment.NewLine + """
                A:
                  type: object
                  properties:
                    outcome:
                      oneOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
                B:
                  type: object
                  properties:
                    outcome:
                      oneOf:
                        - allOf:
                            - $ref: '#/components/schemas/Pet'
                          required: [name]
                        - $ref: '#/components/schemas/Error'
                C:
                  type: object
                  properties:
                    first:
                      oneOf:
                        - type: object
                          required: [id]
                          properties:
                            id:
                              type: integer
                        - type: string
                    second:
                      oneOf:
                        - type: object
                          required: [name]
                          properties:
                            id:
                              type: integer
                            name:
                              type: string
                        - type: string
            """);

        Assert.Equal(new[] { "PetOrError", "BOutcome", "CFirst", "CSecond" }, models.OfType<UnionModel>().Select(u => u.Name), StringComparer.Ordinal);
    }

    // A component keeps its own name, so an inline union identical to it is still a type of its own.
    [Fact]
    public void InlineUnion_IdenticalToAComponent_KeepsItsOwnType()
    {
        var (models, _) = Build(PetAndError + Environment.NewLine + """
                Outcome:
                  oneOf:
                    - $ref: '#/components/schemas/Pet'
                    - $ref: '#/components/schemas/Error'
                A:
                  type: object
                  properties:
                    outcome:
                      oneOf:
                        - $ref: '#/components/schemas/Pet'
                        - $ref: '#/components/schemas/Error'
            """);

        Assert.Equal(new[] { "Outcome", "PetOrError" }, models.OfType<UnionModel>().Select(u => u.Name), StringComparer.Ordinal);
    }

    // Issue #360: object variants the converter cannot tell apart are reported as ZRT003. For oneOf
    // it is an error, since every object matching both throws; for anyOf a warning, since the first
    // declared variant always wins.
    private static string Events(string keyword, string labeledEvent = "type: string", string unlabeledEvent = "type: string", string required = "[event, id]") => $$"""
                Labeled:
                  type: object
                  required: {{required}}
                  properties:
                    event:
                      {{labeledEvent}}
                    id:
                      type: integer
                Unlabeled:
                  type: object
                  required: {{required}}
                  properties:
                    event:
                      {{unlabeledEvent}}
                    id:
                      type: integer
                Timeline:
                  {{keyword}}:
                    - $ref: '#/components/schemas/Labeled'
                    - $ref: '#/components/schemas/Unlabeled'
            """;

    private const string LabeledValue = "{ type: string, enum: [labeled] }";
    private const string UnlabeledValue = "{ type: string, enum: [unlabeled] }";

    [Fact]
    public void OneOf_VariantsRequiringTheSameProperties_AreAZrt003Error()
    {
        var (_, warnings) = Build(Events("oneOf"));

        var diagnostic = Assert.Single(warnings);
        Assert.Equal("ZRT003", diagnostic.Code);
        Assert.Equal(OpenApiSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "Schema '#/components/schemas/Timeline': oneOf variants 'Labeled' and 'Unlabeled' of union 'Timeline' "
            + "cannot be told apart. They require the same properties, and no required property holds a different "
            + "single-value enum in each, so reading a JSON object that matches both throws. Add a discriminator, "
            + "or a required property with a different single-value enum to each variant.",
            diagnostic.Message);
    }

    [Fact]
    public void AnyOf_VariantsRequiringTheSameProperties_AreAZrt003Warning()
    {
        var (_, warnings) = Build(Events("anyOf"));

        var diagnostic = Assert.Single(warnings);
        Assert.Equal("ZRT003", diagnostic.Code);
        Assert.Equal(OpenApiSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            "Schema '#/components/schemas/Timeline': anyOf variants 'Labeled' and 'Unlabeled' of union 'Timeline' "
            + "cannot be told apart. Every JSON object that matches 'Unlabeled' also matches 'Labeled', and the first "
            + "declared variant always wins, so 'Unlabeled' is never read. Add a discriminator, or a required property "
            + "with a different single-value enum to each variant.",
            diagnostic.Message);
    }

    [Theory]
    [InlineData("oneOf")]
    [InlineData("anyOf")]
    public void VariantsWithDifferentSingleValueEnums_AreToldApart(string keyword)
    {
        var (models, warnings) = Build(Events(keyword, LabeledValue, UnlabeledValue));

        Assert.Empty(warnings);
        var union = Assert.IsType<UnionModel>(models.Single(m => m is UnionModel));
        Assert.Equal(List(new UnionValueModel("event", JsonKind.String, "labeled")), union.Variants[0].Values);
        Assert.Equal(List(new UnionValueModel("event", JsonKind.String, "unlabeled")), union.Variants[1].Values);
    }

    // A value on a property that is not required does not tell oneOf variants apart: an object
    // without the property matches both. With anyOf, such an object reads as the first variant, and
    // one holding the second variant's value reads as the second, so neither is reported.
    [Fact]
    public void SingleValueEnum_OnAPropertyThatIsNotRequired_DoesNotTellOneOfVariantsApart()
    {
        var (_, oneOf) = Build(Events("oneOf", LabeledValue, UnlabeledValue, required: "[id]"));
        var (_, anyOf) = Build(Events("anyOf", LabeledValue, UnlabeledValue, required: "[id]"));

        var diagnostic = Assert.Single(oneOf);
        Assert.Equal("ZRT003", diagnostic.Code);
        Assert.Equal(OpenApiSeverity.Error, diagnostic.Severity);
        Assert.Empty(anyOf);
    }

    // With anyOf, a later variant with a value is never read when an earlier one without a value
    // takes everything it matches. The other way round, the earlier variant takes only its value,
    // and the later one is read for every other.
    [Fact]
    public void AnyOf_OnlyAVariantThatIsNeverRead_IsReported()
    {
        var (_, earlierHasValue) = Build(Events("anyOf", labeledEvent: LabeledValue));
        var (_, laterHasValue) = Build(Events("anyOf", unlabeledEvent: UnlabeledValue));

        Assert.Empty(earlierHasValue);
        var diagnostic = Assert.Single(laterHasValue);
        Assert.Equal("ZRT003", diagnostic.Code);
        Assert.Contains("so 'Unlabeled' is never read", diagnostic.Message, StringComparison.Ordinal);
    }

    // With oneOf, a value on one variant alone still leaves an object both variants match.
    [Fact]
    public void OneOf_AValueOnOneVariantOnly_IsStillAnError()
    {
        var (_, warnings) = Build(Events("oneOf", labeledEvent: LabeledValue));

        Assert.Equal(OpenApiSeverity.Error, Assert.Single(warnings).Severity);
    }

    // Each pair is reported once, in declaration order, and only object variants are compared.
    [Fact]
    public void EveryIndistinguishablePair_IsReportedOnce()
    {
        var (_, warnings) = Build(Events("oneOf") + Environment.NewLine + """
                Other:
                  type: object
                  required: [event, id]
                  properties:
                    event:
                      type: string
                    id:
                      type: integer
                Wider:
                  oneOf:
                    - $ref: '#/components/schemas/Labeled'
                    - $ref: '#/components/schemas/Unlabeled'
                    - $ref: '#/components/schemas/Other'
                    - type: string
                    - type: string
            """);

        Assert.All(warnings, w => Assert.Equal("ZRT003", w.Code));
        Assert.Equal(
            new[] { "'Labeled' and 'Unlabeled' of union 'Timeline'", "'Labeled' and 'Unlabeled' of union 'Wider'", "'Labeled' and 'Other' of union 'Wider'", "'Unlabeled' and 'Other' of union 'Wider'" },
            warnings.Select(w => w.Message[(w.Message.IndexOf("variants ", StringComparison.Ordinal) + 9)..w.Message.IndexOf(" cannot", StringComparison.Ordinal)]),
            StringComparer.Ordinal);
    }

    private static readonly EquatableList<UnionValueModel> NoValues = EquatableList<UnionValueModel>.Empty;

    private static RecordModel Record(EquatableList<ModelDefinition> models, string name)
        => Assert.IsType<RecordModel>(Assert.Single(models, m => string.Equals(m.Name, name, StringComparison.Ordinal)));
}
