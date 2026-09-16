using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Creates and manages reusable sandbox templates.</summary>
public sealed class TemplatesService
{
    private readonly Transport _transport; internal TemplatesService(Transport transport) => _transport = transport;
    public Task<IReadOnlyList<Template>> ListAsync(PaginationOptions? options = null, CancellationToken token = default) => SandboxClient.FetchAllAsync<Template>(_transport, "/v1/templates", options?.Limit, options?.Offset ?? 0, "templates", null, null, token);
    public Task<Template> CreateAsync(TemplateCreateRequest request, CancellationToken token = default) => RequiredAsync<Template>(HttpMethod.Post, "/v1/templates", request, token);
    public Task<Template> GetAsync(string templateId, GetTemplateOptions? options = null, CancellationToken token = default) => RequiredAsync<Template>(HttpMethod.Get, $"/v1/templates/{SandboxClient.Segment(templateId)}", null, token, options, new Dictionary<string, string?> { ["include"] = options?.Include is { } include ? SandboxClient.Wire(include) : null });
    public async Task DeleteAsync(string templateId, CancellationToken token = default) => _ = await _transport.SendAsync<object>(HttpMethod.Delete, $"/v1/templates/{SandboxClient.Segment(templateId)}", cancellationToken: token).ConfigureAwait(false);
    public async Task<string> GetLogsAsync(string templateId, TemplateLogsOptions? options = null, CancellationToken token = default)
    {
        using var response = await _transport.SendRawAsync(HttpMethod.Get, $"/v1/templates/{SandboxClient.Segment(templateId)}/logs", options: options, query: AttemptQuery(options), cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }
    public async IAsyncEnumerable<TemplateLogEvent> FollowLogsAsync(string templateId, TemplateLogsOptions? options = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        var query = AttemptQuery(options); query["follow"] = "true";
        using var response = await _transport.SendRawAsync(HttpMethod.Get, $"/v1/templates/{SandboxClient.Segment(templateId)}/logs", options: options, query: query, cancellationToken: token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await SandboxClient.ErrorAsync(response, token).ConfigureAwait(false);
        await foreach (var item in Ndjson.ReadAsync<TemplateLogEvent>(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), token)) yield return item;
    }
    private static Dictionary<string, string?> AttemptQuery(TemplateLogsOptions? options) => new() { ["attempt"] = options?.Attempt?.ToString(CultureInfo.InvariantCulture) };
    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, RequestOptions? options = null, IReadOnlyDictionary<string, string?>? query = null) => await _transport.SendAsync<T>(method, path, body, options, query, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");
}

/// <summary>Creates and manages private sandbox networks.</summary>
public sealed class NetworksService
{
    private readonly Transport _transport; internal NetworksService(Transport transport) => _transport = transport;
    public Task<IReadOnlyList<Network>> ListAsync(PaginationOptions? options = null, CancellationToken token = default) => SandboxClient.FetchAllAsync<Network>(_transport, "/v1/networks", options?.Limit, options?.Offset ?? 0, null, null, null, token);
    public Task<Network> CreateAsync(NetworkCreateRequest request, CancellationToken token = default) => RequiredAsync<Network>(HttpMethod.Post, "/v1/networks", request, token);
    public Task<Network> GetAsync(string networkId, CancellationToken token = default) => RequiredAsync<Network>(HttpMethod.Get, $"/v1/networks/{SandboxClient.Segment(networkId)}", null, token);
    public async Task DeleteAsync(string networkId, CancellationToken token = default) => _ = await _transport.SendAsync<object>(HttpMethod.Delete, $"/v1/networks/{SandboxClient.Segment(networkId)}", cancellationToken: token).ConfigureAwait(false);
    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken token) => await _transport.SendAsync<T>(method, path, body, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");
}

/// <summary>Creates and manages persistent disks.</summary>
public sealed class DisksService
{
    private readonly Transport _transport; internal DisksService(Transport transport) => _transport = transport;
    public Task<IReadOnlyList<Disk>> ListAsync(PaginationOptions? options = null, CancellationToken token = default) => SandboxClient.FetchAllAsync<Disk>(_transport, "/v1/disks", options?.Limit, options?.Offset ?? 0, "disks", null, null, token);
    public Task<Disk> CreateAsync(DiskCreateRequest request, CancellationToken token = default) => RequiredAsync<Disk>(HttpMethod.Post, "/v1/disks", request, token);
    public Task<Disk> GetAsync(string idOrName, CancellationToken token = default) => RequiredAsync<Disk>(HttpMethod.Get, $"/v1/disks/{SandboxClient.Segment(idOrName)}", null, token);
    public async Task<bool> DeleteAsync(string idOrName, CancellationToken token = default) => (await RequiredAsync<Deleted>(HttpMethod.Delete, $"/v1/disks/{SandboxClient.Segment(idOrName)}", null, token).ConfigureAwait(false)).DeletedValue;
    public Task<Disk> RotateCredentialsAsync(string idOrName, DiskCredentials credentials, CancellationToken token = default) => RequiredAsync<Disk>(HttpMethod.Patch, $"/v1/disks/{SandboxClient.Segment(idOrName)}", new { credentials }, token);
    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken token) => await _transport.SendAsync<T>(method, path, body, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");
    private sealed record Deleted([property: System.Text.Json.Serialization.JsonPropertyName("deleted")] bool DeletedValue);
}
