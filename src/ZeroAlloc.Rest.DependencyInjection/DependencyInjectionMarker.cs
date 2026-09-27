using System.ComponentModel;

namespace ZeroAlloc.Rest.DependencyInjection;

/// <summary>
/// Tells the ZeroAlloc.Rest generator that this package is referenced, so it emits the generated
/// <c>Add{I}</c> registration and the client's <c>IGeneratedRestClient</c> members. Without this
/// package, generated clients have their constructor only and need nothing from Microsoft.Extensions.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyInjectionMarker;
