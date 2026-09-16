using System.Net;
using System.Text;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class SandboxInstanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void PreviewUriRejectsInvalidPort(int port)
    {
        using var transport = CreateTransport(new DelegateHandler(_ => NoContent()));
        var instance = Instance(transport, ingress: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => instance.GetPreviewUri(port));
    }

    [Fact]
    public void PreviewUriRequiresIngress()
    {
        using var transport = CreateTransport(new DelegateHandler(_ => NoContent()));
        var instance = Instance(transport, ingress: false);

        Assert.Throws<InvalidOperationException>(() => instance.GetPreviewUri(8080));
    }

    [Fact]
    public void PreviewUriExpandsPortTemplate()
    {
        using var transport = CreateTransport(new DelegateHandler(_ => NoContent()));
        var instance = Instance(transport, ingress: true);

        Assert.Equal("https://8080.preview.example/", instance.GetPreviewUri(8080).AbsoluteUri);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(86401)]
    public async Task AutoPauseRejectsOutOfRangeSeconds(int seconds)
    {
        using var handler = new DelegateHandler(_ => NoContent());
        using var transport = CreateTransport(handler);
        var instance = Instance(transport, ingress: false);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => instance.SetAutoPauseAsync(TimeSpan.FromSeconds(seconds)));

        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task PauseUpdatesCachedStatus()
    {
        using var transport = CreateTransport(new DelegateHandler(request => Json(request, "{\"status\":\"success\",\"data\":{\"id\":\"sb-1\",\"status\":\"paused\"}}")));
        var instance = Instance(transport, ingress: false);

        await instance.PauseAsync();

        Assert.Equal(SandboxStatus.Paused, instance.Status);
    }

    [Fact]
    public async Task ShellThrowsForNonzeroExit()
    {
        using var transport = CreateTransport(new DelegateHandler(request => Json(request, "{\"status\":\"success\",\"data\":{\"result\":{\"stdout\":\"\",\"stderr\":\"failed\",\"exit_code\":2},\"exec_ms\":1}}")));
        var instance = Instance(transport, ingress: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => instance.ShellAsync("false"));

        Assert.Contains("status 2", exception.Message, StringComparison.Ordinal);
    }

    private static SandboxInstance Instance(Transport transport, bool ingress) => new(transport, new SandboxData
    {
        Id = "sb-1",
        Status = SandboxStatus.Running,
        IngressEnabled = ingress,
        IngressUrlTemplate = "https://<port>.preview.example",
    });

    private static Transport CreateTransport(HttpMessageHandler handler) => new(new SandboxClientOptions
    {
        ApiKey = "sdk-key",
        BaseUri = new Uri("https://api.example.test"),
        MaxRetries = 0,
    }, handler);

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static HttpResponseMessage Json(HttpRequestMessage request, string body) => new(HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _count;
        internal int Count => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            var message = response(request);
            message.RequestMessage ??= request;
            return Task.FromResult(message);
        }
    }
}
