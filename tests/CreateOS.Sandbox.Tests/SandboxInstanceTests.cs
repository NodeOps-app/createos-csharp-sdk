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

    [Fact]
    public async Task AccessTokenLifecycleUsesScopedCredential()
    {
        var seen = new List<(string Method, string Path, string? Key)>();
        var responses = new Queue<string>([
            "{\"status\":\"success\",\"data\":{\"token\":\"skp_sb_first\",\"enabled\":true,\"created_at\":\"2026-09-18T10:00:00Z\"}}",
            "{\"status\":\"success\",\"data\":{\"enabled\":true,\"token_hint\":\"skp_sb...irst\"}}",
            "{\"status\":\"success\",\"data\":{\"result\":{\"stdout\":\"hello\\n\",\"stderr\":\"\",\"exit_code\":0},\"exec_ms\":1}}",
            "{\"status\":\"success\",\"data\":{\"token\":\"skp_sb_second\",\"enabled\":true,\"created_at\":\"2026-09-18T10:00:00Z\",\"rotated_at\":\"2026-09-18T11:00:00Z\"}}",
            "{\"status\":\"success\",\"data\":{\"enabled\":false}}",
        ]);
        using var transport = CreateTransport(new DelegateHandler(request =>
        {
            seen.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.GetValues("X-Api-Key").Single()));
            return Json(request, responses.Dequeue());
        }));
        var owner = Instance(transport, ingress: false);
        var created = await owner.CreateAccessTokenAsync();
        Assert.Equal("skp_sb_first", created.Token);
        Assert.DoesNotContain(created.Token, created.ToString());
        Assert.Equal("skp_sb...irst", (await owner.GetAccessTokenAsync()).TokenHint);
        var worker = owner.WithAccessToken(created.Token);
        Assert.NotSame(owner.Files, worker.Files);
        Assert.Equal("hello\n", (await worker.RunCommandAsync(new RunCommandRequest { Command = "echo", Arguments = ["hello"] })).Result.StandardOutput);
        Assert.Equal(11, (await owner.RotateAccessTokenAsync()).RotatedAt?.Hour);
        Assert.False((await owner.DisableAccessTokenAsync()).Enabled);
        Assert.Equal(new (string Method, string Path, string? Key)[] {
            ("POST", "/v1/sandboxes/sb-1/access-token", "sdk-key"),
            ("GET", "/v1/sandboxes/sb-1/access-token", "sdk-key"),
            ("POST", "/v1/sandboxes/sb-1/exec", "skp_sb_first"),
            ("POST", "/v1/sandboxes/sb-1/access-token/rotate", "sdk-key"),
            ("DELETE", "/v1/sandboxes/sb-1/access-token", "sdk-key"),
        }, seen.ToArray());
        Assert.Throws<ArgumentException>(() => owner.WithAccessToken("  "));
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
