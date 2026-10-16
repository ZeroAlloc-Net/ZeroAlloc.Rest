using System;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Issue #336: ZeroAlloc.Rest depends on ZeroAlloc.Results and ZeroAlloc.Collections only, and
// ZeroAlloc.Rest.DependencyInjection depends on core and Microsoft.Extensions.Http only. A unit
// test cannot see a packed nuspec, so this opens the actual .nupkg PackedFeed packed and reads
// its dependency list, so a regression that reintroduces Microsoft.Extensions or
// ZeroAlloc.Serialisation/ValueObjects into core — or drops a dependency from the DI package —
// fails the build instead of only showing up once a consumer restores the real package.
[Collection(PackedFeedCollection.Name)]
public sealed class PackageDependencyTests
{
    private readonly PackedFeed _feed;

    public PackageDependencyTests(PackedFeed feed) => _feed = feed;

    [Theory]
    [InlineData("ZeroAlloc.Rest", new[] { "ZeroAlloc.Collections", "ZeroAlloc.Results" })]
    [InlineData("ZeroAlloc.Rest.DependencyInjection", new[] { "Microsoft.Extensions.Http", "ZeroAlloc.Rest" })]
    public void Package_DependsOnExactly(string packageId, string[] expected)
    {
        var nupkgPath = ConsumerProcess.FindPackage(_feed.FeedDirectory, packageId);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var entry = archive.GetEntry(packageId + ".nuspec");
        Assert.False(entry is null, $"{packageId}.nuspec not found inside {nupkgPath}.");

        using var stream = entry!.Open();
        var doc = XDocument.Load(stream);

        // Every <dependency id="..."/> in every <group>, regardless of the nuspec XML namespace
        // (it varies with the schema version the SDK stamps), deduplicated across TFM groups and
        // sorted ordinally to match the InlineData literals above.
        var actual = doc.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "dependency", StringComparison.Ordinal))
            .Select(e => e.Attribute("id")!.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
