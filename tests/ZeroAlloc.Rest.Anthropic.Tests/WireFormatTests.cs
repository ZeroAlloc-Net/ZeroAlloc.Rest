using System.Text.Json;
using Xunit;
using ZeroAlloc.Rest.Anthropic;

namespace ZeroAlloc.Rest.Anthropic.Tests;

public sealed class WireFormatTests
{
    private static string Write(MessageRequest request) =>
        JsonSerializer.Serialize(request, AnthropicJsonContext.Default.MessageRequest);

    private static MessageRequest Request(params MessageParam[] messages) =>
        new() { Model = "m", MaxTokens = 16, Messages = messages };

    private static MessageParam UserText(string text, CacheControl? cache = null) =>
        new(MessageRole.User, [new TextBlockParam(text, cache)]);

    [Fact]
    public void A_minimal_request_writes_only_model_max_tokens_and_messages()
    {
        var json = Write(Request(UserText("hi")));

        // Red: drop WhenWritingNull from AnthropicJsonContext, the null optionals then appear.
        Assert.Equal(
            """{"model":"m","max_tokens":16,"messages":[{"role":"user","content":[{"type":"text","text":"hi"}]}]}""",
            json);
    }

    [Fact]
    public void Every_optional_request_field_writes_in_the_listed_order()
    {
        var request = new MessageRequest
        {
            Model = "m",
            MaxTokens = 16,
            System = "s",
            Messages = [UserText("hi")],
            Tools = [new ToolDefinition("t", "d", JsonDocument.Parse("""{"type":"object"}""").RootElement, null)],
            Temperature = 0.5,
            TopP = 0.25,
            StopSequences = ["x"],
            Metadata = new RequestMetadata("u1"),
        };

        // Red: swap the declaration order of Temperature and TopP in MessageRequest.
        Assert.Equal(
            """{"model":"m","max_tokens":16,"system":"s","messages":[{"role":"user","content":[{"type":"text","text":"hi"}]}],"""
            + "\"tools\":[{\"name\":\"t\",\"description\":\"d\",\"input_schema\":{\"type\":\"object\"}}],\"temperature\":0.5,\"top_p\":0.25,"
            + "\"stop_sequences\":[\"x\"],\"metadata\":{\"user_id\":\"u1\"}}",
            Write(request));
    }

    [Fact]
    public void Stream_is_written_only_when_set()
    {
        var request = Request(UserText("hi"));

        // Red: remove the JsonInclude attribute from MessageRequest.Stream, the property is then never written.
        Assert.EndsWith(""","stream":true}""", Write(request with { Stream = true }), StringComparison.Ordinal);
        // Red: drop WhenWritingNull from AnthropicJsonContext, "stream":null then appears.
        Assert.DoesNotContain("stream", Write(request), StringComparison.Ordinal);
    }

    [Fact]
    public void System_as_a_string_and_as_blocks_both_write()
    {
        IReadOnlyList<TextBlockParam> blocks = [new TextBlockParam("s")];

        // Red: make SystemPromptConverter write the string as a one-element array.
        Assert.Equal(
            """{"model":"m","max_tokens":16,"system":"s","messages":[]}""",
            Write(new MessageRequest { Model = "m", MaxTokens = 16, System = "s", Messages = [] }));
        // Red: write the blocks without the type discriminator, by serializing them as TextBlockParam.
        Assert.Equal(
            """{"model":"m","max_tokens":16,"system":[{"type":"text","text":"s"}],"messages":[]}""",
            Write(new MessageRequest { Model = "m", MaxTokens = 16, System = SystemPrompt.FromBlocks(blocks), Messages = [] }));
    }

    [Fact]
    public void A_system_prompt_reads_back_from_a_string_and_from_blocks()
    {
        var text = JsonSerializer.Deserialize("""{"model":"m","max_tokens":1,"system":"s","messages":[]}""", AnthropicJsonContext.Default.MessageRequest)!;
        var blocks = JsonSerializer.Deserialize(
            """{"model":"m","max_tokens":1,"system":[{"type":"text","text":"b"}],"messages":[]}""", AnthropicJsonContext.Default.MessageRequest)!;

        // Red: make SystemPromptConverter.Read return FromBlocks for a string token.
        Assert.Equal("s", text.System!.Value.Text);
        // Red: make SystemPromptConverter.Read skip every array element.
        Assert.Equal("b", Assert.Single(blocks.System!.Value.Blocks!).Text);
    }

    [Fact]
    public void Cache_control_writes_ephemeral_with_ttl_on_a_system_block_a_message_block_and_a_tool()
    {
        const string OneHour = "\"cache_control\":{\"type\":\"ephemeral\",\"ttl\":\"1h\"}";
        IReadOnlyList<TextBlockParam> system = [new TextBlockParam("s", CacheControl.OneHour)];
        var schema = JsonDocument.Parse("{}").RootElement;

        // Red: remove the cache_control property from TextBlockParam, the system block loses it.
        Assert.Contains(
            """{"type":"text","text":"s",""" + OneHour + "}",
            Write(new MessageRequest { Model = "m", MaxTokens = 1, System = SystemPrompt.FromBlocks(system), Messages = [] }),
            StringComparison.Ordinal);
        // Red: remove the cache_control property from TextBlockParam, the message block loses it.
        Assert.Contains(
            """{"type":"text","text":"hi",""" + OneHour + "}",
            Write(Request(UserText("hi", CacheControl.OneHour))),
            StringComparison.Ordinal);
        // Red: remove the cache_control property from ToolDefinition, the tool loses it.
        Assert.Contains(
            "\"input_schema\":{}," + OneHour + "}",
            Write(new MessageRequest
            {
                Model = "m",
                MaxTokens = 1,
                Messages = [],
                Tools = [new ToolDefinition("t", null, schema, CacheControl.OneHour)],
            }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_cache_control_statics_carry_their_ttl_and_a_missing_ttl_is_omitted()
    {
        // Red: change FiveMinutes to "1h".
        Assert.Equal("""{"type":"ephemeral","ttl":"5m"}""", JsonSerializer.Serialize(CacheControl.FiveMinutes, AnthropicJsonContext.Default.CacheControl));
        // Red: change OneHour to "5m".
        Assert.Equal("""{"type":"ephemeral","ttl":"1h"}""", JsonSerializer.Serialize(CacheControl.OneHour, AnthropicJsonContext.Default.CacheControl));
        // Red: drop WhenWritingNull from AnthropicJsonContext, "ttl":null then appears.
        Assert.Equal("""{"type":"ephemeral"}""", JsonSerializer.Serialize(new CacheControl(null), AnthropicJsonContext.Default.CacheControl));
    }

    [Fact]
    public void A_tool_input_schema_passes_through_as_raw_json()
    {
        // 1.0 keeps its text only when the element is written through, not rebuilt from a number.
        const string Schema = """{"type":"object","properties":{"n":{"type":"number","minimum":1.0}},"required":["n"]}""";
        using var doc = JsonDocument.Parse(Schema);
        var request = new MessageRequest
        {
            Model = "m",
            MaxTokens = 1,
            Messages = [],
            Tools = [new ToolDefinition("t", null, doc.RootElement, null)],
        };

        // Red: rename the JsonPropertyName of InputSchema to "schema".
        Assert.Contains("\"input_schema\":" + Schema + "}", Write(request), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_result_with_is_error_writes_is_error_true()
    {
        var request = Request(new MessageParam(MessageRole.User, [new ToolResultBlockParam("t1", "boom", true)]));

        // Red: rename the JsonPropertyName of IsError to "error".
        Assert.Contains(
            """{"type":"tool_result","tool_use_id":"t1","content":"boom","is_error":true}""",
            Write(request),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_result_without_is_error_omits_it()
    {
        var request = Request(new MessageParam(MessageRole.User, [new ToolResultBlockParam("t1", "ok", null)]));

        // Red: drop WhenWritingNull from AnthropicJsonContext, "is_error":null then appears.
        Assert.Contains(
            """{"type":"tool_result","tool_use_id":"t1","content":"ok"}""",
            Write(request),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_assistant_tool_use_block_writes_its_input_and_the_assistant_role()
    {
        var input = JsonDocument.Parse("""{"location":"SF"}""").RootElement;
        var request = Request(new MessageParam(MessageRole.Assistant, [new ToolUseBlockParam("t1", "get_weather", input)]));

        // Red: remove the JsonStringEnumMemberName attribute from MessageRole.Assistant, it then writes "Assistant".
        Assert.Contains("\"role\":\"assistant\"", Write(request), StringComparison.Ordinal);
        // Red: rename the JsonPropertyName of Input to "arguments".
        Assert.Contains(
            """{"type":"tool_use","id":"t1","name":"get_weather","input":{"location":"SF"}}""",
            Write(request),
            StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string, string, int, int> Responses => new()
    {
        { "basic.json", "msg_01XFDUDYJgAACzvnptvVoYEL", "end_turn", "Hello!", 12, 6 },
        { "max_tokens.json", "msg_01Q8Faay6S7QPTvEUUQARt7h", "max_tokens", "C", 42, 1 },
    };

    [Theory]
    [MemberData(nameof(Responses))]
    public void Each_documented_response_parses(
        string file, string id, string stopReason, string text, int inputTokens, int outputTokens)
    {
        var response = JsonSerializer.Deserialize(Example("responses", file), AnthropicJsonContext.Default.MessageResponse);

        Assert.NotNull(response);
        // Red: rename the JsonPropertyName of Id to "identifier".
        Assert.Equal(id, response.Id);
        // Red: rename the JsonPropertyName of StopReason to "stop".
        Assert.Equal(stopReason, response.StopReason);
        // Red: rename the JsonPropertyName of Text on TextBlock to "value".
        Assert.Equal(text, Assert.IsType<TextBlock>(Assert.Single(response.Content)).Text);
        // Red: rename the JsonPropertyName of InputTokens to "input".
        Assert.Equal(inputTokens, response.Usage.InputTokens);
        // Red: rename the JsonPropertyName of OutputTokens to "output".
        Assert.Equal(outputTokens, response.Usage.OutputTokens);
        // Red: rename the JsonPropertyName of Role to "who".
        Assert.Equal(MessageRole.Assistant, response.Role);
    }

    [Fact]
    public void The_documented_text_and_tool_use_response_parses_both_blocks()
    {
        var response = JsonSerializer.Deserialize(
            Example("responses", "text-and-tool_use-partial.json"), AnthropicJsonContext.Default.MessageResponse);

        Assert.NotNull(response);
        // Red: remove the JsonDerivedType for ToolUseBlock from ContentBlock, the block reads as a plain ContentBlock.
        var tool = Assert.IsType<ToolUseBlock>(response.Content[1]);
        // Red: rename the JsonPropertyName of Id on ToolUseBlock to "uid".
        Assert.Equal("toolu_01A09q90qw90lq917835lq9", tool.Id);
        // Red: rename the JsonPropertyName of Name on ToolUseBlock to "tool".
        Assert.Equal("get_weather", tool.Name);
        // Red: rename the JsonPropertyName of Input on ToolUseBlock to "arguments".
        Assert.Equal("San Francisco, CA", tool.Input.GetProperty("location").GetString());
    }

    public static TheoryData<string, Type> Events => new()
    {
        { "message_start-basic.json", typeof(MessageStartEvent) },
        { "message_start-cache-usage.json", typeof(MessageStartEvent) },
        { "message_start-without-usage.json", typeof(MessageStartEvent) },
        { "content_block_start-text.json", typeof(ContentBlockStartEvent) },
        { "content_block_start-tool_use.json", typeof(ContentBlockStartEvent) },
        { "content_block_start-thinking-UNKNOWN.json", typeof(ContentBlockStartEvent) },
        { "content_block_start-server_tool_use-UNKNOWN.json", typeof(ContentBlockStartEvent) },
        { "ping.json", typeof(PingEvent) },
        { "content_block_delta-text_delta.json", typeof(ContentBlockDeltaEvent) },
        { "content_block_delta-input_json_delta.json", typeof(ContentBlockDeltaEvent) },
        { "content_block_delta-thinking_delta-UNKNOWN.json", typeof(ContentBlockDeltaEvent) },
        { "content_block_stop.json", typeof(ContentBlockStopEvent) },
        { "message_delta-end_turn.json", typeof(MessageDeltaEvent) },
        { "message_delta-tool_use.json", typeof(MessageDeltaEvent) },
        { "message_delta-with-server_tool_use-usage.json", typeof(MessageDeltaEvent) },
        { "message_stop.json", typeof(MessageStopEvent) },
        { "error-overloaded.json", typeof(ErrorEvent) },
    };

    [Fact]
    public void Every_documented_event_file_is_in_the_table()
    {
        var onDisk = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "DocumentedExamples", "events"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        var listed = Events.Select(row => (string)row[0]).Order(StringComparer.Ordinal).ToArray();

        // Red: add a file to DocumentedExamples/events that the Events table does not list.
        Assert.Equal(onDisk, listed);
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Each_documented_stream_event_parses(string file, Type expected)
    {
        var json = Example("events", file);

        var asBase = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.MessageStreamEvent);
        // Task 7 reads each event as its own type from the data payload, which still carries "type".
        var asOwnType = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.GetTypeInfo(expected)!);

        // Red: remove the JsonDerivedType for this event from MessageStreamEvent, the base read then fails.
        Assert.IsType(expected, asBase);
        // Red: remove PingEvent from both the JsonDerivedType list and the JsonSerializable list, GetTypeInfo returns null.
        // A derived type that stays in the JsonDerivedType list needs no JsonSerializable of its own.
        Assert.IsType(expected, asOwnType);
    }

    [Fact]
    public void A_derived_event_reads_on_its_own_from_a_data_payload_that_carries_type()
    {
        var start = JsonSerializer.Deserialize(
            Example("events", "message_start-basic.json"), AnthropicJsonContext.Default.MessageStartEvent);
        var delta = JsonSerializer.Deserialize(
            Example("events", "content_block_delta-input_json_delta.json"), AnthropicJsonContext.Default.ContentBlockDeltaEvent);

        // Red: rename the JsonPropertyName of Message on MessageStartEvent to "msg".
        Assert.Equal("msg_1nZdL29xx5MUA1yADyHTEsnR8uuvGzszyY", start!.Message.Id);
        // Red: rename the JsonPropertyName of Delta on ContentBlockDeltaEvent to "change".
        Assert.Equal("""{"location":""", Assert.IsType<InputJsonDelta>(delta!.Delta).PartialJson);
    }

    [Fact]
    public void The_message_start_event_carries_the_message_and_its_usage()
    {
        var start = Event<MessageStartEvent>("message_start-basic.json");

        // Red: rename the JsonPropertyName of Message to "msg".
        Assert.Equal("msg_1nZdL29xx5MUA1yADyHTEsnR8uuvGzszyY", start.Message.Id);
        // Red: rename the JsonPropertyName of Content on MessageResponse to "blocks", the list is then null.
        Assert.Empty(start.Message.Content);
        // Red: change the documented file's stop_reason to "end_turn".
        Assert.Null(start.Message.StopReason);
        // Red: rename the JsonPropertyName of InputTokens to "input".
        Assert.Equal(25, start.Message.Usage.InputTokens);
    }

    [Fact]
    public void The_documented_cache_usage_on_message_start_reads_zero_creation_and_read()
    {
        var usage = Event<MessageStartEvent>("message_start-cache-usage.json").Message.Usage;

        // Red: rename the JsonPropertyName of InputTokens to "input".
        Assert.Equal(2679, usage.InputTokens);
        // Red: rename the JsonPropertyName of CacheCreationInputTokens to "cache_creation".
        Assert.Equal(0, usage.CacheCreationInputTokens);
        // Red: rename the JsonPropertyName of CacheReadInputTokens to "cache_read".
        Assert.Equal(0, usage.CacheReadInputTokens);
        // Red: add a cache_creation object to the documented file, the split is then not null.
        Assert.Null(usage.CacheCreation);
    }

    [Fact]
    public void A_message_start_without_usage_reads_with_a_null_usage()
    {
        var start = Event<MessageStartEvent>("message_start-without-usage.json");

        // Red: mark Usage as required on MessageResponse, the read then throws.
        Assert.Null(start.Message.Usage);
    }

    [Fact]
    public void The_content_block_events_carry_index_block_and_delta()
    {
        var textStart = Event<ContentBlockStartEvent>("content_block_start-text.json");
        var toolStart = Event<ContentBlockStartEvent>("content_block_start-tool_use.json");
        var textDelta = Event<ContentBlockDeltaEvent>("content_block_delta-text_delta.json");
        var jsonDelta = Event<ContentBlockDeltaEvent>("content_block_delta-input_json_delta.json");

        // Red: rename the JsonPropertyName of Index on ContentBlockStartEvent to "i".
        Assert.Equal(1, toolStart.Index);
        // Red: rename the JsonPropertyName of ContentBlock to "block".
        Assert.Equal("", Assert.IsType<TextBlock>(textStart.ContentBlock).Text);
        var tool = Assert.IsType<ToolUseBlock>(toolStart.ContentBlock);
        // Red: rename the JsonPropertyName of Name on ToolUseBlock to "tool".
        Assert.Equal("get_weather", tool.Name);
        // Red: rename the JsonPropertyName of Input on ToolUseBlock to "arguments", the input is then undefined.
        Assert.Equal(JsonValueKind.Object, tool.Input.ValueKind);
        // Red: rename the JsonPropertyName of Text on TextDelta to "value".
        Assert.Equal("Hello", Assert.IsType<TextDelta>(textDelta.Delta).Text);
        // Red: rename the JsonPropertyName of Index on ContentBlockDeltaEvent to "i", the index then reads 0.
        Assert.Equal(1, jsonDelta.Index);
        // Red: rename the JsonPropertyName of PartialJson to "json".
        Assert.Equal("""{"location":""", Assert.IsType<InputJsonDelta>(jsonDelta.Delta).PartialJson);
    }

    [Fact]
    public void The_content_block_stop_event_carries_its_index()
    {
        var stop = JsonSerializer.Deserialize("""{"type":"content_block_stop","index":3}""", AnthropicJsonContext.Default.MessageStreamEvent);

        // Red: rename the JsonPropertyName of Index on ContentBlockStopEvent to "i", the index then reads 0.
        Assert.Equal(3, Assert.IsType<ContentBlockStopEvent>(stop).Index);
    }

    [Fact]
    public void The_message_delta_events_carry_the_stop_reason_and_the_cumulative_output_tokens()
    {
        var end = Event<MessageDeltaEvent>("message_delta-end_turn.json");
        var tool = Event<MessageDeltaEvent>("message_delta-tool_use.json");
        var server = Event<MessageDeltaEvent>("message_delta-with-server_tool_use-usage.json");

        // Red: rename the JsonPropertyName of StopReason on MessageDeltaBody to "reason".
        Assert.Equal(StopReasons.EndTurn, end.Delta.StopReason);
        // Red: rename the JsonPropertyName of StopReason on MessageDeltaBody to "reason".
        Assert.Equal(StopReasons.ToolUse, tool.Delta.StopReason);
        // Red: rename the JsonPropertyName of Usage on MessageDeltaEvent to "tokens".
        Assert.Equal(89, tool.Usage!.OutputTokens);
        // An unmodelled usage field such as server_tool_use must not break the read.
        // Red: set UnmappedMemberHandling to Disallow on Usage, the unmodelled server_tool_use then throws.
        // Red: rename the JsonPropertyName of InputTokens to "input".
        Assert.Equal(10682, server.Usage!.InputTokens);
        // Red: rename the JsonPropertyName of OutputTokens to "output".
        Assert.Equal(510, server.Usage.OutputTokens);
    }

    [Fact]
    public void The_error_event_carries_the_error_type_and_message()
    {
        var error = Event<ErrorEvent>("error-overloaded.json");

        // Red: rename the JsonPropertyName of Type on ErrorDetail to "kind".
        Assert.Equal("overloaded_error", error.Error.Type);
        // Red: rename the JsonPropertyName of Message on ErrorDetail to "text".
        Assert.Equal("Overloaded", error.Error.Message);
    }

    [Fact]
    public void The_stop_reason_constants_are_the_wire_values()
    {
        // Red: change any one constant, for example EndTurn to "end".
        Assert.Equal(
            ["end_turn", "max_tokens", "stop_sequence", "tool_use", "pause_turn", "refusal"],
            new[]
            {
                StopReasons.EndTurn, StopReasons.MaxTokens, StopReasons.StopSequence,
                StopReasons.ToolUse, StopReasons.PauseTurn, StopReasons.Refusal,
            });
    }

    [Fact]
    public void Usage_reads_cache_read_creation_and_the_ttl_split()
    {
        const string Json = """
            {"input_tokens":10,"output_tokens":5,"cache_creation_input_tokens":10,"cache_read_input_tokens":4,
             "cache_creation":{"ephemeral_5m_input_tokens":7,"ephemeral_1h_input_tokens":3}}
            """;

        var usage = JsonSerializer.Deserialize(Json, AnthropicJsonContext.Default.Usage)!;

        // Red: rename the JsonPropertyName of CacheCreationInputTokens to "cache_creation_tokens".
        Assert.Equal(10, usage.CacheCreationInputTokens);
        // Red: rename the JsonPropertyName of CacheReadInputTokens to "cache_read_tokens".
        Assert.Equal(4, usage.CacheReadInputTokens);
        // Red: rename the JsonPropertyName of Ephemeral5mInputTokens to "ephemeral_5m".
        Assert.Equal(7, usage.CacheCreation!.Ephemeral5mInputTokens);
        // Red: rename the JsonPropertyName of Ephemeral1hInputTokens to "ephemeral_1h".
        Assert.Equal(3, usage.CacheCreation.Ephemeral1hInputTokens);
    }

    [Fact]
    public void An_unknown_content_block_type_reads_as_a_plain_ContentBlock()
    {
        const string Json = """{"type":"server_tool_use","id":"s1","name":"web_search","input":{}}""";

        var block = JsonSerializer.Deserialize(Json, AnthropicJsonContext.Default.ContentBlock);

        // Red: set IgnoreUnrecognizedTypeDiscriminators = false on ContentBlock, the read then throws.
        Assert.Equal(typeof(ContentBlock), block!.GetType());
    }

    [Fact]
    public void An_unknown_delta_type_reads_as_a_plain_ContentDelta()
    {
        const string Json = """{"type":"thinking_delta","thinking":"hm"}""";

        var delta = JsonSerializer.Deserialize(Json, AnthropicJsonContext.Default.ContentDelta);

        // Red: set IgnoreUnrecognizedTypeDiscriminators = false on ContentDelta, the read then throws.
        Assert.Equal(typeof(ContentDelta), delta!.GetType());
    }

    [Fact]
    public void The_documented_unknown_blocks_and_deltas_keep_the_stream_event_readable()
    {
        var thinking = Event<ContentBlockStartEvent>("content_block_start-thinking-UNKNOWN.json");
        var server = Event<ContentBlockStartEvent>("content_block_start-server_tool_use-UNKNOWN.json");
        var delta = Event<ContentBlockDeltaEvent>("content_block_delta-thinking_delta-UNKNOWN.json");

        // Red: set IgnoreUnrecognizedTypeDiscriminators = false on ContentBlock, both reads throw.
        Assert.Equal(typeof(ContentBlock), thinking.ContentBlock.GetType());
        Assert.Equal(typeof(ContentBlock), server.ContentBlock.GetType());
        // Red: set IgnoreUnrecognizedTypeDiscriminators = false on ContentDelta, the read throws.
        Assert.Equal(typeof(ContentDelta), delta.Delta.GetType());
    }

    [Fact]
    public void The_type_discriminator_need_not_come_first()
    {
        var block = JsonSerializer.Deserialize("""{"text":"a","type":"text"}""", AnthropicJsonContext.Default.ContentBlock);

        // Red: remove AllowOutOfOrderMetadataProperties from AnthropicJsonContext, the read then throws.
        Assert.Equal("a", Assert.IsType<TextBlock>(block).Text);
    }

    private static string Example(string folder, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DocumentedExamples", folder, file));

    private static T Event<T>(string file)
        where T : MessageStreamEvent =>
        Assert.IsType<T>(JsonSerializer.Deserialize(Example("events", file), AnthropicJsonContext.Default.MessageStreamEvent));
}
