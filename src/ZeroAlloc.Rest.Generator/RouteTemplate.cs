using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator;

// A route template's {name} tokens. A token is a '{', then any text without a brace, then a '}'.
// Every other character, including a lone or nested brace, is literal text. A route parameter
// binds a token whose name equals its own exactly: ordinal, case-sensitive. A token's name is its
// text without a leading '*' or '**': {*name} and {**name} are catch-all forms of {name}.
internal static class RouteTemplate
{
    internal enum CatchAllKind
    {
        // {name}: the value is escaped as one segment.
        None,

        // {*name}: bound like {name}, so a '/' in the value is escaped too.
        SingleStar,

        // {**name}: the value's '/' separators are kept; each segment between them is escaped.
        DoubleStar,
    }

    internal readonly struct Token
    {
        internal Token(int start, int length, string text)
        {
            Start = start;
            Length = length;
            Text = text;
            if (text.StartsWith("**", System.StringComparison.Ordinal))
            {
                CatchAll = CatchAllKind.DoubleStar;
                Name = text.Substring(2);
            }
            else if (text.StartsWith("*", System.StringComparison.Ordinal))
            {
                CatchAll = CatchAllKind.SingleStar;
                Name = text.Substring(1);
            }
            else
            {
                CatchAll = CatchAllKind.None;
                Name = text;
            }
        }

        // The position of the opening brace, and the length including both braces.
        internal int Start { get; }
        internal int Length { get; }

        // The text between the braces, as written. Diagnostics print it.
        internal string Text { get; }

        // The text without its '*' or '**' prefix: what a route parameter is matched against.
        internal string Name { get; }
        internal CatchAllKind CatchAll { get; }
    }

    internal static List<Token> Tokens(string route)
    {
        var tokens = new List<Token>();
        var open = -1;
        for (var i = 0; i < route.Length; i++)
        {
            var c = route[i];
            if (c == '{')
            {
                open = i;
            }
            else if (c == '}' && open >= 0)
            {
                tokens.Add(new Token(open, i - open + 1, route.Substring(open + 1, i - open - 1)));
                open = -1;
            }
        }
        return tokens;
    }
}
