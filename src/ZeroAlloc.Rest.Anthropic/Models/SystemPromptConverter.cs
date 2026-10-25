using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace ZeroAlloc.Rest.Anthropic;

internal sealed class SystemPromptConverter : JsonConverter<SystemPrompt>
{
    public override SystemPrompt Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return SystemPrompt.FromString(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A system prompt is a string or an array of text blocks.");

        var typeInfo = BlockTypeInfo(options);
        var blocks = new List<TextBlockParam>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (JsonSerializer.Deserialize(ref reader, typeInfo) is TextBlockParam text)
                blocks.Add(text);
        }

        return SystemPrompt.FromBlocks(blocks);
    }

    public override void Write(Utf8JsonWriter writer, SystemPrompt value, JsonSerializerOptions options)
    {
        if (value.Blocks is null)
        {
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
