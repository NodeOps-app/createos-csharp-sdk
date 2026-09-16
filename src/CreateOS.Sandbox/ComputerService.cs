using System.Globalization;
using System.Text.Json;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

public sealed class ComputerService
{
    private readonly SandboxInstance _instance; private readonly Transport _transport;
    public MouseService Mouse { get; }
    public KeyboardService Keyboard { get; }
    public WindowsService Windows { get; }
    public ScreensService Screens { get; }
    internal ComputerService(SandboxInstance instance, Transport transport)
    {
        _instance = instance; _transport = transport; Mouse = new(this); Keyboard = new(this); Windows = new(this); Screens = new(this);
    }
    internal string Path(string suffix) => _instance.Path("/computer" + suffix);
    internal static Dictionary<string, string?> Query(ComputerScreenOptions? options) => new() { ["screen_id"] = options?.ScreenId is { } id ? SandboxClient.Wire(id) : null };
    internal async Task<T> SendAsync<T>(HttpMethod method, string suffix, object? body, ComputerScreenOptions? options, CancellationToken token, IReadOnlyDictionary<string, string?>? query = null)
    {
        var values = Query(options); if (query is not null) foreach (var pair in query) values[pair.Key] = pair.Value;
        return await _transport.SendAsync<T>(method, Path(suffix), body, options, values, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"Computer operation {suffix} returned no data.");
    }
    internal async Task SendAsync(HttpMethod method, string suffix, object? body, ComputerScreenOptions? options, CancellationToken token) => _ = await _transport.SendAsync<object>(method, Path(suffix), body, options, Query(options), cancellationToken: token).ConfigureAwait(false);

    public async Task<Stream> ScreenshotAsync(ComputerScreenshotOptions? options = null, CancellationToken token = default)
    {
        options ??= new(); var query = Query(options);
        query["window_id"] = options.WindowId; query["x"] = options.X?.ToString(CultureInfo.InvariantCulture); query["y"] = options.Y?.ToString(CultureInfo.InvariantCulture); query["width"] = options.Width?.ToString(CultureInfo.InvariantCulture); query["height"] = options.Height?.ToString(CultureInfo.InvariantCulture);
        var response = await _transport.SendRawAsync(HttpMethod.Get, Path("/screenshot"), options: options, query: query, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { var error = await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false); response.Dispose(); throw error; }
        return new OwnedStream(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), response);
    }
    public Task<ComputerScreenGeometry> GetScreenAsync(ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync<ComputerScreenGeometry>(HttpMethod.Get, "/screen", null, options, token);
    public Task<ComputerPoint> GetCursorAsync(ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync<ComputerPoint>(HttpMethod.Get, "/cursor", null, options, token);
    public Task<ComputerClipboard> GetClipboardAsync(ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync<ComputerClipboard>(HttpMethod.Get, "/clipboard", null, options, token);
    public Task SetClipboardAsync(string text, ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync(HttpMethod.Put, "/clipboard", new ComputerClipboard(text), options, token);
    public Task OpenAsync(ComputerOpenRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync(HttpMethod.Post, "/open", request, options, token);
    public Task LaunchAsync(ComputerLaunchRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => SendAsync(HttpMethod.Post, "/launch", request, options, token);

    private sealed class OwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false; public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); } base.Dispose(disposing); }
    }
}

public sealed class MouseService(ComputerService computer)
{
    public Task MoveAsync(ComputerPoint point, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/move", point, options, token);
    public Task ClickAsync(ComputerClickRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/click", request, options, token);
    public Task ScrollAsync(ComputerScrollRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/scroll", request, options, token);
    public Task DragAsync(ComputerDragRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/drag", request, options, token);
    public Task DownAsync(ComputerButtonRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/down", request, options, token);
    public Task UpAsync(ComputerButtonRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/mouse/up", request, options, token);
}
public sealed class KeyboardService(ComputerService computer)
{
    public Task TypeAsync(ComputerTypeRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/keyboard/type", request, options, token);
    public Task PressAsync(IEnumerable<string> keys, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/keyboard/press", new { keys = keys.ToArray() }, options, token);
    public Task DownAsync(IEnumerable<string> keys, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/keyboard/down", new { keys = keys.ToArray() }, options, token);
    public Task UpAsync(IEnumerable<string> keys, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Post, "/keyboard/up", new { keys = keys.ToArray() }, options, token);
}
public sealed class WindowsService(ComputerService computer)
{
    public Task<IReadOnlyList<ComputerWindow>> ListAsync(ComputerListWindowsOptions? options = null, CancellationToken token = default) => computer.SendAsync<IReadOnlyList<ComputerWindow>>(HttpMethod.Get, "/windows", null, options, token, new Dictionary<string, string?> { ["application"] = options?.Application });
    public Task<ComputerWindow> CurrentAsync(ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync<ComputerWindow>(HttpMethod.Get, "/windows/current", null, options, token);
    public Task<ComputerWindow> GetAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync<ComputerWindow>(HttpMethod.Get, $"/windows/{SandboxClient.Segment(id)}", null, options, token);
    public Task<ComputerWindowGeometry> GeometryAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync<ComputerWindowGeometry>(HttpMethod.Get, $"/windows/{SandboxClient.Segment(id)}/geometry", null, options, token);
    public Task FocusAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "focus", null, options, token);
    public Task MoveAsync(string id, ComputerWindowMoveRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "move", request, options, token);
    public Task ResizeAsync(string id, ComputerWindowResizeRequest request, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "resize", request, options, token);
    public Task MaximizeAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "maximize", null, options, token);
    public Task MinimizeAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "minimize", null, options, token);
    public Task RestoreAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => Action(id, "restore", null, options, token);
    public Task CloseAsync(string id, ComputerScreenOptions? options = null, CancellationToken token = default) => computer.SendAsync(HttpMethod.Delete, $"/windows/{SandboxClient.Segment(id)}", null, options, token);
    private Task Action(string id, string action, object? body, ComputerScreenOptions? options, CancellationToken token) => computer.SendAsync(HttpMethod.Post, $"/windows/{SandboxClient.Segment(id)}/{action}", body, options, token);
}
public sealed class ScreensService(ComputerService computer)
{
    public Task<IReadOnlyList<ComputerScreen>> ListAsync(CancellationToken token = default) => computer.SendAsync<IReadOnlyList<ComputerScreen>>(HttpMethod.Get, "/screens", null, null, token);
    public Task<ComputerScreen> CreateAsync(ComputerCreateScreenRequest request, CancellationToken token = default) => computer.SendAsync<ComputerScreen>(HttpMethod.Post, "/screens", request, null, token);
    public Task<ComputerScreen> GetAsync(ComputerScreenId id, CancellationToken token = default) => computer.SendAsync<ComputerScreen>(HttpMethod.Get, Path(id), null, null, token);
    public Task<ComputerScreenConnection> ConnectAsync(ComputerScreenId id, CancellationToken token = default) => computer.SendAsync<ComputerScreenConnection>(HttpMethod.Get, Path(id, "/connect"), null, null, token);
    public Task<ComputerScreen> ResizeAsync(ComputerScreenId id, ComputerCreateScreenRequest request, CancellationToken token = default) => computer.SendAsync<ComputerScreen>(HttpMethod.Post, Path(id, "/resize"), request, null, token);
    public Task DeleteAsync(ComputerScreenId id, CancellationToken token = default) => computer.SendAsync(HttpMethod.Delete, Path(id), null, null, token);
    private static string Path(ComputerScreenId id, string suffix = "") => "/screens/" + SandboxClient.Segment(SandboxClient.Wire(id)) + suffix;
}
