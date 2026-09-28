namespace ZeroAlloc.Rest;

/// <summary>
/// Registered next to a per-client serializer, so a provider without keyed-service support can
/// report that the client's serializer is unreachable instead of silently using another one.
/// </summary>
internal sealed class PerClientRestSerializer<TClient>
    where TClient : class;
