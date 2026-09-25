using System.Diagnostics.CodeAnalysis;

namespace ZeroAlloc.Rest;

public sealed class ZeroAllocClientOptions
{
    public Uri? BaseAddress { get; set; }

    // [DynamicallyAccessedMembers] is required for AOT safety: it tells the trimmer to preserve
    // the public constructors of the registered serializer type so DI can instantiate it.
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? SerializerType { get; private set; }

    /// <summary>
    /// The serializer instance set by <see cref="UseSerializer(IRestSerializer)"/>, if any.
    /// </summary>
    public IRestSerializer? SerializerInstance { get; private set; }

    /// <summary>
    /// Uses <typeparamref name="TSerializer"/> for this client only. The generated <c>Add{I}</c>
    /// registers it as a keyed singleton under the client interface; it never becomes the
    /// app-wide <see cref="IRestSerializer"/>. Replaces any earlier <c>UseSerializer</c> call.
    /// </summary>
    public void UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>()
        where TSerializer : IRestSerializer
    {
        SerializerType = typeof(TSerializer);
        SerializerInstance = null;
    }

    /// <summary>
    /// Uses <paramref name="serializer"/> for this client only, for serializers that need
    /// constructor arguments. Replaces any earlier <c>UseSerializer</c> call.
    /// </summary>
    public void UseSerializer(IRestSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        SerializerInstance = serializer;
        SerializerType = null;
    }
}
