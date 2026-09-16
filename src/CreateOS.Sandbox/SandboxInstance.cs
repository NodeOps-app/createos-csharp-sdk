using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Represents a sandbox and exposes its lifecycle and in-sandbox services.</summary>
public sealed class SandboxInstance
{
    private readonly Transport _transport;
    private readonly object _gate = new();
    private SandboxData _data;
    public FilesService Files { get; }
    public ProcessesService Processes { get; }
    public ComputerService Computer { get; }
    public string Id { get { lock (_gate) return _data.Id; } }
    public string? Name { get { lock (_gate) return _data.Name; } }
    public SandboxStatus Status { get { lock (_gate) return _data.Status; } }
    public string? IpAddress { get { lock (_gate) return _data.IpAddress; } }
    public SandboxData Data { get { lock (_gate) return _data; } }

    internal SandboxInstance(Transport transport, SandboxData data)
    {
        _transport = transport; _data = data;
        Files = new FilesService(this, transport); Processes = new ProcessesService(this, transport); Computer = new ComputerService(this, transport);
    }
    internal string Path(string suffix = "") => $"/v1/sandboxes/{SandboxClient.Segment(Id)}{suffix}";
    private void Update(SandboxData data) { lock (_gate) _data = data; }
    private async Task<SandboxData> LifecycleAsync(HttpMethod method, string suffix, object? body, CancellationToken token)
    {
        var data = await _transport.SendAsync<SandboxData>(method, Path(suffix), body, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException("Lifecycle response contained no data.");
        Update(data); return data;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default) => Update(await RequiredAsync<SandboxData>(HttpMethod.Get, Path(), null, cancellationToken).ConfigureAwait(false));
    public async Task PauseAsync(CancellationToken cancellationToken = default) => _ = await LifecycleAsync(HttpMethod.Post, "/pause", null, cancellationToken).ConfigureAwait(false);
    public async Task ResumeAsync(CancellationToken cancellationToken = default) => _ = await LifecycleAsync(HttpMethod.Post, "/resume", null, cancellationToken).ConfigureAwait(false);
    public async Task<SandboxInstance> ForkAsync(ForkSandboxRequest? request = null, CancellationToken cancellationToken = default) => new(_transport, await RequiredAsync<SandboxData>(HttpMethod.Post, Path("/fork"), request ?? new ForkSandboxRequest(), cancellationToken).ConfigureAwait(false));
    public async Task DestroyAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequiredAsync<DestroyedResponse>(HttpMethod.Delete, Path(), null, cancellationToken).ConfigureAwait(false);
        lock (_gate) _data = _data with { Status = result.Status };
    }
    public async Task<long> ResizeAsync(long diskMiB, CancellationToken cancellationToken = default)
    {
        var result = await RequiredAsync<ResizeResponse>(HttpMethod.Post, Path("/resize"), new { disk_mib = diskMiB }, cancellationToken).ConfigureAwait(false);
        lock (_gate) _data = _data with { DiskMiB = result.DiskMiB }; return result.DiskMiB;
    }
    public async Task SetIngressAsync(bool enabled, CancellationToken cancellationToken = default) => _ = await LifecycleAsync(HttpMethod.Patch, "", new { ingress_enabled = enabled }, cancellationToken).ConfigureAwait(false);
    public async Task SetAutoPauseAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        if (timeout is { } duration && (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(24) || duration.Ticks % TimeSpan.TicksPerSecond != 0))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Auto-pause must be whole seconds between 1 minute and 24 hours.");
        object body = timeout is null ? new { disable_auto_pause = true } : new { auto_pause_after_seconds = (int)timeout.Value.TotalSeconds };
        _ = await LifecycleAsync(HttpMethod.Patch, "", body, cancellationToken).ConfigureAwait(false);
    }
    public async Task<int> AddSshPublicKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        (await RequiredAsync<CountResponse>(HttpMethod.Post, Path("/ssh-pubkeys"), new { keys = keys.ToArray() }, cancellationToken).ConfigureAwait(false)).Count;

    public Task WaitUntilRunningAsync(WaitOptions? options = null, CancellationToken token = default) => WaitForAsync(SandboxStatus.Running, [SandboxStatus.Error, SandboxStatus.Failed, SandboxStatus.Destroying, SandboxStatus.Destroyed], options, token);
    public Task WaitUntilPausedAsync(WaitOptions? options = null, CancellationToken token = default) => WaitForAsync(SandboxStatus.Paused, [SandboxStatus.Error, SandboxStatus.Failed, SandboxStatus.Destroying, SandboxStatus.Destroyed], options, token);
    public Task WaitUntilDestroyedAsync(WaitOptions? options = null, CancellationToken token = default) => WaitForAsync(SandboxStatus.Destroyed, [SandboxStatus.Error, SandboxStatus.Failed], options, token);
    private async Task WaitForAsync(SandboxStatus desired, HashSet<SandboxStatus> terminal, WaitOptions? options, CancellationToken token)
    {
        options ??= new WaitOptions(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(options.Timeout);
        try
        {
            while (true)
            {
                var data = await _transport.SendAsync<SandboxData>(HttpMethod.Get, Path(), options: options.Request, cancellationToken: timeout.Token).ConfigureAwait(false) ?? throw new JsonException("Status response contained no data.");
                Update(data); if (data.Status == desired) return; if (terminal.Contains(data.Status)) throw new InvalidOperationException($"Sandbox '{Id}' entered terminal state '{data.Status}'.");
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException($"Sandbox '{Id}' did not become '{desired}' within {options.Timeout}."); }
    }

    public async Task<RunCommandResponse> RunCommandAsync(RunCommandRequest request, ExecOptions? options = null, CancellationToken token = default)
    {
        request = request with { Stream = false, StandardInput = options?.StandardInput ?? request.StandardInput, EnvironmentVariables = options?.EnvironmentVariables ?? request.EnvironmentVariables };
        return await RequiredAsync<RunCommandResponse>(HttpMethod.Post, Path("/exec"), request, token, options).ConfigureAwait(false);
    }
    public async Task<RunCommandResponse> ShellAsync(string script, ExecOptions? options = null, CancellationToken token = default)
    {
        var result = await RunCommandAsync(new RunCommandRequest { Command = "bash", Arguments = ["-lc", script] }, options, token).ConfigureAwait(false);
        if (result.Result.ExitCode != 0 || !string.IsNullOrEmpty(result.Result.ErrorMessage)) throw new InvalidOperationException($"Command exited with status {result.Result.ExitCode}: {result.Result.ErrorMessage}\n{result.Result.StandardError[^Math.Min(2000, result.Result.StandardError.Length)..]}");
        return result;
    }
    public async IAsyncEnumerable<CommandStreamEvent> StreamCommandAsync(RunCommandRequest request, ExecOptions? options = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        request = request with { Stream = true, StandardInput = options?.StandardInput ?? request.StandardInput, EnvironmentVariables = options?.EnvironmentVariables ?? request.EnvironmentVariables };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Transport.Json));
        await using var body = new MemoryStream(bytes);
        using var response = await _transport.SendRawAsync(HttpMethod.Post, Path("/exec?stream=true"), body, "application/json", options, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false);
        await foreach (var frame in Ndjson.ReadAsync<CommandFrame>(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), token))
        {
            if (frame.Heartbeat) yield return new(ExecStreamEventType.Heartbeat);
            if (!string.IsNullOrEmpty(frame.StandardOutput)) yield return new(ExecStreamEventType.Stdout, frame.StandardOutput);
            if (!string.IsNullOrEmpty(frame.StandardError)) yield return new(ExecStreamEventType.Stderr, frame.StandardError);
            if (!string.IsNullOrEmpty(frame.ErrorMessage)) yield return new(ExecStreamEventType.Error, ErrorMessage: frame.ErrorMessage);
            if (frame.ExitCode is not null) yield return new(ExecStreamEventType.Exit, ExitCode: frame.ExitCode);
        }
    }

    public Uri GetPreviewUri(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var data = Data; if (!data.IngressEnabled || string.IsNullOrWhiteSpace(data.IngressUrlTemplate)) throw new InvalidOperationException("Sandbox ingress is not enabled.");
        return new Uri(data.IngressUrlTemplate.Replace("<port>", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
    public async Task WaitForPortAsync(int port, string host = "127.0.0.1", TimeSpan? timeout = null, CancellationToken token = default)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or ':' or '-'))) throw new ArgumentException("Host is not a valid IP address or DNS name.", nameof(host));
        var duration = timeout ?? TimeSpan.FromSeconds(30); var seconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));
        var command = $"timeout {seconds} bash -c 'until (echo > /dev/tcp/{host}/{port}) 2>/dev/null; do sleep 0.25; done'";
        var result = await RunCommandAsync(new RunCommandRequest { Command = "bash", Arguments = ["-c", command] }, new ExecOptions { Timeout = duration + TimeSpan.FromSeconds(5) }, token).ConfigureAwait(false);
        if (result.Result.ExitCode != 0) throw new TimeoutException($"Port {port} did not become ready within {duration}.");
    }

    public Task<EgressView> GetEgressAsync(CancellationToken token = default) => RequiredAsync<EgressView>(HttpMethod.Get, Path("/egress"), null, token);
    public Task<EgressView> SetEgressAsync(IReadOnlyList<string> rules, CancellationToken token = default) => RequiredAsync<EgressView>(HttpMethod.Put, Path("/egress"), new { egress = rules }, token);
    public Task<BandwidthView> GetBandwidthAsync(CancellationToken token = default) => RequiredAsync<BandwidthView>(HttpMethod.Get, Path("/bandwidth"), null, token);
    public Task<BandwidthView> RechargeBandwidthAsync(long bytes, CancellationToken token = default) => RequiredAsync<BandwidthView>(HttpMethod.Post, Path("/bandwidth/recharge"), new { add_bytes = bytes }, token);
    public Task AttachNetworkAsync(string networkId, CancellationToken token = default) => SendNoResultAsync(HttpMethod.Post, Path("/networks"), new NetworkEntry(networkId), token);
    public Task DetachNetworkAsync(string networkId, CancellationToken token = default) => SendNoResultAsync(HttpMethod.Delete, Path($"/networks/{SandboxClient.Segment(networkId)}"), null, token);
    public Task<IReadOnlyList<SandboxDisk>> ListDisksAsync(PaginationOptions? options = null, CancellationToken token = default) => SandboxClient.FetchAllAsync<SandboxDisk>(_transport, Path("/disks"), options?.Limit, options?.Offset ?? 0, "disks", null, null, token);
    public Task AttachDiskAsync(AttachDiskOptions options, CancellationToken token = default) => SendNoResultAsync(HttpMethod.Post, Path("/disks"), new DiskAttachment(options.DiskId, options.MountPath, options.SubPath), token);
    public async Task<bool> DetachDiskAsync(DetachDiskOptions options, CancellationToken token = default) => (await RequiredAsync<DetachResponse>(HttpMethod.Delete, Path($"/disks/{SandboxClient.Segment(options.DiskId)}"), null, token, query: new Dictionary<string, string?> { ["mount_path"] = options.MountPath }).ConfigureAwait(false)).Detached;

    private async Task SendNoResultAsync(HttpMethod method, string path, object? body, CancellationToken token) => _ = await _transport.SendAsync<object>(method, path, body, cancellationToken: token).ConfigureAwait(false);
    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, RequestOptions? options = null, IReadOnlyDictionary<string, string?>? query = null) =>
        await _transport.SendAsync<T>(method, path, body, options, query, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");
    private sealed record DestroyedResponse(string Id, SandboxStatus Status);
    private sealed record ResizeResponse(string Id, [property: JsonPropertyName("disk_mib")] long DiskMiB);
    private sealed record CountResponse(int Count);
    private sealed record DetachResponse(bool Detached);
    private sealed record CommandFrame([property: JsonPropertyName("stdout")] string? StandardOutput, [property: JsonPropertyName("stderr")] string? StandardError, [property: JsonPropertyName("exit_code")] int? ExitCode, [property: JsonPropertyName("error")] string? ErrorMessage, [property: JsonPropertyName("hb")] bool Heartbeat);
}

internal static class Ndjson
{
    private const int MaximumLineLength = 16 << 20;

    internal static async IAsyncEnumerable<T> ReadAsync<T>(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, leaveOpen: false);
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length > MaximumLineLength) throw new JsonException($"Stream frame exceeded {MaximumLineLength} characters.");
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith(':') || line.StartsWith("event:", StringComparison.Ordinal) ||
                line.StartsWith("id:", StringComparison.Ordinal) || line.StartsWith("retry:", StringComparison.Ordinal)) continue;
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                line = line[5..].Trim();
                if (line.Length == 0) continue;
            }
            yield return JsonSerializer.Deserialize<T>(line, Transport.Json) ?? throw new JsonException("Stream frame was null.");
        }
    }
}
