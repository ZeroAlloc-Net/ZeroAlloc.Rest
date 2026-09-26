using System.Net;
using Xunit;

namespace ZeroAlloc.Rest.Tests;

public sealed class HttpErrorTests
{
    [Fact]
    public void Constructor_SetsStatusCodeAndHeaders()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["X-Request-Id"] = new List<string> { "abc123" }.AsReadOnly()
        };

        var error = new HttpError(HttpStatusCode.NotFound, headers);

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("abc123", error.Headers["X-Request-Id"][0]);
    }

    [Fact]
    public void Message_DefaultsToNull()
    {
        var error = new HttpError(HttpStatusCode.BadRequest,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.Null(error.Message);
    }

    [Fact]
    public void Message_CanBeSet()
    {
        var error = new HttpError(HttpStatusCode.UnprocessableEntity,
            new Dictionary<string, IReadOnlyList<string>>(),
            Message: "Validation failed");

        Assert.Equal("Validation failed", error.Message);
    }

    [Fact]
    public void Kind_DefaultsToStatus_AndException_DefaultsToNull()
    {
        var error = new HttpError(HttpStatusCode.NotFound,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.Equal(HttpErrorKind.Status, error.Kind);
        Assert.Null(error.Exception);
    }

    [Fact]
    public void KindAndException_CanBeInitialised()
    {
        var cause = new HttpRequestException("connection refused");

        var error = new HttpError((HttpStatusCode)0, new Dictionary<string, IReadOnlyList<string>>(), cause.Message)
        {
            Kind = HttpErrorKind.Transport,
            Exception = cause,
        };

        Assert.Equal(HttpErrorKind.Transport, error.Kind);
        Assert.Same(cause, error.Exception);
    }

    [Fact]
    public void Body_DefaultsToEmpty_ContentTypeToNull_AndNotTruncated()
    {
        var error = new HttpError(HttpStatusCode.BadRequest,
            new Dictionary<string, IReadOnlyList<string>>());

        Assert.True(error.Body.IsEmpty);
        Assert.Null(error.ContentType);
        Assert.False(error.BodyTruncated);
    }

    [Fact]
    public void BodyContentTypeAndTruncated_CanBeInitialised()
    {
        byte[] body = [1, 2, 3];

        var error = new HttpError(HttpStatusCode.UnprocessableEntity,
            new Dictionary<string, IReadOnlyList<string>>())
        {
            Body = body,
            ContentType = "application/problem+json",
            BodyTruncated = true,
        };

        Assert.Equal(body, error.Body.ToArray());
        Assert.Equal("application/problem+json", error.ContentType);
        Assert.True(error.BodyTruncated);
    }
}
