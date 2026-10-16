using Xunit;

namespace ZeroAlloc.Rest.DuplicateGeneratorTests;

/// <summary>
/// Shares one <see cref="PackedFeed"/> across every test class that restores the packed packages,
/// so the repository is packed once per run, and the packs and consumer builds do not run in
/// parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PackedFeedCollection : ICollectionFixture<PackedFeed>
{
    public const string Name = "PackedFeed";
}
