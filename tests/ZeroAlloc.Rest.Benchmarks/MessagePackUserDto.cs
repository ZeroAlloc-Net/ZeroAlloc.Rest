using MessagePack;

namespace ZeroAlloc.Rest.Benchmarks;

[MessagePackObject]
public sealed class MessagePackUserDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}
