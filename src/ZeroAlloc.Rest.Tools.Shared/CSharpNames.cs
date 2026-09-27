using System.Globalization;
using System.Text;

namespace ZeroAlloc.Rest.Tools;

// Turns names from a spec into C# identifiers, literals and doc comments. The interface and the
// models share these, so a name is sanitised the same way wherever it appears.
internal static class CSharpNames
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    // A type or member name: PascalCase, sanitised, never a keyword since keywords are lower-case.
    internal static string Pascal(string name, string fallback)
        => ToIdentifier(ToPascalCase(name), upperFirst: true, fallback);

    // Turns a name from the spec into a C# identifier. The first letter is cased as asked. A
    // character an identifier cannot hold is dropped and the letter after it upper-cased, and a
    // leading digit gets an underscore: UserId and user-id both become userId.
    internal static string ToIdentifier(string name, bool upperFirst, string? fallback = null)
    {
        var sb = new StringBuilder(name.Length);
        var upperNext = false;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                upperNext = sb.Length > 0;
                continue;
            }
            if (sb.Length == 0)
                sb.Append(upperFirst ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            else
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }
        if (sb.Length == 0)
            return fallback ?? (upperFirst ? "Operation" : "value");
        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    // A keyword is escaped with @. The source generator matches a {token} against the name without it.
    internal static string Escape(string identifier) => Keywords.Contains(identifier) ? "@" + identifier : identifier;

    internal static string Literal(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    internal static string Unique(string identifier, HashSet<string> used)
    {
        var candidate = identifier;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = identifier + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>Converts snake_case, kebab-case, or plain strings to PascalCase.</summary>
    internal static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new StringBuilder();
        foreach (var part in s.Split('_', '-'))
        {
            if (part.Length == 0) continue;
            result.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }
        return result.Length > 0 ? result.ToString() : s;
    }

    // A description from the spec as a <summary>, one /// line per line, XML-escaped.
    internal static void AppendDocComment(StringBuilder sb, string indent, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.Append(indent).AppendLine("/// <summary>");
        foreach (var line in text.Trim().Split('\n'))
            sb.Append(indent).Append("/// ").AppendLine(XmlEscape(line.TrimEnd('\r')));
        sb.Append(indent).AppendLine("/// </summary>");
    }

    private static string XmlEscape(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
