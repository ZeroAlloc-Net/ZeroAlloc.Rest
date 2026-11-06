using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ZeroAlloc.Rest.Anthropic;

internal sealed class ToolResultContentConverter : JsonConverter<ToolResultContent>
{
    public override ToolResultContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ToolResultContent.FromString(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Tool result content is a string or an array of text blocks.");

        var typeInfo = BlockTypeInfo(options);
        var blocks = new List<TextBlockParam>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            blocks.Add(JsonSerializer.Deserialize(ref reader, typeInfo) as TextBlockParam
                ?? throw new JsonException("Tool result content holds text blocks only."));
        }

        return ToolResultContent.FromBlocks(blocks);
    }

    public override void Write(Utf8JsonWriter writer, ToolResultContent value, JsonSerializerOptions options)
    {
        if (value.Blocks is null)
        {
            // default of ToolResultContent holds neither; leave the property null instead, optional members are omitted.
            if (value.Text is null)
                throw new JsonException("A default ToolResultContent has no value. Leave the content null instead.");

            writer.WriteStringValue(value.Text);
            return;
        }

        // Through the polymorphic base, so each block carries its "type" discriminator.
        var typeInfo = BlockTypeInfo(options);
        writer.WriteStartArray();
        foreach (var block in value.Blocks)
            JsonSerializer.Serialize(writer, block, typeInfo);
        writer.WriteEndArray();
    }

    private static JsonTypeInfo<ContentBlockParam> BlockTypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<ContentBlockParam>)options.GetTypeInfo(typeof(ContentBlockParam));
}
