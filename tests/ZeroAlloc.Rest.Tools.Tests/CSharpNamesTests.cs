using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// U+0085, U+2028 and U+2029 are line breaks to the C# lexer, like \r and \n. Left raw in a string
// literal they end it; left raw in a doc comment they end the /// line and leave the rest as code.
public class CSharpNamesTests
{
    [Fact]
    public void Literal_EscapesEveryControlCharacterAndCSharpNewline()
    {
        const string Value = "a\u0085b\u2028c\u2029d\u0001e\u007f\u009ff\n\r\t\0\"\\g";

        var literal = CSharpNames.Literal(Value);

        Assert.Equal("\"a\\u0085b\\u2028c\\u2029d\\u0001e\\u007F\\u009Ff\\n\\r\\t\\0\\\"\\\\g\"", literal);
        var tree = CSharpSyntaxTree.ParseText($"class C {{ const string S = {literal}; }}");
        Assert.Empty(tree.GetDiagnostics());
        var token = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token;
        Assert.Equal(Value, token.ValueText);
    }

    [Fact]
    public void DocComment_BreaksLinesAtEveryCSharpNewline()
    {
        var sb = new StringBuilder();

        CSharpNames.AppendDocComment(sb, "", "one\u0085two\u2028three\u2029four\r\nfive\rsix");

        Assert.Equal(
            "/// <summary>\n/// one\n/// two\n/// three\n/// four\n/// five\n/// six\n/// </summary>\n",
            sb.ToString().ReplaceLineEndings("\n"));
        var tree = CSharpSyntaxTree.ParseText(sb + "class C { }", new CSharpParseOptions(documentationMode: Microsoft.CodeAnalysis.DocumentationMode.Diagnose));
        Assert.Empty(tree.GetDiagnostics());
    }
}
