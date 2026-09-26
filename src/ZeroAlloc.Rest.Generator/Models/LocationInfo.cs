using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Rest.Generator.Models;

// A diagnostic location the model can hold without keeping a SyntaxTree alive.
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    internal Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    internal static LocationInfo From(Location location)
        => new(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);
}
