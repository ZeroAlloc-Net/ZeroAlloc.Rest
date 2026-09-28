using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using ZeroAlloc.Rest.MessagePack;

namespace ZeroAlloc.Rest.MessagePackAotSmoke;

// MessagePackSerializerOptions.Standard builds formatters with Reflection.Emit, which Native AOT
// cannot run. This proves the resolver constructor works in the published binary with only the
// source-generated resolver and the built-in formatters, and that a type neither covers is refused.
internal static class MessagePackSmoke
{
    // SmokeParcel { Id = 7, Name = "Ada", Weight = 2.5 } as MessagePack writes it: a three-element
    // array of a fixint, a fixstr and a float64.
    private static readonly byte[] s_parcelPayload =
    [
        0x93, 0x07, 0xA3, 0x41, 0x64, 0x61, 0xCB, 0x40, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    // SmokeParcel { Id = 42, Name = "Grace", Weight = 12.75 } as MessagePack writes it.
    private static readonly byte[] s_graceParcel =
    [
        0x93, 0x2A, 0xA5, 0x47, 0x72, 0x61, 0x63, 0x65, 0xCB, 0x40, 0x29, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    public static async Task<string?> RunAsync()
    {
        var resolver = CompositeResolver.Create(
            Array.Empty<IMessagePackFormatter>(),
            [SmokeResolver.Instance, BuiltinResolver.Instance]);
        IRestSerializer serializer = new MessagePackRestSerializer(resolver);

        if (await RoundTripAsync(serializer, 1234).ConfigureAwait(false) != 1234
            || !string.Equals(await RoundTripAsync(serializer, "Ada").ConfigureAwait(false), "Ada", StringComparison.Ordinal))
        {
            return "a built-in MessagePack type should round-trip";
        }

        if (await ValueTypesAsync(serializer).ConfigureAwait(false) is { } valueTypeFailure)
            return valueTypeFailure;

        using (var payload = new MemoryStream(s_parcelPayload))
        {
            var read = await serializer.DeserializeAsync<SmokeParcel>(payload).ConfigureAwait(false);
            if (read is null || read.Id != 7 || !string.Equals(read.Name, "Ada", StringComparison.Ordinal) || read.Weight != 2.5)
                return "a [MessagePackObject] type should read from a fixed payload";
        }

        var parcel = new SmokeParcel { Id = 42, Name = "Grace", Weight = 12.75 };
        using (var stream = new MemoryStream())
        {
            await serializer.SerializeAsync(stream, parcel).ConfigureAwait(false);
            if (!stream.ToArray().AsSpan().SequenceEqual(s_graceParcel))
            {
                return "a [MessagePackObject] type should write the source-generated array layout";
            }

            stream.Position = 0;
            var back = await serializer.DeserializeAsync<SmokeParcel>(stream).ConfigureAwait(false);
            if (back is null
                || back.Id != parcel.Id
                || !string.Equals(back.Name, parcel.Name, StringComparison.Ordinal)
                || back.Weight != parcel.Weight)
            {
                return "a [MessagePackObject] type should round-trip field by field";
            }
        }

        // StandardResolver would build a Queue<T> formatter through DynamicGenericResolver, with
        // MakeGenericType; neither SmokeResolver nor BuiltinResolver has one, so it must be refused.
        using (var uncovered = new MemoryStream())
        {
            try
            {
                await serializer.SerializeAsync(uncovered, new Queue<SmokeParcel>([parcel])).ConfigureAwait(false);
                return "a type no resolver covers should throw MessagePackSerializationException";
            }
            catch (MessagePackSerializationException ex)
            {
                if (!ex.ToString().Contains("Queue", StringComparison.Ordinal))
                    return "the missing-formatter error should name the type: " + ex;
            }
        }

        return null;
    }

    // Nullable value types go through BuiltinResolver's nullable formatters: a value and a nil
    // must both come back as they went in.
    private static async Task<string?> ValueTypesAsync(IRestSerializer serializer)
    {
        if (await RoundTripAsync<int?>(serializer, 5).ConfigureAwait(false) != 5)
            return "an int? holding a value should round-trip";

        if (await RoundTripAsync<int?>(serializer, null).ConfigureAwait(false) is not null)
            return "an int? holding null should round-trip as null";

        var id = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        if (await RoundTripAsync<Guid?>(serializer, id).ConfigureAwait(false) != id)
            return "a Guid? holding a value should round-trip";

        return null;
    }

    private static async Task<T?> RoundTripAsync<T>(IRestSerializer serializer, T value)
    {
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, value).ConfigureAwait(false);
        stream.Position = 0;
        return await serializer.DeserializeAsync<T>(stream).ConfigureAwait(false);
    }
}
