namespace ZeroAlloc.Rest;

public static partial class GeneratedRestClient
{
    /// <summary>
    /// Deserializes the data of the event <paramref name="reader"/> last read. Generated
    /// <c>[EventStream]</c> methods call this once per event they yield, before reading the next.
    /// </summary>
    /// <typeparam name="T">The type the data is read as.</typeparam>
    /// <param name="serializer">The client's serializer.</param>
    /// <param name="reader">The reader, after a <see cref="ServerSentEventReader.ReadAsync"/> that returned <see langword="true"/>.</param>
    /// <param name="cancellationToken">The enumeration's token.</param>
    /// <returns>The deserialized data.</returns>
    /// <remarks>
    /// The serializer reads a seekable stream over <see cref="ServerSentEventReader.Data"/>. The
    /// reader keeps one such stream and points it at each event's data, so a stream of events costs
    /// one stream object, not one per event.
    /// </remarks>
    public static ValueTask<T?> DeserializeEventDataAsync<T>(
        IRestSerializer serializer, ServerSentEventReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(reader);
        return serializer.DeserializeAsync<T>(reader.OpenDataStream(), cancellationToken);
    }
}
