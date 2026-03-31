using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientOptions
{
    public Uri? BaseAddress { get; set; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    internal Type? SerializerType { get; private set; }

    public void UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>()
        where TSerializer : IRestSerializer
        => SerializerType = typeof(TSerializer);
}
