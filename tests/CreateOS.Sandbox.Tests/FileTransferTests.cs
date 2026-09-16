using System.Net;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class FileTransferTests
{
    [Fact]
    public async Task UploadUsesPathAndLeavesInputOpen()
    {
        using var handler = new InspectUploadHandler();
        using var transport = CreateTransport(handler);
        var instance = Instance(transport);
        await using var source = new MemoryStream("contents"u8.ToArray());

        await instance.Files.UploadAsync("/workspace/file.txt", source);

        Assert.Equal("contents", handler.Body);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task DownloadUsesPathAndReturnsContent()
    {
        using var handler = new DownloadHandler(new MemoryStream("contents"u8.ToArray()));
        using var transport = CreateTransport(handler);
        var instance = Instance(transport);

        await using var stream = await instance.Files.DownloadAsync("/workspace/file.txt");
        using var reader = new StreamReader(stream);

        Assert.Equal("contents", await reader.ReadToEndAsync());
        Assert.Equal("/workspace/file.txt", handler.Path);
    }

    [Fact]
    public async Task DownloadTimeoutRemainsActiveWhileReadingBody()
    {
        using var handler = new DownloadHandler(new BlockingStream());
        using var transport = CreateTransport(handler);
        var instance = Instance(transport);
        await using var stream = await instance.Files.DownloadAsync("/workspace/file.txt", new RequestOptions
        {
            Timeout = TimeSpan.FromMilliseconds(30),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            var buffer = new byte[1];
            _ = await stream.ReadAsync(buffer);
        });
    }

    private static SandboxInstance Instance(Transport transport) => new(transport, new SandboxData { Id = "sb-1" });

    private static Transport CreateTransport(HttpMessageHandler handler) => new(new SandboxClientOptions
    {
        ApiKey = "sdk-key",
        BaseUri = new Uri("https://api.example.test"),
        MaxRetries = 0,
    }, handler);

    private sealed class InspectUploadHandler : HttpMessageHandler
    {
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/v1/sandboxes/sb-1/files", request.RequestUri!.AbsolutePath);
            Assert.Equal("path=%2Fworkspace%2Ffile.txt", request.RequestUri.Query.TrimStart('?'));
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
        }
    }

    private sealed class DownloadHandler(Stream stream) : HttpMessageHandler
    {
        internal string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Path = Uri.UnescapeDataString(request.RequestUri!.Query.Split('=', 2)[1]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(stream),
            });
        }
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
