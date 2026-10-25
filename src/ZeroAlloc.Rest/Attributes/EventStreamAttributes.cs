namespace ZeroAlloc.Rest.Attributes;

/// <summary>
/// Marks an interface method that returns <c>IAsyncEnumerable&lt;T&gt;</c> as a
/// <c>text/event-stream</c> endpoint. The response is read incrementally, one event at a time.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EventStreamAttribute : Attribute
{
    /// <summary>
    /// The error type a non-success response is mapped to before the stream starts. The method then
    /// throws <see cref="HttpErrorException{TError}"/> instead of <see cref="HttpErrorException"/>.
    /// </summary>
    public Type? ErrorType { get; init; }
}

/// <summary>
/// Maps one <c>event:</c> name of a <see cref="EventStreamAttribute"/> method to the type its
/// <c>data:</c> payload is deserialized to. Repeat it for each named event.
/// </summary>
/// <param name="name">The event name, matched as UTF-8 bytes.</param>
/// <param name="type">The payload type of that event.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class EventTypeAttribute(string name, Type type) : Attribute
{
    /// <summary>The event name.</summary>
    public string Name { get; } = name;

    /// <summary>The payload type of that event.</summary>
    public Type Type { get; } = type;
}
