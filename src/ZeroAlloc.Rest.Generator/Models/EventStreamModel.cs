namespace ZeroAlloc.Rest.Generator.Models;

// An [EventStream] method's shape. ElementTypeName is the T of IAsyncEnumerable<T>, which the
// iterator yields. Shape one, IAsyncEnumerable<ServerSentEvent<TData>>, has DataTypeName set and no
// EventTypes: every event is yielded, its data read as TData. Shape two has DataTypeName null and
// one EventTypeModel per [EventType]: an event with one of those names is read as its type, and
// any other event is skipped. A NeedsNullCheck type is a non-nullable reference type, so data that
// reads as null must fail instead of reaching the caller.
// IsValid is false when ZRA007 or ZRA008 is reported, or a type does not resolve: the method is
// then a throwing stub, so the diagnostic is the only error the user sees.
internal sealed record EventStreamModel(
    string ElementTypeName,
    string? DataTypeName,
    bool DataNeedsNullCheck,
    EquatableArray<EventTypeModel> EventTypes,
    bool IsValid);

internal sealed record EventTypeModel(string Name, string TypeName, bool NeedsNullCheck);
