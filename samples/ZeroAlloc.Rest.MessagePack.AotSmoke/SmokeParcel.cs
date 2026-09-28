using MessagePack;

namespace ZeroAlloc.Rest.MessagePackAotSmoke;

// SmokeResolver carries its source-generated formatter.
[MessagePackObject]
public sealed class SmokeParcel
{
    [Key(0)] public int Id { get; set; }

    [Key(1)] public string Name { get; set; } = "";

    [Key(2)] public double Weight { get; set; }
}
