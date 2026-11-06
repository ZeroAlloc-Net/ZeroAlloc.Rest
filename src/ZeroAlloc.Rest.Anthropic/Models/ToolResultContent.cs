using System.Text.Json.Serialization;

namespace ZeroAlloc.Rest.Anthropic;

/// <summary>
/// The content of a tool result: one string, or a list of text blocks so that a result with several parts reaches
/// the model as separate blocks.
/// </summary>
[JsonConverter(typeof(ToolResultContentConverter))]
public readonly struct ToolResultContent : IEquatable<ToolResultContent>
{
    private ToolResultContent(string? text, IReadOnlyList<TextBlockParam>? blocks)
    {
        Text = text;
        Blocks = blocks;
    }

    /// <summary>The content when it is a plain string, otherwise null.</summary>
    public string? Text { get; }

    /// <summary>The content when it is a list of text blocks, otherwise null.</summary>
    public IReadOnlyList<TextBlockParam>? Blocks { get; }

    /// <summary>Content written as a JSON string.</summary>
    public static implicit operator ToolResultContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ToolResultContent(text, null);
    }

    /// <summary>Content written as a JSON array of text blocks.</summary>
    /// <remarks>C# allows no user-defined conversion from an interface, so use <see cref="FromBlocks"/> for one.</remarks>
    public static implicit operator ToolResultContent(TextBlockParam[] blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new ToolResultContent(null, blocks);
    }

    /// <summary>Content written as a JSON array of text blocks.</summary>
    public static implicit operator ToolResultContent(List<TextBlockParam> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new ToolResultContent(null, blocks);
    }

    /// <summary>Converts a string to tool result content.</summary>
    public static ToolResultContent FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ToolResultContent(text, null);
    }

    /// <summary>Converts text blocks to tool result content.</summary>
    public static ToolResultContent FromBlocks(IReadOnlyList<TextBlockParam> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return new ToolResultContent(null, blocks);
    }

    /// <inheritdoc />
    public bool Equals(ToolResultContent other) =>
        string.Equals(Text, other.Text, StringComparison.Ordinal) && ReferenceEquals(Blocks, other.Blocks);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ToolResultContent other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Text, Blocks);

    /// <summary>Equality.</summary>
    public static bool operator ==(ToolResultContent left, ToolResultContent right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ToolResultContent left, ToolResultContent right) => !left.Equals(right);
}
