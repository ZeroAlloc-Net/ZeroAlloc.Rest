using System.Globalization;
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Spec §5.6: a union wrapper and its converter. The converter is emitted as source, not by a source
// generator, so the STJ source generator sees its [JsonConverter] attribute. Variants are read and
// written through options.GetTypeInfo, which resolves from the generated context and is not
// annotated for trimming, so nothing here uses reflection.
internal static class UnionEmitter
{
    private const string Serialization = ModelEmitter.Serialization;
    private const string Json = ModelEmitter.Json;

    internal static void Emit(StringBuilder sb, UnionModel model)
    {
        EmitWrapper(sb, model);
        sb.AppendLine();
        EmitConverter(sb, model);
    }

    private static void EmitWrapper(StringBuilder sb, UnionModel model)
    {
        CSharpNames.AppendDocComment(sb, "", model.Description);
        sb.Append('[').Append(Serialization).Append(".JsonConverter(typeof(").Append(model.Name).AppendLine("Converter))]");
        sb.Append("public sealed record ").AppendLine(model.Name);
        sb.AppendLine("{");
        foreach (var variant in model.Variants)
        {
            sb.Append("    public ").Append(TypeMapper.Declare(variant.Type, required: false, nullable: true))
                .Append(" As").Append(variant.Name).AppendLine(" { get; init; }");
            sb.AppendLine();
        }
        EmitMatch(sb, model);
        sb.AppendLine();
        EmitSwitch(sb, model);
        sb.AppendLine("}");
    }

    // Match's type parameter ends in an underscore, which no model name has: CSharpNames.Pascal
    // drops underscores. A model named TResult is therefore never shadowed inside Match.
    private const string MatchResult = "TMatch_";

    private static void EmitMatch(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public ").Append(MatchResult).Append(" Match<").Append(MatchResult).Append(">(")
            .Append(string.Join(", ", model.Variants.Select(v => $"global::System.Func<{v.Type.Name}, {MatchResult}> {Parameter(v)}")))
            .AppendLine(")");
        sb.AppendLine("    {");
        foreach (var variant in model.Variants)
            sb.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(Parameter(variant)).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (As").Append(variant.Name).Append(" is not null) return ").Append(Parameter(variant))
                .Append('(').Append(Value(variant)).AppendLine(");");
        }
        sb.Append("        throw new global::System.InvalidOperationException(").Append(CSharpNames.Literal($"{model.Name} holds no value.")).AppendLine(");");
        sb.AppendLine("    }");
    }

    private static void EmitSwitch(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public void Switch(")
            .Append(string.Join(", ", model.Variants.Select(v => $"global::System.Action<{v.Type.Name}> {Parameter(v)}")))
            .AppendLine(")");
        sb.AppendLine("    {");
        foreach (var variant in model.Variants)
            sb.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(Parameter(variant)).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (As").Append(variant.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            ").Append(Parameter(variant)).Append('(').Append(Value(variant)).AppendLine(");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
        }
        sb.Append("        throw new global::System.InvalidOperationException(").Append(CSharpNames.Literal($"{model.Name} holds no value.")).AppendLine(");");
        sb.AppendLine("    }");
    }

    private static void EmitConverter(StringBuilder sb, UnionModel model)
    {
        sb.Append("internal sealed class ").Append(model.Name).Append("Converter : ").Append(Serialization)
            .Append(".JsonConverter<").Append(model.Name).AppendLine(">");
        sb.AppendLine("{");
        var hasRequired = false;
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var required = model.Variants[i].RequiredWireNames;
            if (required.Count == 0)
                continue;
            sb.Append("    private static readonly string[] Required").Append(Index(i)).Append(" = [")
                .Append(string.Join(", ", required.Select(CSharpNames.Literal))).AppendLine("];");
            hasRequired = true;
        }
        if (hasRequired)
            sb.AppendLine();
        EmitRead(sb, model);
        sb.AppendLine();
        EmitWrite(sb, model);
        sb.AppendLine();
        sb.Append("    private static bool HasAll(").Append(Json).AppendLine(".JsonElement element, string[] names)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var name in names)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!element.TryGetProperty(name, out _)) return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        EmitValueChecks(sb, model);
        sb.AppendLine("}");
    }

    // Issue #360: a property with a single-value enum passes when it holds that value, or is absent;
    // HasAll already rejects an absent required property. No check allocates: TryGetProperty and ValueEquals compare
    // the UTF-8 bytes in place. Only the overloads the variants use are emitted.
    private static void EmitValueChecks(StringBuilder sb, UnionModel model)
    {
        var kinds = model.Variants.SelectMany(v => v.Values).Select(v => v.Kind).Distinct().ToList();
        foreach (var kind in kinds.Order())
        {
            var (parameter, test) = kind switch
            {
                JsonKind.String => ("string", $"property.ValueKind == {Json}.JsonValueKind.String && property.ValueEquals(value)"),
                JsonKind.Number => ("long", $"property.ValueKind == {Json}.JsonValueKind.Number && property.TryGetInt64(out var number) && number == value"),
                _ => ("bool", $"property.ValueKind == (value ? {Json}.JsonValueKind.True : {Json}.JsonValueKind.False)"),
            };
            sb.AppendLine();
            sb.Append("    private static bool IsAbsentOr(").Append(Json).Append(".JsonElement element, string name, ")
                .Append(parameter).AppendLine(" value)");
            sb.Append("        => !element.TryGetProperty(name, out var property) || (").Append(test).AppendLine(");");
        }
    }

    // Parse once, filter by JSON kind, filter objects by required properties and by the values of
    // their single-value enums, then pick the variant matching the most required properties,
    // earliest first on a tie. oneOf rejects more than one candidate as ambiguous; anyOf takes the
    // pick.
    private static void EmitRead(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public override ").Append(model.Name).Append(" Read(ref ").Append(Json)
            .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.Append("        using var document = ").Append(Json).AppendLine(".JsonDocument.ParseValue(ref reader);");
        sb.AppendLine("        var element = document.RootElement;");
        sb.AppendLine("        var kind = element.ValueKind;");
        sb.AppendLine("        var candidates = 0;");
        sb.AppendLine("        var best = -1;");
        sb.AppendLine("        var bestScore = -1;");
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var variant = model.Variants[i];
            var score = variant.RequiredWireNames.Count.ToString(CultureInfo.InvariantCulture);
            sb.Append("        if (").Append(Accepts(variant, i)).AppendLine(")");
            sb.AppendLine("        {");
            sb.AppendLine("            candidates++;");
            sb.Append("            if (").Append(score).Append(" > bestScore) { best = ").Append(Index(i)).Append("; bestScore = ").Append(score).AppendLine("; }");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        if (candidates == 0)");
        sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches no variant of {model.Name}.")).AppendLine(");");
        if (model.IsOneOf)
        {
            sb.AppendLine("        if (candidates > 1)");
            sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches more than one variant of oneOf {model.Name}.")).AppendLine(");");
        }
        sb.AppendLine("        return best switch");
        sb.AppendLine("        {");
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var variant = model.Variants[i];
            sb.Append("            ").Append(Index(i)).Append(" => new ").Append(model.Name).Append(" { As").Append(variant.Name)
                .Append(" = ").Append(Json).Append(".JsonSerializer.Deserialize(element, ").Append(TypeInfo(variant)).AppendLine(") },");
        }
        sb.Append("            _ => throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"The JSON value matches no variant of {model.Name}.")).AppendLine("),");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
    }

    private static void EmitWrite(StringBuilder sb, UnionModel model)
    {
        sb.Append("    public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ").Append(model.Name)
            .Append(" value, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.Append("        var set = ").Append(string.Join(" + ", model.Variants.Select(v => $"(value.As{v.Name} is not null ? 1 : 0)"))).AppendLine(";");
        sb.AppendLine("        if (set != 1)");
        sb.Append("            throw new ").Append(Json).Append(".JsonException(").Append(CSharpNames.Literal($"{model.Name} must hold exactly one value to be written.")).AppendLine(");");
        foreach (var variant in model.Variants)
        {
            sb.Append("        if (value.As").Append(variant.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            ").Append(Json).Append(".JsonSerializer.Serialize(writer, ").Append(Value(variant, "value.")).Append(", ").Append(TypeInfo(variant)).AppendLine(");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
    }

    private static string Accepts(UnionVariantModel variant, int index)
        => variant.Kind == JsonKind.Object
            ? AcceptsKind(variant, index) + string.Concat(variant.Values.Select(v => $" && IsAbsentOr(element, {CSharpNames.Literal(v.WireName)}, {ValueLiteral(v)})"))
            : AcceptsKind(variant, index);

    private static string ValueLiteral(UnionValueModel value) => value.Kind switch
    {
        JsonKind.String => CSharpNames.Literal(value.Value),
        JsonKind.Number => value.Value + "L",
        _ => value.Value,
    };

    private static string AcceptsKind(UnionVariantModel variant, int index) => variant.Kind switch
    {
        JsonKind.Object when variant.RequiredWireNames.Count > 0
            => $"kind == {Json}.JsonValueKind.Object && HasAll(element, Required{Index(index)})",
        JsonKind.Object => $"kind == {Json}.JsonValueKind.Object",
        JsonKind.Array => $"kind == {Json}.JsonValueKind.Array",
        JsonKind.String => $"kind == {Json}.JsonValueKind.String",
        JsonKind.Number => $"kind == {Json}.JsonValueKind.Number",
        JsonKind.Boolean => $"kind is {Json}.JsonValueKind.True or {Json}.JsonValueKind.False",
        _ => "true",
    };

    private static string TypeInfo(UnionVariantModel variant)
        => $"({Json}.Serialization.Metadata.JsonTypeInfo<{variant.Type.Name}>)options.GetTypeInfo(typeof({variant.Type.Name}))";

    // A value-type variant is stored as T?, so its value is read through .Value once it is known set.
    private static string Value(UnionVariantModel variant, string owner = "")
        => variant.Type.IsValueType ? $"{owner}As{variant.Name}.Value" : $"{owner}As{variant.Name}";

    private static string Parameter(UnionVariantModel variant)
        => CSharpNames.Escape(CSharpNames.ToIdentifier(variant.Name, upperFirst: false));

    private static string Index(int index) => index.ToString(CultureInfo.InvariantCulture);
}
