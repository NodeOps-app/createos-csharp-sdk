using System.Net;
using System.Text;
using System.Text.Json;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class TransportProtocolTests
{
    [Fact]
    public async Task DecodesJsendSuccessEnvelope()
    {
        using var transport = CreateTransport(_ => Json(HttpStatusCode.OK, "{\"status\":\"success\",\"data\":{\"value\":42}}"));

        var result = await transport.SendAsync<TestValue>(HttpMethod.Get, "/value");

        Assert.Equal(42, result!.Value);
    }

    [Fact]
    public async Task ThrowsForJsendFailureEnvelope()
    {
        using var transport = CreateTransport(_ => Json(HttpStatusCode.OK, "{\"status\":\"fail\",\"data\":{\"message\":\"invalid input\"},\"code\":17}"));

        var exception = await Assert.ThrowsAsync<CreateOSEnvelopeException>(() => transport.SendAsync<TestValue>(HttpMethod.Get, "/value"));

        Assert.Equal("invalid input", exception.Message);
        Assert.Equal(17, exception.ApiCode);
    }

    [Fact]
    public async Task RejectsEmptySuccessfulEnvelope()
    {
        using var transport = CreateTransport(_ => Json(HttpStatusCode.OK, string.Empty));

        await Assert.ThrowsAsync<JsonException>(() => transport.SendAsync<TestValue>(HttpMethod.Get, "/value"));
    }

    [Fact]
    public async Task ApiErrorIncludesCodeRequestIdAndStructuredMessage()
    {
        using var transport = CreateTransport(_ =>
        {
            var response = Json(HttpStatusCode.BadRequest, "{\"status\":\"fail\",\"data\":{\"field\":\"is required\"},\"code\":23}");
            response.Headers.Add("X-Request-ID", "request-123");
            return response;
        });

        var exception = await Assert.ThrowsAsync<CreateOSApiException>(() => transport.SendAsync<TestValue>(HttpMethod.Get, "/value"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(23, exception.ApiCode);
        Assert.Equal("request-123", exception.RequestId);
        Assert.Contains("field: is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUriPreservesBasePathAndEncodesQuery()
    {
        using var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = new Transport(Options(new Uri("https://api.example.test/prefix")), handler);

        var uri = transport.BuildUri("/v1/items", new Dictionary<string, string?> { ["path"] = "/a b" });

        Assert.Equal("https://api.example.test/prefix/v1/items?path=%2Fa%20b", uri.AbsoluteUri);
    }

    [Fact]
    public void BuildUriRejectsAnotherOrigin()
    {
        using var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = new Transport(Options(), handler);

        Assert.Throws<InvalidOperationException>(() => transport.BuildUri("https://attacker.example/steal"));
    }

    private static Transport CreateTransport(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(Options(), new DelegateHandler(response));

    private static SandboxClientOptions Options(Uri? baseUri = null) => new()
    {
        ApiKey = "sdk-key",
        BaseUri = baseUri ?? new Uri("https://api.example.test"),
        MaxRetries = 0,
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var message = response(request);
            message.RequestMessage = request;
            return Task.FromResult(message);
        }
    }

    private sealed record TestValue(int Value);
}
