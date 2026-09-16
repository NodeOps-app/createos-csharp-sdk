using System.Globalization;
using System.Text.Json;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Provides access to CreateOS Sandbox account and sandbox APIs.</summary>
public sealed class SandboxClient : IDisposable
{
    private const int MaximumPageSize = 500;
    private readonly Transport _transport;
    /// <summary>Gets the resolved API base URI.</summary>
    public Uri BaseUri => _transport.BaseUri;
    /// <summary>Gets the reusable-template service.</summary>
    public TemplatesService Templates { get; }
    /// <summary>Gets the private-network service.</summary>
    public NetworksService Networks { get; }
    /// <summary>Gets the persistent-disk service.</summary>
    public DisksService Disks { get; }

    /// <summary>Initializes a client with explicit options or environment defaults.</summary>
    /// <param name="options">Client configuration, or <see langword="null"/> to use defaults.</param>
    public SandboxClient(SandboxClientOptions? options = null)
    {
        _transport = new Transport(options ?? new SandboxClientOptions());
        Templates = new TemplatesService(_transport);
        Networks = new NetworksService(_transport);
        Disks = new DisksService(_transport);
    }

    /// <summary>Gets the unauthenticated service health status.</summary>
    public Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<HealthResponse>(HttpMethod.Get, "/healthz", skipAuth: true, cancellationToken: cancellationToken);

    /// <summary>Gets the unauthenticated service readiness status.</summary>
    public async Task<ReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _transport.SendRawAsync(HttpMethod.Get, "/readyz", skipAuth: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is not (200 or 503))
            throw await ErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
            return root.Deserialize<ReadinessResponse>(Transport.Json) ?? new ReadinessResponse(response.IsSuccessStatusCode);
        }
        catch (JsonException) { return new ReadinessResponse(response.IsSuccessStatusCode); }
    }

    /// <summary>Gets the identity and usage summary associated with the API key.</summary>
    public Task<WhoAmIResponse?> WhoAmIAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<WhoAmIResponse>(HttpMethod.Get, "/v1/whoami", cancellationToken: cancellationToken);

    /// <summary>Creates a sandbox and returns its live resource handle.</summary>
    public async Task<SandboxInstance> CreateSandboxAsync(CreateSandboxRequest request, CreateSandboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await _transport.SendAsync<SandboxData>(HttpMethod.Post, "/v1/sandboxes", request, options, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The create response did not contain sandbox data.");
        if (request.IngressEnabled is { } enabled) data = data with { IngressEnabled = enabled };
        return new SandboxInstance(_transport, data);
    }

    /// <summary>Gets a sandbox by identifier.</summary>
    public async Task<SandboxInstance> GetSandboxAsync(string sandboxId, CancellationToken cancellationToken = default) =>
        new(_transport, await RequiredAsync<SandboxData>(HttpMethod.Get, $"/v1/sandboxes/{Segment(sandboxId)}", cancellationToken).ConfigureAwait(false));

    /// <summary>Gets a sandbox by its private IP address.</summary>
    public async Task<SandboxInstance> GetSandboxByIpAsync(string ipAddress, CancellationToken cancellationToken = default) =>
        new(_transport, await RequiredAsync<SandboxData>(HttpMethod.Get, $"/v1/sandboxes/by-ip/{Segment(ipAddress)}", cancellationToken).ConfigureAwait(false));

    /// <summary>Lists sandboxes, transparently walking server-side pages.</summary>
    public async Task<IReadOnlyList<SandboxInstance>> ListSandboxesAsync(ListSandboxesOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = options?.Status is { } status ? new Dictionary<string, string?> { ["status"] = Wire(status) } : null;
        var data = await FetchAllAsync<SandboxData>(_transport, "/v1/sandboxes", options?.Limit, 0, null, query, options, cancellationToken).ConfigureAwait(false);
        return data.Select(x => new SandboxInstance(_transport, x)).ToArray();
    }

    /// <summary>Lists available compute shapes.</summary>
    public Task<IReadOnlyList<Shape>> ListShapesAsync(CancellationToken cancellationToken = default) => FetchAllAsync<Shape>(_transport, "/v1/shapes", null, 0, "shapes", null, null, cancellationToken, true);
    /// <summary>Lists available root file systems.</summary>
    public Task<RootFileSystemCatalog?> ListRootFileSystemsAsync(CancellationToken cancellationToken = default) => _transport.SendAsync<RootFileSystemCatalog>(HttpMethod.Get, "/v1/rootfs", skipAuth: true, cancellationToken: cancellationToken);
    /// <summary>Lists available sandbox hosts.</summary>
    public Task<IReadOnlyList<Host>> ListHostsAsync(CancellationToken cancellationToken = default) => FetchAllAsync<Host>(_transport, "/v1/hosts", null, 0, null, null, null, cancellationToken);

    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, CancellationToken token) =>
        await _transport.SendAsync<T>(method, path, cancellationToken: token).ConfigureAwait(false) ?? throw new JsonException($"{path} returned no data.");

    internal static async Task<IReadOnlyList<T>> FetchAllAsync<T>(Transport transport, string path, int? limit, int offset, string? legacyKey,
        IReadOnlyDictionary<string, string?>? initialQuery, RequestOptions? requestOptions, CancellationToken token, bool skipAuth = false)
    {
        var items = new List<T>();
        while (limit is null || limit <= 0 || items.Count < limit)
        {
            var size = Math.Min(MaximumPageSize, limit is > 0 ? limit.Value - items.Count : MaximumPageSize);
            var query = initialQuery is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>(initialQuery);
            query["limit"] = size.ToString(CultureInfo.InvariantCulture); query["offset"] = offset.ToString(CultureInfo.InvariantCulture);
            var raw = await transport.SendAsync<JsonElement>(HttpMethod.Get, path, options: requestOptions, query: query, skipAuth: skipAuth, cancellationToken: token).ConfigureAwait(false);
            var page = DecodePage<T>(raw, legacyKey, out var total);
            items.AddRange(page);
            if (page.Length == 0 || total is null || offset + page.Length >= total) break;
            offset += page.Length;
        }
        return limit is > 0 ? items.Take(limit.Value).ToArray() : items;
    }

    private static T[] DecodePage<T>(JsonElement raw, string? legacyKey, out int? total)
    {
        total = null;
        if (raw.ValueKind == JsonValueKind.Array) return raw.Deserialize<T[]>(Transport.Json) ?? [];
        if (raw.TryGetProperty("pagination", out var pagination) && pagination.TryGetProperty("total", out var count)) total = count.GetInt32();
        if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) return data.Deserialize<T[]>(Transport.Json) ?? [];
        if (legacyKey is not null && raw.TryGetProperty(legacyKey, out var legacy)) return legacy.Deserialize<T[]>(Transport.Json) ?? [];
        return [];
    }

    internal static string Segment(string value) => Uri.EscapeDataString(value);
    internal static string Wire<T>(T value) where T : struct, Enum => JsonSerializer.Serialize(value, Transport.Json).Trim('"');
    internal static async Task<CreateOSApiException> ErrorAsync(HttpResponseMessage response, CancellationToken token) =>
        new(response.RequestMessage!.Method, response.RequestMessage.RequestUri!, response.StatusCode, await Transport.ReadLimitedStringAsync(response.Content, token).ConfigureAwait(false), response.Headers);

    /// <summary>Releases the underlying HTTP transport.</summary>
    public void Dispose() => _transport.Dispose();
}
