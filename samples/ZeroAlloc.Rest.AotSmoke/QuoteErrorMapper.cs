namespace ZeroAlloc.Rest.AotSmoke;

public sealed class QuoteErrorMapper : IHttpErrorMapper<QuoteError>
{
    public QuoteError Map(HttpError error) => new(error.Kind, (int)error.StatusCode, error.Body.Length);
}
