namespace ZeroAlloc.Rest.Anthropic;

/// <summary>The values <see cref="MessageResponse.StopReason"/> takes.</summary>
public static class StopReasons
{
    /// <summary>The model finished its turn.</summary>
    public const string EndTurn = "end_turn";

    /// <summary>The token limit was reached.</summary>
    public const string MaxTokens = "max_tokens";

    /// <summary>A stop sequence matched.</summary>
    public const string StopSequence = "stop_sequence";

    /// <summary>The model called a tool.</summary>
    public const string ToolUse = "tool_use";

    /// <summary>A server tool turn paused and can be continued.</summary>
    public const string PauseTurn = "pause_turn";

    /// <summary>The model declined to answer.</summary>
    public const string Refusal = "refusal";
}
