using System.Net;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class TransportAuthenticationTests
{
    [Fact]
    public async Task EnvironmentApiKeyIsTrimmed()
    {
        const string variable = "CREATEOS_SANDBOX_API_KEY";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "  environment-key\n");
            var handler = new CaptureHandler();
            using var transport = CreateTransport(handler);

            _ = await transport.SendAsync<object>(HttpMethod.Get, "/test");

            Assert.Equal("environment-key", handler.Request!.Headers.GetValues("X-Api-Key").Single());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Api-Key")]
    [InlineData("X-Auth-Token")]
    [InlineData("X-CSRF-Token")]
    public async Task CallerCannotInjectSensitiveHeader(string headerName)
    {
        var handler = new CaptureHandler();
        using var transport = CreateTransport(handler, "sdk-key");
        var options = new RequestOptions { Headers = new Dictionary<string, string> { [headerName] = "caller-secret" } };

        _ = await transport.SendAsync<object>(HttpMethod.Get, "/test", options: options);

        if (headerName.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase))
            Assert.Equal("sdk-key", handler.Request!.Headers.GetValues("X-Api-Key").Single());
        else
            Assert.False(handler.Request!.Headers.Contains(headerName));
    }

    [Fact]
    public void ConfiguredHandlerHasRedirectsDisabled()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = true };

        using var client = new SandboxClient(new SandboxClientOptions
        {
            ApiKey = "sdk-key",
            HttpClientHandler = handler,
        });

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task UnauthenticatedRequestContainsNoCredentials()
    {
        var handler = new CaptureHandler();
        using var transport = CreateTransport(handler, "sdk-key");
        var options = new RequestOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer caller-secret",
                ["X-Auth-Token"] = "caller-secret",
            },
        };

        _ = await transport.SendAsync<object>(HttpMethod.Get, "/test", options: options, skipAuth: true);

        Assert.False(handler.Request!.Headers.Contains("X-Api-Key"));
        Assert.False(handler.Request.Headers.Contains("Authorization"));
        Assert.False(handler.Request.Headers.Contains("X-Auth-Token"));
    }

    private static Transport CreateTransport(CaptureHandler handler, string? apiKey = null) => new(new SandboxClientOptions
    {
        ApiKey = apiKey,
        BaseUri = new Uri("https://api.example.test"),
        MaxRetries = 0,
    }, handler);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                RequestMessage = request,
            });
        }
    }
}
