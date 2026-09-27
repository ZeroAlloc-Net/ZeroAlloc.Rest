using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator;

// A route template's {name} tokens. A token is a '{', then any text without a brace, then a '}'.
// Every other character, including a lone or nested brace, is literal text. A route parameter
// binds a token whose text equals its name exactly: ordinal, case-sensitive.
internal static class RouteTemplate
{
    internal readonly struct Token
    {
        internal Token(int start, int length, string name)
        {
            Start = start;
            Length = length;
            Name = name;
        }

        // The position of the opening brace, and the length including both braces.
        internal int Start { get; }
        internal int Length { get; }
        internal string Name { get; }
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
