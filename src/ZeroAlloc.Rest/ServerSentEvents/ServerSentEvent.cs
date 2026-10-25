namespace ZeroAlloc.Rest;

/// <summary>
/// One server-sent event: the optional <c>event:</c> name, the optional <c>id:</c>, and the
/// deserialized <c>data:</c> payload.
/// </summary>
/// <typeparam name="T">The type the data payload is deserialized to.</typeparam>
/// <param name="Event">The event name, or <see langword="null"/> when the event had none.</param>
/// <param name="Id">The last event id the stream set, or <see langword="null"/> when it set none.</param>
/// <param name="Data">The deserialized data payload.</param>
public readonly record struct ServerSentEvent<T>(string? Event, string? Id, T Data);
