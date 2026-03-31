using System.Net;
using Xunit;
using ZeroAlloc.Rest;

namespace ZeroAlloc.Rest.Tests;

public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_ExposesStatusCode()
    {
        var response = new ApiResponse<string>("hello", HttpStatusCode.OK, new Dictionary<string, IReadOnlyList<string>>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void ApiResponse_ExposesContent()
    {
        var response = new ApiResponse<int>(42, HttpStatusCode.OK, new Dictionary<string, IReadOnlyList<string>>());
        Assert.Equal(42, response.Content);
    }

    [Fact]
    public void ApiResponse_NonSuccessStatus_IsSuccessStatusCodeFalse()
    {
        var response = new ApiResponse<string>(null, HttpStatusCode.NotFound, new Dictionary<string, IReadOnlyList<string>>());
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public void ApiResponse_ExposesHeaders()
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["X-Request-Id"] = new[] { "abc123" }
        };
        var response = new ApiResponse<string>("body", HttpStatusCode.OK, headers);
        Assert.Equal("abc123", response.Headers["X-Request-Id"].First());
    }

    [Fact]
    public void ApiResponse_StatusCode201_IsSuccess()
    {
        var response = new ApiResponse<string>("created", HttpStatusCode.Created, new Dictionary<string, IReadOnlyList<string>>());
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void ApiResponse_StatusCode500_IsNotSuccess()
    {
        var response = new ApiResponse<string>(null, HttpStatusCode.InternalServerError, new Dictionary<string, IReadOnlyList<string>>());
        Assert.False(response.IsSuccessStatusCode);
    }
}
