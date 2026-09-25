using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Rest;

/// <summary>
/// Implemented by every client the ZeroAlloc.Rest generator emits. Its static members let the
/// generated <c>Add{I}</c> and integrations such as the Resilience bridge register and build a
/// client without reflection or <c>ActivatorUtilities</c>.
/// </summary>
/// <typeparam name="TSelf">The generated client type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IGeneratedRestClient<TSelf>
    where TSelf : class, IGeneratedRestClient<TSelf>
{
    /// <summary>
    /// Builds the client. Its serializer is the interface-level <c>[Serializer]</c> type resolved
    /// from <paramref name="services"/> when the interface declares one; otherwise the per-client
    /// serializer, then the app-wide <see cref="IRestSerializer"/>.
    /// </summary>
    static abstract TSelf Create(HttpClient httpClient, IServiceProvider services);

    /// <summary>
    /// Registers the serializers the client needs: the interface-level and method-level
    /// <c>[Serializer]</c> types, or the per-client serializer from <paramref name="options"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The interface declares <c>[Serializer]</c> and <paramref name="options"/> also sets one.
    /// </exception>
    static abstract void AddSerializers(IServiceCollection services, ZeroAllocClientOptions options);
}
