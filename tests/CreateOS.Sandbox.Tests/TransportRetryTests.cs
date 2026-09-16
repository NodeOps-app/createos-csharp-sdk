using System.Net;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class TransportRetryTests
{
    [Fact]
    public async Task PerRequestRetryPolicyOverridesClientPolicy()
    {
        using var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = CreateTransport(handler, maxRetries: 0);
        var options = new RequestOptions
        {
            Retry = new RetryOptions
            {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1),
            },
        };

        _ = await transport.SendAsync<object>(HttpMethod.Get, "/retry", options: options);

        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task DisableRetryTakesPrecedence()
    {
        using var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var transport = CreateTransport(handler, maxRetries: 2);

        await Assert.ThrowsAsync<CreateOSApiException>(() => transport.SendAsync<object>(HttpMethod.Get, "/retry", options: new RequestOptions { DisableRetry = true }));

        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task RetriesIdempotentNetworkFailure()
    {
        using var handler = new SequenceHandler(
            _ => throw new HttpRequestException("temporary"),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = CreateTransport(handler, maxRetries: 1);

        _ = await transport.SendAsync<object>(HttpMethod.Get, "/retry");

        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task DoesNotRetryPostNetworkFailure()
    {
        using var handler = new SequenceHandler(_ => throw new HttpRequestException("temporary"));
        using var transport = CreateTransport(handler, maxRetries: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync<object>(HttpMethod.Post, "/no-retry"));

        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task RawRequestBodyIsNotRetried()
    {
        using var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var transport = CreateTransport(handler, maxRetries: 2);
        await using var content = new MemoryStream([1, 2, 3]);

        using var response = await transport.SendRawAsync(HttpMethod.Put, "/upload", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task RejectsInvalidPerRequestRetryPolicy()
    {
        using var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var transport = CreateTransport(handler, maxRetries: 0);
        var options = new RequestOptions { Retry = new RetryOptions { MaxRetries = -1 } };

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync<object>(HttpMethod.Get, "/retry", options: options));

        Assert.Equal(0, handler.Count);
    }

    private static Transport CreateTransport(HttpMessageHandler handler, int maxRetries) => new(new SandboxClientOptions
    {
        ApiKey = "sdk-key",
        BaseUri = new Uri("https://api.example.test"),
        MaxRetries = maxRetries,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        RetryMaxDelay = TimeSpan.FromMilliseconds(1),
    }, handler);

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _count;
        internal int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _count) - 1;
            var response = responses[Math.Min(index, responses.Length - 1)](request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
