namespace ZeroAlloc.Rest.AotSmoke;

public sealed record QuoteError(HttpErrorKind Kind, int Status, int BodyLength);
