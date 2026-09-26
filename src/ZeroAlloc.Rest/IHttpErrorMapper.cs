namespace ZeroAlloc.Rest;

/// <summary>
/// Turns an <see cref="HttpError"/> into the error type of methods that return
/// <c>Result&lt;T, TError&gt;</c>. Name it on the client interface with
/// <see cref="Attributes.ErrorMapperAttribute"/>.
/// </summary>
/// <typeparam name="TError">The error type the client's methods return.</typeparam>
/// <remarks>
/// A mapper should be total: return a <typeparamref name="TError"/> for every
/// <see cref="HttpError"/>, whatever its <see cref="HttpError.Kind"/>. An exception it throws
/// propagates to the caller unchanged, marks the request's span as failed, and is never passed to a
/// mapper again. The error body has already been read, and the response is already disposed.
/// </remarks>
public interface IHttpErrorMapper<TError>
{
    /// <summary>Maps one failure.</summary>
    /// <param name="error">The failure, with its body already read.</param>
    /// <returns>The error the method returns.</returns>
    TError Map(HttpError error);
}
