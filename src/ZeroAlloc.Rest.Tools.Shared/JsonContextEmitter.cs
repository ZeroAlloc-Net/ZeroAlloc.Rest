using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the JsonSerializerContext covering every generated type and every request and response
// type, spec §4. AllowOutOfOrderMetadataProperties lets a discriminator appear anywhere in an object.
internal static class JsonContextEmitter
{
    // The names the STJ source generator gives the context properties of the framework types a
    // generated file can reach: the simple CLR name, and ByteArray for byte[].
    private static readonly string[] FrameworkPropertyNames =
    [
        "String", "Int32", "Int64", "Single", "Double", "Decimal", "Boolean", "ByteArray",
        "DateTimeOffset", "DateOnly", "TimeOnly", "Guid", "Uri", "JsonElement",
    ];

    internal static void Emit(StringBuilder sb, string contextName, IEnumerable<string> typeNames)
    {
        var names = typeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var used = new HashSet<string>(names.Concat(FrameworkPropertyNames), StringComparer.Ordinal);
        sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSourceGenerationOptions(").Append(ModelEmitter.Json)
            .AppendLine(".JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]");
        foreach (var typeName in names)
        {
            sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSerializable(typeof(").Append(typeName).Append(')');
            // A model named like a framework type, such as a schema called Uri, would share its
            // context property with that type, SYSLIB1031. The framework type cannot be renamed:
            // the STJ generator derives a built-in converter's name from the property name.
            if (FrameworkPropertyNames.Contains(typeName, StringComparer.Ordinal))
                sb.Append(", TypeInfoPropertyName = ").Append(CSharpNames.Literal(CSharpNames.Unique(typeName + "Model", used)));
            sb.AppendLine(")]");
        }
        sb.Append("public partial class ").Append(contextName).Append(" : ").Append(ModelEmitter.Serialization).AppendLine(".JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
    }
}
