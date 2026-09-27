using System;
using System.IO;
using System.Threading.Tasks;
using ZeroAlloc.Rest.MemoryPack;

namespace ZeroAlloc.Rest.MemoryPackAotSmoke;

// MemoryPack finds a type's formatter through reflection unless the type is registered, and that
// reflection is trimmed away under Native AOT. This proves the explicit registration path works in
// the published binary, and that a missing registration is reported rather than silently failing.
internal static class MemoryPackSmoke
{
    // SmokeParcel(7, "Ada", 2.5) as MemoryPack writes it. It is read before any SmokeParcel instance
    // exists, so the type's static constructor has not registered its formatter yet: only
    // types.Add<SmokeParcel>() can have done that.
    private static readonly byte[] s_parcelPayload =
    [
        0x03, 0x07, 0x00, 0x00, 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0x03, 0x00, 0x00,
        0x00, 0x41, 0x64, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x40,
    ];

    public static async Task<string?> RunAsync()
    {
        // The first MemoryPack call in the process: built-in formatters are found without registration
        // and without reflection.
        IRestSerializer builtIn = new MemoryPackRestSerializer(_ => { });
        if (await RoundTripAsync(builtIn, 1234).ConfigureAwait(false) != 1234
            || !string.Equals(await RoundTripAsync(builtIn, "Ada").ConfigureAwait(false), "Ada", StringComparison.Ordinal))
        {
            return "a built-in MemoryPack type should round-trip without registration";
        }

        IRestSerializer serializer = new MemoryPackRestSerializer(types => types.Add<SmokeParcel>());

        using (var payload = new MemoryStream(s_parcelPayload))
        {
            var read = await serializer.DeserializeAsync<SmokeParcel>(payload).ConfigureAwait(false);
            if (read is null || read.Id != 7 || !string.Equals(read.Name, "Ada", StringComparison.Ordinal) || read.Weight != 2.5)
                return "a registered MemoryPack type should read from a fixed payload";
        }

        var parcel = new SmokeParcel(42, "Grace", 12.75);
        using (var stream = new MemoryStream())
        {
            await serializer.SerializeAsync(stream, parcel).ConfigureAwait(false);
            stream.Position = 0;
            var back = await serializer.DeserializeAsync<SmokeParcel>(stream).ConfigureAwait(false);
            if (back is null
                || back.Id != parcel.Id
                || !string.Equals(back.Name, parcel.Name, StringComparison.Ordinal)
                || back.Weight != parcel.Weight)
            {
                return "a registered MemoryPack type should round-trip field by field";
            }
        }

        using (var unregistered = new MemoryStream([0x01, 0x07, 0x00, 0x00, 0x00]))
        {
            try
            {
                _ = await serializer.DeserializeAsync<SmokeUnregisteredParcel>(unregistered).ConfigureAwait(false);
                return "an unregistered MemoryPack type should throw InvalidOperationException";
            }
            catch (InvalidOperationException ex)
            {
                if (!ex.Message.Contains(nameof(SmokeUnregisteredParcel), StringComparison.Ordinal))
                    return "the missing-registration error should name the type: " + ex.Message;
            }
        }

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
