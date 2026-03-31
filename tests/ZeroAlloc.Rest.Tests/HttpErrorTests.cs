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
}
