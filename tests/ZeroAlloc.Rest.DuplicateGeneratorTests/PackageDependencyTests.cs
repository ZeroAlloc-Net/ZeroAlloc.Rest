using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

// Issue #336: ZeroAlloc.Rest depends on ZeroAlloc.Results and ZeroAlloc.Collections only, and
// ZeroAlloc.Rest.DependencyInjection depends on core and Microsoft.Extensions.Http only. A unit
// test cannot see a packed nuspec, so this opens the actual .nupkg from artifacts/local and reads
// its dependency list, so a regression that reintroduces Microsoft.Extensions or
// ZeroAlloc.Serialisation/ValueObjects into core — or drops a dependency from the DI package —
// fails the build instead of only showing up once a consumer restores the real package.
public sealed class PackageDependencyTests
{
    [Theory]
    [InlineData("ZeroAlloc.Rest", new[] { "ZeroAlloc.Collections", "ZeroAlloc.Results" })]
    [InlineData("ZeroAlloc.Rest.DependencyInjection", new[] { "Microsoft.Extensions.Http", "ZeroAlloc.Rest" })]
    public void Package_DependsOnExactly(string packageId, string[] expected)
    {
        var repoRoot = ConsumerProcess.LocateRepoRoot();
        var feed = Path.Combine(repoRoot, "artifacts", "local");
        Assert.True(Directory.Exists(feed),
            $"Local nupkg feed not found at {feed}. Run `dotnet pack -c Release -p:Version=0.0.0-dev -o artifacts/local` " +
            "on src/ZeroAlloc.Rest, src/ZeroAlloc.Rest.Generator, src/ZeroAlloc.Rest.Tools.MSBuild and " +
            "src/ZeroAlloc.Rest.DependencyInjection first.");

        var nupkgPath = FindPackage(feed, packageId);
        Assert.False(nupkgPath is null, $"No {packageId}.<version>.nupkg found in {feed}.");

        using var archive = ZipFile.OpenRead(nupkgPath!);
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

    // packageId + "." + version, where version starts with a digit, so ZeroAlloc.Rest does not
    // match ZeroAlloc.Rest.DependencyInjection.<version>.nupkg (both start with "ZeroAlloc.Rest.").
    private static string? FindPackage(string feed, string packageId)
    {
        foreach (var path in Directory.GetFiles(feed, packageId + ".*.nupkg"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var remainder = name.Substring(packageId.Length + 1);
            if (remainder.Length > 0 && char.IsDigit(remainder[0]))
                return path;
        }
        return null;
    }
}
