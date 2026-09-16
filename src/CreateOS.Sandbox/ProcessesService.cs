using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Creates and manages durable processes inside a sandbox.</summary>
public sealed class ProcessesService
{
    private readonly SandboxInstance _instance; private readonly Transport _transport;
    internal ProcessesService(SandboxInstance instance, Transport transport) { _instance = instance; _transport = transport; }
    private string Path(string? processId = null, string suffix = "") => _instance.Path("/processes" + (processId is null ? "" : "/" + SandboxClient.Segment(processId)) + suffix);

    public Task<ManagedProcess> CreateAsync(ManagedProcessCreateRequest request, CancellationToken token = default) => RequiredAsync<ManagedProcess>(HttpMethod.Post, Path(), request, token);
    public async Task<IReadOnlyList<ManagedProcess>> ListAsync(CancellationToken token = default) => (await RequiredAsync<ProcessList>(HttpMethod.Get, Path(), null, token).ConfigureAwait(false)).Processes;
    public Task<ManagedProcess> GetAsync(string processId, CancellationToken token = default) => RequiredAsync<ManagedProcess>(HttpMethod.Get, Path(processId), null, token);
    public async IAsyncEnumerable<ManagedProcessEvent> ConnectAsync(string processId, ManagedProcessConnectOptions? options = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        using var response = await _transport.SendRawAsync(HttpMethod.Get, Path(processId, "/connect"), options: options,
            query: new Dictionary<string, string?> { ["after"] = (options?.AfterSequence ?? 0).ToString(CultureInfo.InvariantCulture) }, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false);
        await foreach (var frame in Ndjson.ReadAsync<ProcessFrame>(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), token))
        {
            byte[]? data = null;
            if (!string.IsNullOrEmpty(frame.DataBase64)) data = Convert.FromBase64String(frame.DataBase64);
            yield return new ManagedProcessEvent(frame.Type, frame.Sequence, frame.Stream, data, frame.ExitCode, frame.Signal, frame.ErrorMessage, frame.OldestAvailableSequence);
        }
    }
    public Task<long> InputAsync(string processId, string data, CancellationToken token = default) => InputAsync(processId, System.Text.Encoding.UTF8.GetBytes(data), token);
    public async Task<long> InputAsync(string processId, ReadOnlyMemory<byte> data, CancellationToken token = default) =>
        (await RequiredAsync<InputResponse>(HttpMethod.Post, Path(processId, "/input"), new { data_base64 = Convert.ToBase64String(data.Span) }, token).ConfigureAwait(false)).InputSequence;
    public Task CloseStandardInputAsync(string processId, CancellationToken token = default) => NoResultAsync(HttpMethod.Post, Path(processId, "/stdin/close"), null, token);
    public Task ResizeAsync(string processId, PtySize size, CancellationToken token = default) => NoResultAsync(HttpMethod.Post, Path(processId, "/resize"), size, token);
    public Task SignalAsync(string processId, ManagedProcessSignal signal, CancellationToken token = default) => NoResultAsync(HttpMethod.Post, Path(processId, "/signal"), new { signal }, token);
    public Task<ManagedProcess> WaitAsync(string processId, ManagedProcessWaitOptions? options = null, CancellationToken token = default)
    {
        var query = new Dictionary<string, string?> { ["scope"] = SandboxClient.Wire(options?.Scope ?? ManagedProcessWaitScope.Leader), ["timeout_ms"] = options?.WaitTimeout is { } wait ? wait.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) : null };
        return RequiredAsync<ManagedProcess>(HttpMethod.Get, Path(processId, "/wait"), null, token, options, query);
    }
    public Task<ManagedProcess> DeleteAsync(string processId, ManagedProcessDeleteOptions? options = null, CancellationToken token = default) =>
        RequiredAsync<ManagedProcess>(HttpMethod.Delete, Path(processId), null, token, options, new Dictionary<string, string?> { ["grace_ms"] = options?.GracePeriod is { } grace ? grace.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) : null });

    private async Task NoResultAsync(HttpMethod method, string path, object? body, CancellationToken token) => _ = await _transport.SendAsync<object>(method, path, body, cancellationToken: token).ConfigureAwait(false);
    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, RequestOptions? options = null, IReadOnlyDictionary<string, string?>? query = null) =>
        await _transport.SendAsync<T>(method, path, body, options, query, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");
    private sealed record ProcessList(IReadOnlyList<ManagedProcess> Processes);
    private sealed record InputResponse([property: JsonPropertyName("input_seq")] long InputSequence);
    private sealed record ProcessFrame(ManagedProcessConnectEventType Type, [property: JsonPropertyName("seq")] long Sequence, ManagedProcessStream? Stream, [property: JsonPropertyName("data_base64")] string? DataBase64, [property: JsonPropertyName("exit_code")] int? ExitCode, string? Signal, [property: JsonPropertyName("error")] string? ErrorMessage, [property: JsonPropertyName("oldest_available_seq")] long OldestAvailableSequence);
}
