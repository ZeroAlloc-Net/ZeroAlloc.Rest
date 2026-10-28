using System.Globalization;
using System.Text;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

// Issue #428: the builder generated clients write their URL with escapes every value exactly as
// the generated code before it did, through Uri.EscapeDataString and the __EscapePath helper.
public sealed class RestUrlBuilderTests
{
    private static readonly string[] Samples =
    [
        "", "a", "/", "//", "a/b", "/a/", "a b", "~-._", "AZaz09", "a?b#c&d=e+f%g", "é", "😀", "\uD800", "\uDC00",
        "x\uD800/y", "\uD83D/\uDE00", "ü/ä/ö", "%2F", "100%/x", "\u0000\u007F\u0080߿ࠀ￿", "a//b/",
        new string('x', 300), new string('/', 70), string.Concat(Enumerable.Repeat("é/", 200)),
    ];

    public static TheoryData<string> Values()
    {
        var data = new TheoryData<string>();
        foreach (var sample in Samples)
            data.Add(sample);
        var random = new Random(428);
        for (var i = 0; i < 200; i++)
            data.Add(RandomText(random));
        return data;
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void AppendEscaped_EscapesAsEscapeDataString(string value)
    {
        foreach (var size in new[] { 0, 3, 256 })
        {
            var builder = new RestUrlBuilder(new char[size]);
            builder.AppendLiteral("p/");
            builder.AppendEscaped(value);
            builder.AppendLiteral("/s");
            Assert.Equal("p/" + Uri.EscapeDataString(value) + "/s", builder.ToStringAndClear());
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void AppendPathEscaped_EscapesAsTheEscapePathHelperDid(string value)
    {
        foreach (var size in new[] { 0, 3, 256 })
        {
            var builder = new RestUrlBuilder(new char[size]);
            builder.AppendLiteral("p/");
            builder.AppendPathEscaped(value);
            Assert.Equal("p/" + EscapePathAt6e79d98(value), builder.ToStringAndClear());
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void AValueAlone_IsReturnedAsItIs_WhenNothingNeedsEscaping(string value)
    {
        var escaped = new RestUrlBuilder(stackalloc char[16]);
        escaped.AppendEscaped(value);
        var url = escaped.ToStringAndClear();
        Assert.Equal(Uri.EscapeDataString(value), url);
        Assert.Equal(string.Equals(url, value, StringComparison.Ordinal), ReferenceEquals(url, value));

        var path = new RestUrlBuilder(stackalloc char[16]);
        path.AppendPathEscaped(value);
        url = path.ToStringAndClear();
        Assert.Equal(EscapePathAt6e79d98(value), url);
        Assert.Equal(string.Equals(url, value, StringComparison.Ordinal), ReferenceEquals(url, value));
    }

    // Theory data goes through xUnit's serializer, which does not keep an unpaired surrogate as it
    // is, so these are checked here, straight from the strings. On net8.0 the builder escapes with
    // its own code instead of Uri.TryEscapeDataString, which .NET 9 added.
    [Fact]
    public void UnpairedSurrogates_AreEscapedAsEscapeDataString()
    {
        foreach (var value in new[] { "\uD800", "\uDC00", "x\uD800/y", "\uD83D/\uDE00", "a\uDBFFb", "\uDC00\uD800", "😀\uD800" })
        {
            foreach (var size in new[] { 0, 3, 256 })
            {
                var escaped = new RestUrlBuilder(new char[size]);
                escaped.AppendEscaped(value);
                Assert.Equal(Uri.EscapeDataString(value), escaped.ToStringAndClear());

                var path = new RestUrlBuilder(new char[size]);
                path.AppendPathEscaped(value);
                Assert.Equal(EscapePathAt6e79d98(value), path.ToStringAndClear());
            }
        }
    }

    [Fact]
    public void AValueAfterOtherText_IsNotReturnedAlone()
    {
        var builder = new RestUrlBuilder(stackalloc char[16]);
        builder.AppendEscaped("a");
        builder.AppendEscaped("b");
        Assert.Equal("ab", builder.ToStringAndClear());

        builder = new RestUrlBuilder(stackalloc char[16]);
        builder.AppendEscaped("a");
        builder.AppendQueryName("q");
        Assert.Equal("a?q=", builder.ToStringAndClear());

        builder = new RestUrlBuilder(stackalloc char[16]);
        builder.AppendEscaped("a");
        builder.AppendEscaped(1, null);
        Assert.Equal("a1", builder.ToStringAndClear());
    }

    [Fact]
    public void QueryNames_AreSeparatedByQuestionMarkThenAmpersand()
    {
        var builder = new RestUrlBuilder(stackalloc char[4]);
        builder.AppendLiteral("list?fixed=1");
        builder.AppendQueryName("a");
        builder.AppendEscaped("1 2");
        builder.AppendQueryName("b");
        builder.AppendEscaped(-2.5, null);
        Assert.Equal("list?fixed=1?a=1%202&b=-2.5", builder.ToStringAndClear());
    }

    [Fact]
    public void FormattedValues_AreInvariant_AndEscaped()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
        try
        {
            var when = new DateTimeOffset(2026, 10, 11, 1, 2, 3, TimeSpan.FromHours(2));
            var builder = new RestUrlBuilder(stackalloc char[8]);
            builder.AppendEscaped(-1.5e20, null);
            builder.AppendLiteral("/");
            builder.AppendEscaped(1234.5600m, null);
            builder.AppendLiteral("/");
            builder.AppendEscaped(when, "O");
            builder.AppendLiteral("/");
            builder.AppendPathEscaped(when, "O");
            builder.AppendLiteral("/");
            builder.AppendEscaped(Int128.MinValue, null);
            Assert.Equal(
                Uri.EscapeDataString((-1.5e20).ToString(null, CultureInfo.InvariantCulture)) + "/1234.5600/"
                    + Uri.EscapeDataString(when.ToString("O", CultureInfo.InvariantCulture)) + "/"
                    + Uri.EscapeDataString(when.ToString("O", CultureInfo.InvariantCulture)) + "/"
                    + Int128.MinValue.ToString(CultureInfo.InvariantCulture),
                builder.ToStringAndClear());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AValueTooLongForTheFormatBuffer_IsFormattedThroughToString()
    {
        var builder = new RestUrlBuilder(stackalloc char[8]);
        builder.AppendEscaped(new LongFormattable(), null);
        builder.AppendLiteral("/");
        builder.AppendPathEscaped(new LongFormattable(), null);
        var text = new string('é', 100) + "/" + new string('é', 100);
        Assert.Equal(Uri.EscapeDataString(text) + "/" + EscapePathAt6e79d98(text), builder.ToStringAndClear());
    }

    [Fact]
    public void NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RestUrlBuilder(default).AppendEscaped(null!));
        Assert.Throws<ArgumentNullException>(() => new RestUrlBuilder(default).AppendPathEscaped(null!));
    }

    [Fact]
    public void AnEmptyBuilder_IsTheEmptyString()
        => Assert.Same(string.Empty, new RestUrlBuilder(default).ToStringAndClear());

    private static string RandomText(Random random)
    {
        var length = random.Next(0, 40);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(random.Next(8) switch
            {
                0 => '/',
                1 => (char)random.Next(0x20, 0x7F),
                2 => (char)random.Next(0x80, 0x800),
                3 => (char)random.Next(0xD800, 0xE000), // a surrogate, often unpaired
                4 => (char)random.Next(0xE000, 0x10000),
                5 => (char)random.Next(0, 0x20),
                _ => (char)random.Next('a', 'z' + 1),
            });
        }
        return sb.ToString();
    }

    // The __EscapePath helper every client with a {**name} route had at 6e79d98, copied unchanged.
    private static bool IsUnreserved(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~';

    private static string EscapePathAt6e79d98(string value)
    {
        var unchanged = true;
        for (var k = 0; k < value.Length; k++)
        {
            var ch = value[k];
            if (ch != '/' && !IsUnreserved(ch))
            {
                unchanged = false;
                break;
            }
        }
        if (unchanged)
            return value;
        if (value.IndexOf('/', StringComparison.Ordinal) < 0)
            return Uri.EscapeDataString(value);
        var result = new StringBuilder(value.Length + 16);
        var start = 0;
        while (true)
        {
            var end = value.IndexOf('/', start);
            if (end < 0)
                end = value.Length;
            var clean = true;
            for (var i = start; i < end; i++)
            {
                if (!IsUnreserved(value[i]))
                {
                    clean = false;
                    break;
                }
            }
            if (clean)
                result.Append(value, start, end - start);
            else
                result.Append(Uri.EscapeDataString(value.Substring(start, end - start)));
            if (end == value.Length)
                return result.ToString();
            result.Append('/');
            start = end + 1;
        }
    }

    private readonly struct LongFormattable : ISpanFormattable
    {
        private static readonly string Text = new string('é', 100) + "/" + new string('é', 100);

        public string ToString(string? format, IFormatProvider? formatProvider) => Text;

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            charsWritten = 0;
            if (!Text.AsSpan().TryCopyTo(destination))
                return false;
            charsWritten = Text.Length;
            return true;
        }
    }
}
