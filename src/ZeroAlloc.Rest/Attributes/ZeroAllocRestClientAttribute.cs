namespace ZeroAlloc.Rest.Attributes;

/// <summary>Generates a REST client for the interface it is applied to.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class ZeroAllocRestClientAttribute : Attribute
{
    /// <summary>
    /// The most bytes of an error response body that a <c>Result&lt;T, HttpError&gt;</c> method keeps
    /// in <see cref="HttpError.Body"/>. A longer body is cut to this size and
    /// <see cref="HttpError.BodyTruncated"/> is set. A value of 0 or less means no read: the
    /// generated client then contains no body-reading code. A value above
    /// <c>Array.MaxLength - 1</c> is clamped to <c>Array.MaxLength - 1</c>, the largest body a
    /// single array can hold alongside the byte that detects truncation. Read at compile time.
    /// Defaults to 65536.
    /// </summary>
    public int MaxErrorBodyBytes { get; set; } = 65536;
}
