using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Uploads files to and downloads files from a sandbox.</summary>
public sealed class FilesService
{
    private readonly SandboxInstance _instance;
    private readonly Transport _transport;
    internal FilesService(SandboxInstance instance, Transport transport) { _instance = instance; _transport = transport; }

    public async Task UploadAsync(string path, Stream content, RequestOptions? options = null, CancellationToken token = default)
    {
        using var response = await _transport.SendRawAsync(HttpMethod.Put, _instance.Path("/files"), content, "application/octet-stream", options,
            new Dictionary<string, string?> { ["path"] = path }, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false);
    }

    public async Task<Stream> DownloadAsync(string path, RequestOptions? options = null, CancellationToken token = default)
    {
        var response = await _transport.SendRawAsync(HttpMethod.Get, _instance.Path("/files"), options: options,
            query: new Dictionary<string, string?> { ["path"] = path }, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false); response.Dispose(); throw error;
        }
        return new ResponseStream(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), response);
    }

    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); } base.Dispose(disposing); }
    }
}
