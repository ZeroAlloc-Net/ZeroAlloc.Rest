using MemoryPack;

namespace ZeroAlloc.Rest.Benchmarks;

[MemoryPackable]
public partial class MemoryPackUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
