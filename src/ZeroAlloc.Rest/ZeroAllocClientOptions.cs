using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientOptions
{
    public Uri? BaseAddress { get; set; }

    // [DynamicallyAccessedMembers] is required for AOT safety: it tells the trimmer to preserve
    // the public constructors of the registered serializer type so DI can instantiate it.
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? SerializerType { get; private set; }

    public void UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>()
        where TSerializer : IRestSerializer
        => SerializerType = typeof(TSerializer);
}
