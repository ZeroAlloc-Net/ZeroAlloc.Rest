using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the models, spec §5.2 to §5.6, as pure functions of the intermediate model. Every framework
// type is global::-qualified, because a model named from the spec may shadow it.
internal static class ModelEmitter
{
    internal const string Serialization = "global::System.Text.Json.Serialization";
    internal const string Json = "global::System.Text.Json";

    internal static void Emit(StringBuilder sb, IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
        {
            switch (model)
            {
                case RecordModel record:
                    EmitRecord(sb, record);
                    break;
                case EnumModel enumModel:
                    EmitEnum(sb, enumModel);
                    break;
                default:
                    throw new InvalidOperationException($"No emitter for {model.GetType().Name} '{model.Name}'.");
            }
            sb.AppendLine();
        }
    }

    // Every model, plus every union variant: a type behind a custom converter is not reachable by
    // the STJ source generator on its own, and the union converter resolves variants by type.
    internal static IEnumerable<string> SerializableTypes(IEnumerable<ModelDefinition> models)
    {
        foreach (var model in models)
            yield return model.Name;
    }

    private static void EmitRecord(StringBuilder sb, RecordModel record)
    {
        CSharpNames.AppendDocComment(sb, "", record.Description);
        sb.Append("public sealed record ").Append(record.Name);
        if (record.BaseName is not null)
            sb.Append(" : ").Append(record.BaseName);
        sb.AppendLine().AppendLine("{");
        EmitProperties(sb, record.Properties);
        sb.AppendLine("}");
    }

    internal static void EmitProperties(StringBuilder sb, EquatableList<PropertyModel> properties)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (i > 0)
                sb.AppendLine();
            CSharpNames.AppendDocComment(sb, "    ", property.Description);
            sb.Append("    [").Append(Serialization).Append(".JsonPropertyName(").Append(CSharpNames.Literal(property.WireName)).AppendLine(")]");
            // An optional property that is null was absent: it stays absent on the wire. A required
            // one is always written, null included.
            if (!property.Required)
                sb.Append("    [").Append(Serialization).Append(".JsonIgnore(Condition = ").Append(Serialization).AppendLine(".JsonIgnoreCondition.WhenWritingNull)]");
            sb.Append("    public ");
            if (property.Required)
                sb.Append("required ");
            sb.Append(TypeMapper.Declare(property.Type, property.Required, property.Nullable))
                .Append(' ').Append(property.Name).AppendLine(" { get; init; }");
        }
    }

    // Spec §5.3 and design decision 11: strict enums. A string enum reads its wire names only; an
    // integer enum reads its declared values only. Anything else throws JsonException.
    private static void EmitEnum(StringBuilder sb, EnumModel model)
    {
        var converter = model.Name + "Converter";
        CSharpNames.AppendDocComment(sb, "", model.Description);
        sb.Append('[').Append(Serialization).Append(".JsonConverter(typeof(").Append(converter).AppendLine("))]");
        sb.Append("public enum ").Append(model.Name);
        if (!model.IsString && string.Equals(model.UnderlyingType, "long", StringComparison.Ordinal))
            sb.Append(" : long");
        sb.AppendLine().AppendLine("{");
        foreach (var member in model.Members)
        {
            if (model.IsString)
            {
                sb.Append("    [").Append(Serialization).Append(".JsonStringEnumMemberName(").Append(CSharpNames.Literal(member.WireValue)).AppendLine(")]");
                sb.Append("    ").Append(member.Name).AppendLine(",");
            }
            else
            {
                sb.Append("    ").Append(member.Name).Append(" = ").Append(member.WireValue).AppendLine(",");
            }
        }
        sb.AppendLine("}").AppendLine();
        if (model.IsString)
            EmitStringEnumConverter(sb, model, converter);
        else
            EmitIntegerEnumConverter(sb, model, converter);
    }

    // Reads a JSON string equal to exactly one wire name, so a comma-separated combination, another
    // casing or a number is rejected. The stock JsonStringEnumConverter parses flags combinations.
    // Each member keeps [JsonStringEnumMemberName] because the Rest generator formats values by it.
    private static void EmitStringEnumConverter(StringBuilder sb, EnumModel model, string converter)
    {
        EmitConverterHeader(sb, model, converter);
        sb.Append("        if (reader.TokenType == ").Append(Json).AppendLine(".JsonTokenType.String)");
        sb.AppendLine("        {");
        foreach (var member in model.Members)
        {
            sb.Append("            if (reader.ValueTextEquals(").Append(CSharpNames.Literal(member.WireValue)).AppendLine("))");
            sb.Append("                return ").Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
        }
        sb.AppendLine("        }");
        EmitReadFailureAndWriteHeader(sb, model);
        sb.AppendLine("        switch (value)");
        sb.AppendLine("        {");
        foreach (var member in model.Members)
        {
            sb.Append("            case ").Append(model.Name).Append('.').Append(member.Name).AppendLine(":");
            sb.Append("                writer.WriteStringValue(").Append(CSharpNames.Literal(member.WireValue)).AppendLine(");");
            sb.AppendLine("                return;");
        }
        sb.AppendLine("        }");
        EmitWriteFailure(sb, model);
    }

    // Reads a JSON number equal to one declared value, so a string, a fraction or an out-of-range
    // number is rejected.
    private static void EmitIntegerEnumConverter(StringBuilder sb, EnumModel model, string converter)
    {
        EmitConverterHeader(sb, model, converter);
        sb.Append("        if (reader.TokenType == ").Append(Json).AppendLine(".JsonTokenType.Number && reader.TryGetInt64(out var value))");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (value)");
        sb.AppendLine("            {");
        foreach (var member in model.Members)
            sb.Append("                case ").Append(member.WireValue).Append(": return ").Append(model.Name).Append('.').Append(member.Name).AppendLine(";");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        EmitReadFailureAndWriteHeader(sb, model);
        sb.AppendLine("        switch (value)");
        sb.AppendLine("        {");
        foreach (var member in model.Members)
        {
            sb.Append("            case ").Append(model.Name).Append('.').Append(member.Name).AppendLine(":");
            sb.Append("                writer.WriteNumberValue(").Append(member.WireValue).AppendLine(");");
            sb.AppendLine("                return;");
        }
        sb.AppendLine("        }");
        EmitWriteFailure(sb, model);
    }

    private static void EmitConverterHeader(StringBuilder sb, EnumModel model, string converter)
    {
        sb.Append("internal sealed class ").Append(converter).Append(" : ").Append(Serialization)
            .Append(".JsonConverter<").Append(model.Name).AppendLine(">");
        sb.AppendLine("{");
        sb.Append("    public override ").Append(model.Name).Append(" Read(ref ").Append(Json)
            .Append(".Utf8JsonReader reader, global::System.Type typeToConvert, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
    }

    private static void EmitReadFailureAndWriteHeader(StringBuilder sb, EnumModel model)
    {
        sb.Append("        throw new ").Append(Json).Append(".JsonException(")
            .Append(CSharpNames.Literal($"The JSON value is not a defined {model.Name} value.")).AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public override void Write(").Append(Json).Append(".Utf8JsonWriter writer, ").Append(model.Name)
            .Append(" value, ").Append(Json).AppendLine(".JsonSerializerOptions options)");
        sb.AppendLine("    {");
    }

    // An undefined value has no wire form; sending one would only be rejected by the API.
    private static void EmitWriteFailure(StringBuilder sb, EnumModel model)
    {
        sb.Append("        throw new ").Append(Json).Append(".JsonException(")
            .Append(CSharpNames.Literal($"The value is not a defined {model.Name} value.")).AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }
}
