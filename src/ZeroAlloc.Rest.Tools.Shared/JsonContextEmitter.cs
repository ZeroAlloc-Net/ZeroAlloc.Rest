using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Writes the JsonSerializerContext covering every generated type and every request and response
// type, spec §4. AllowOutOfOrderMetadataProperties lets a discriminator appear anywhere in an object.
internal static class JsonContextEmitter
{
    internal static void Emit(StringBuilder sb, string contextName, IEnumerable<string> typeNames)
    {
        sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSourceGenerationOptions(").Append(ModelEmitter.Json)
            .AppendLine(".JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]");
        foreach (var typeName in typeNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sb.Append('[').Append(ModelEmitter.Serialization).Append(".JsonSerializable(typeof(").Append(typeName).AppendLine("))]");
        sb.Append("public partial class ").Append(contextName).Append(" : ").Append(ModelEmitter.Serialization).AppendLine(".JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
    }
}
