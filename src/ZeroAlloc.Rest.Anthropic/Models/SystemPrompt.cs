using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>A system prompt: one string, or a list of text blocks that can carry cache control.</summary>
[JsonConverter(typeof(SystemPromptConverter))]
public readonly struct SystemPrompt : IEquatable<SystemPrompt>
{
    private SystemPrompt(string? text, IReadOnlyList<TextBlockParam>? blocks)
    {
        Text = text;
        Blocks = blocks;
    }

    /// <summary>The prompt when it is a plain string, otherwise null.</summary>
    public string? Text { get; }

    /// <summary>The prompt when it is a list of text blocks, otherwise null.</summary>
    public IReadOnlyList<TextBlockParam>? Blocks { get; }

    /// <summary>A system prompt written as a JSON string.</summary>
    public static implicit operator SystemPrompt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SystemPrompt(text, null);
    }

    /// <summary>A system prompt written as a JSON array of text blocks.</summary>
    /// <remarks>C# allows no user-defined conversion from an interface, so use <see cref="FromBlocks"/> for one.</remarks>
    public static implicit operator SystemPrompt(TextBlockParam[] blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new SystemPrompt(null, blocks);
    }

    /// <summary>A system prompt written as a JSON array of text blocks.</summary>
    public static implicit operator SystemPrompt(List<TextBlockParam> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new SystemPrompt(null, blocks);
    }

    /// <summary>Converts a string to a system prompt.</summary>
    public static SystemPrompt FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SystemPrompt(text, null);
    }

    /// <summary>Converts text blocks to a system prompt.</summary>
    public static SystemPrompt FromBlocks(IReadOnlyList<TextBlockParam> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new SystemPrompt(null, blocks);
    }

    /// <inheritdoc />
    public bool Equals(SystemPrompt other) =>
        string.Equals(Text, other.Text, StringComparison.Ordinal) && ReferenceEquals(Blocks, other.Blocks);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SystemPrompt other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Text, Blocks);

    /// <summary>Equality.</summary>
    public static bool operator ==(SystemPrompt left, SystemPrompt right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(SystemPrompt left, SystemPrompt right) => !left.Equals(right);
}
