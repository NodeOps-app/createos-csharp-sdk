using System.Net;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class TransportConfigurationTests
{
    [Fact]
    public void TrimsEnvironmentBaseUri()
    {
        const string variable = "CREATEOS_SANDBOX_BASE_URL";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "  https://environment.example/prefix  ");
            using var transport = new Transport(Options(), new NoOpHandler());

            Assert.Equal("https://environment.example/prefix", transport.BaseUri.AbsoluteUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public void EmptyEnvironmentBaseUriUsesDefault()
    {
        const string variable = "CREATEOS_SANDBOX_BASE_URL";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "   ");
            using var transport = new Transport(Options(), new NoOpHandler());

            Assert.Equal("https://api.sb.createos.sh/", transport.BaseUri.AbsoluteUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Theory]
    [InlineData("ftp://api.example.test")]
    [InlineData("https://user:password@api.example.test")]
    [InlineData("https://api.example.test?token=secret")]
    [InlineData("https://api.example.test#fragment")]
    public void RejectsUnsafeBaseUri(string value)
    {
        var options = Options();
        options.BaseUri = new Uri(value);

        Assert.Throws<ArgumentException>(() => new Transport(options, new NoOpHandler()));
    }

    [Fact]
    public void RejectsExplicitEmptyApiKey()
    {
        var options = Options();
        options.ApiKey = "   ";

        Assert.Throws<ArgumentException>(() => new Transport(options, new NoOpHandler()));
    }

    [Fact]
    public void RejectsEmptyUserAgent()
    {
        var options = Options();
        options.UserAgent = "   ";

        Assert.Throws<ArgumentException>(() => new Transport(options, new NoOpHandler()));
    }

    [Fact]
    public async Task RejectsNonPositiveRequestTimeout()
    {
        using var transport = new Transport(Options(), new NoOpHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync<object>(HttpMethod.Get, "/test", options: new RequestOptions { Timeout = TimeSpan.Zero }));
    }

    private static SandboxClientOptions Options() => new()
    {
        ApiKey = "sdk-key",
        MaxRetries = 0,
    };

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request });
    }
}
