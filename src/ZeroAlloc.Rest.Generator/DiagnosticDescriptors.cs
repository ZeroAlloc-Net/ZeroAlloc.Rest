using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Rest.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "ZeroAlloc.Rest.Generator";

    internal static readonly DiagnosticDescriptor ConflictingBody = new(
        id: "ZRA001",
        title: "Conflicting body attributes",
        messageFormat: "Method '{0}' has both [Body] and [FormBody] parameters; only one is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingErrorMapper = new(
        id: "ZRA002",
        title: "No error mapper for an error type",
        messageFormat: "Method '{0}' returns a Result with error type '{1}', but no [ErrorMapper] on the interface implements IHttpErrorMapper<{1}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // The same rule for [EventStream(ErrorType = ...)], whose E is not a Result's.
    internal static readonly DiagnosticDescriptor MissingEventStreamErrorMapper = new(
        id: "ZRA002",
        title: "No error mapper for an error type",
        messageFormat: "Method '{0}' declares [EventStream(ErrorType = typeof({1}))], but no [ErrorMapper] on the interface implements IHttpErrorMapper<{1}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidErrorMapper = new(
        id: "ZRA003",
        title: "Invalid error mapper type",
        messageFormat: "'{0}' cannot be an error mapper: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateErrorMapper = new(
        id: "ZRA004",
        title: "Duplicate error mapper",
        messageFormat: "'{1}' maps '{2}', which '{0}' already maps; declare one [ErrorMapper] per error type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ZRA005 has three messages, one per case, under one ID, title and severity. A Warning, not
    // an Error: code that compiled before #333 and silently dropped the value keeps compiling.
    // RouteTemplateAnalyzer reports it, not the generator, so that #pragma warning disable applies.
    private const string RouteMismatchTitle = "Route template and route parameters do not match";

    internal static readonly DiagnosticDescriptor RouteParameterWithoutToken = new(
        id: "ZRA005",
        title: RouteMismatchTitle,
        messageFormat: "Parameter '{0}' of method '{1}' is a route parameter, but route '{2}' has no '{{{0}}}' token, so its value is never sent; add the token, or bind the parameter with [Query], [Header] or [Body]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RouteTokenWithoutParameter = new(
        id: "ZRA005",
        title: RouteMismatchTitle,
        messageFormat: "Route '{0}' of method '{1}' has a '{{{2}}}' token that no route parameter matches, so it is sent as literal text",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // A token no route parameter matches, which the URL still fills as it always has: the token's
    // text is compiled as a C# interpolation hole. See RouteTokenBinding.
    internal static readonly DiagnosticDescriptor RouteTokenEvaluated = new(
        id: "ZRA005",
        title: RouteMismatchTitle,
        messageFormat: "Route '{0}' of method '{1}' has a '{{{2}}}' token that no route parameter matches; it is compiled as C# and takes its value from {3}, without URL escaping; make that value a route parameter with a matching token",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // A Warning, not an Error, as for the other ZeroAlloc generators' nested and generic type rules:
    // before #394 such an interface got a client that did not compile, so nothing that built stops
    // building. No client is generated for the interface.
    internal static readonly DiagnosticDescriptor UnsupportedClientInterface = new(
        id: "ZRA006",
        title: "No client can be generated for the interface",
        messageFormat: "No REST client is generated for '{0}', because {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generated client is a class at namespace level that implements the interface. "
            + "It cannot implement a generic interface or one declared inside a generic type, and cannot "
            + "reach an interface that it, or a containing type, declares private or protected. Declare "
            + "the interface non-generic, outside generic types, and public or internal.");

    // ZRA007 and ZRA008 are Errors: before them an IAsyncEnumerable<T> method got a client that did
    // not compile, and [EventStream] and [EventType] are new, so nothing that built stops building.
    // The method is emitted as a throwing stub, so the diagnostic is the only error.
    private const string EventStreamMismatchTitle = "IAsyncEnumerable<T> and [EventStream] must go together";

    internal static readonly DiagnosticDescriptor AsyncEnumerableWithoutEventStream = new(
        id: "ZRA007",
        title: EventStreamMismatchTitle,
        messageFormat: "Method '{0}' returns '{1}' but has no [EventStream]; add [EventStream] to read the response as text/event-stream, one event at a time",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EventStreamWithoutAsyncEnumerable = new(
        id: "ZRA007",
        title: EventStreamMismatchTitle,
        messageFormat: "Method '{0}' has [EventStream] or [EventType], but returns '{1}'; only a method that returns IAsyncEnumerable<T> reads an event stream",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ZRA008 has one message per case, under one ID, title and severity.
    private const string InvalidEventTypeTitle = "Invalid [EventType] mapping";

    internal static readonly DiagnosticDescriptor EventTypeNotAssignable = new(
        id: "ZRA008",
        title: InvalidEventTypeTitle,
        messageFormat: "[EventType(\"{0}\", typeof({1}))] on method '{2}' names a type that does not convert to '{3}', the type the method yields",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateEventName = new(
        id: "ZRA008",
        title: InvalidEventTypeTitle,
        messageFormat: "Method '{0}' maps the event name \"{1}\" more than once; declare one [EventType] per event name",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EventTypeOnServerSentEventMethod = new(
        id: "ZRA008",
        title: InvalidEventTypeTitle,
        messageFormat: "Method '{0}' returns '{1}', which reads every event's data as one type, so its [EventType] would never apply; remove the [EventType], or return IAsyncEnumerable of the mapped types' base type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NoEventTypes = new(
        id: "ZRA008",
        title: InvalidEventTypeTitle,
        messageFormat: "Method '{0}' returns '{1}' but has no [EventType], so it would skip every event; add an [EventType] per event name, or return IAsyncEnumerable<ServerSentEvent<T>> to read every event as T",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
