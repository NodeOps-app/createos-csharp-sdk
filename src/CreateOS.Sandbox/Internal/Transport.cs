using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreateOS.Sandbox.Internal;

internal sealed class Transport : IDisposable
{
    private const int MaximumErrorBodyBytes = 4 << 20;
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token",
        "X-CSRF-Token",
    };
    internal static readonly JsonSerializerOptions Json = CreateJsonOptions();
    private readonly Uri _baseUri;
    private readonly string? _apiKey;
    private readonly HttpClient _http;
    private readonly SandboxClientOptions _options;

    internal Transport(SandboxClientOptions options, HttpMessageHandler? handlerOverride = null)
    {
        _options = options;
        var configuredApiKey = options.ApiKey is null
            ? Environment.GetEnvironmentVariable("CREATEOS_API_KEY")
            : options.ApiKey;
        _apiKey = configuredApiKey?.Trim();
        if (options.ApiKey is not null && _apiKey?.Length == 0)
            throw new ArgumentException("ApiKey must not be empty when explicitly configured.", nameof(options));
        var environmentBaseUri = Environment.GetEnvironmentVariable("CREATEOS_SANDBOX_BASE_URL")?.Trim();
        var baseValue = options.BaseUri?.ToString() ?? (string.IsNullOrEmpty(environmentBaseUri) ? "https://api.sb.createos.sh" : environmentBaseUri);
        if (!Uri.TryCreate(baseValue, UriKind.Absolute, out var parsedBaseUri) || (parsedBaseUri.Scheme != "http" && parsedBaseUri.Scheme != "https"))
            throw new ArgumentException("BaseUri must be an absolute HTTP or HTTPS URI.", nameof(options));
        _baseUri = parsedBaseUri;
        if (!_baseUri.UserInfo.Equals(string.Empty, StringComparison.Ordinal) || !_baseUri.Query.Equals(string.Empty, StringComparison.Ordinal) || !_baseUri.Fragment.Equals(string.Empty, StringComparison.Ordinal))
            throw new ArgumentException("BaseUri cannot contain credentials, a query, or a fragment.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero || options.MaxRetries < 0 || options.RetryBaseDelay <= TimeSpan.Zero || options.RetryMaxDelay < options.RetryBaseDelay)
            throw new ArgumentException("Timeout or retry settings are invalid.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.UserAgent))
            throw new ArgumentException("UserAgent must not be empty.", nameof(options));
        if (handlerOverride is not null)
        {
            _http = new HttpClient(handlerOverride, disposeHandler: false);
        }
        else
        {
            var handler = options.HttpClientHandler ?? new HttpClientHandler();
            // The SDK owns redirect policy even for a caller-configured handler,
            // so X-Api-Key can never be forwarded to another origin.
            handler.AllowAutoRedirect = false;
            _http = new HttpClient(handler, disposeHandler: options.HttpClientHandler is null);
        }
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    internal Uri BaseUri => _baseUri;

    internal async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, RequestOptions? options = null,
        IReadOnlyDictionary<string, string?>? query = null, bool skipAuth = false, CancellationToken cancellationToken = default)
    {
        var bytes = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, Json);
        using var response = await SendCoreAsync(method, path, bytes, null, options, query, skipAuth, true, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await ReadLimitedStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new CreateOSApiException(method, response.RequestMessage!.RequestUri!, response.StatusCode, errorBody, response.Headers);
        }
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent) return default;
            throw new JsonException("The API returned an empty JSend envelope.");
        }
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String)
            throw new JsonException("The API response was not a JSend envelope.");
        var status = statusElement.GetString() ?? string.Empty;
        root.TryGetProperty("data", out var data);
        if (!status.Equals("success", StringComparison.Ordinal))
        {
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : null;
            if (message is null && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out var detail) && detail.ValueKind == JsonValueKind.String) message = detail.GetString();
            int? code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var codeValue) ? codeValue : null;
            throw new CreateOSEnvelopeException(status, message, code, data.ValueKind == JsonValueKind.Undefined ? null : data);
        }
        if (typeof(T) == typeof(object)) return default;
        return data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? default : data.Deserialize<T>(Json);
    }

    internal async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, Stream? content = null, string? contentType = null,
        RequestOptions? options = null, IReadOnlyDictionary<string, string?>? query = null, bool skipAuth = false,
        CancellationToken cancellationToken = default) =>
        await SendCoreAsync(method, path, null, content is null ? null : new StreamContent(new NonDisposingStream(content)), options, query, skipAuth, false, cancellationToken, contentType).ConfigureAwait(false);

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, byte[]? body, HttpContent? rawContent,
        RequestOptions? requestOptions, IReadOnlyDictionary<string, string?>? query, bool skipAuth, bool allowRetry,
        CancellationToken cancellationToken, string? contentType = null)
    {
        if (!skipAuth && string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Authentication is required. Set ApiKey or CREATEOS_API_KEY.");
        var uri = BuildUri(path, query);
        var retry = ResolveRetry(requestOptions);
        var retries = requestOptions?.DisableRetry == true || !allowRetry ? 0 : retry.MaxRetries;
        for (var attempt = 0; ; attempt++)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var duration = requestOptions?.Timeout ?? _options.Timeout;
            if (duration > TimeSpan.Zero) timeout.CancelAfter(duration);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            if (!skipAuth) request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
            if (requestOptions?.Headers is not null)
                foreach (var header in requestOptions.Headers.Where(x => !IsSensitive(x.Key))) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body is not null) request.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
            else if (rawContent is not null)
            {
                request.Content = rawContent;
                if (contentType is not null) request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
            try
            {
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (attempt >= retries || !ShouldRetry(method, response.StatusCode))
                {
                    response.Content = new TimeoutContent(response.Content, timeout);
                    return response;
                }
                var delay = RetryDelay(response, attempt, retry);
                response.Dispose();
                timeout.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < retries && IsIdempotent(method) && !cancellationToken.IsCancellationRequested)
            {
                timeout.Dispose();
                await Task.Delay(Backoff(attempt, retry), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                timeout.Dispose();
                throw;
            }
        }
    }

    internal Uri BuildUri(string path, IReadOnlyDictionary<string, string?>? query = null)
    {
        var root = _baseUri.ToString().TrimEnd('/') + "/";
        var builder = new UriBuilder(new Uri(new Uri(root), path.TrimStart('/')));
        if (query is not null)
        {
            var values = query.Where(x => x.Value is not null).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}");
            var added = string.Join("&", values);
            builder.Query = string.IsNullOrEmpty(builder.Query) ? added : builder.Query.TrimStart('?') + "&" + added;
        }
        if (!builder.Uri.GetLeftPart(UriPartial.Authority).Equals(_baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing request to a different origin.");
        return builder.Uri;
    }

    private RetryPolicy ResolveRetry(RequestOptions? options)
    {
        if (options?.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentException("Request timeout must be positive.", nameof(options));
        var maxRetries = options?.Retry?.MaxRetries ?? _options.MaxRetries;
        var baseDelay = options?.Retry?.BaseDelay ?? _options.RetryBaseDelay;
        var maxDelay = options?.Retry?.MaxDelay ?? _options.RetryMaxDelay;
        if (maxRetries < 0 || baseDelay <= TimeSpan.Zero || maxDelay < baseDelay)
            throw new ArgumentException("Request retry settings are invalid.", nameof(options));
        return new RetryPolicy(maxRetries, baseDelay, maxDelay);
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt, RetryPolicy retry)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is { } date) return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return Backoff(attempt, retry);
    }
    private static TimeSpan Backoff(int attempt, RetryPolicy retry) => TimeSpan.FromMilliseconds(Math.Min(retry.MaxDelay.TotalMilliseconds,
        retry.BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 30)) + Random.Shared.NextDouble() * retry.BaseDelay.TotalMilliseconds));
    private static bool IsIdempotent(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put || method == HttpMethod.Delete;
    private static bool ShouldRetry(HttpMethod method, HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable ||
        (IsIdempotent(method) && status is HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout);
    private static bool IsSensitive(string name) => SensitiveHeaders.Contains(name);
    internal static async Task<string> ReadLimitedStringAsync(HttpContent content, CancellationToken token)
    {
        await using var source = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (destination.Length < MaximumErrorBodyBytes)
        {
            var remaining = MaximumErrorBodyBytes - (int)destination.Length;
            var count = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
            if (count == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, (int)destination.Length);
    }
    public void Dispose() => _http.Dispose();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        options.Converters.Add(new EnumMemberJsonConverterFactory());
        return options;
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) => base.Dispose(disposing);
        public override ValueTask DisposeAsync() => base.DisposeAsync();
    }

    private sealed class TimeoutContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly CancellationTokenSource _timeout;

        internal TimeoutContent(HttpContent inner, CancellationTokenSource timeout)
        {
            _inner = inner;
            _timeout = timeout;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await _inner.CopyToAsync(stream, _timeout.Token).ConfigureAwait(false);

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? 0;
            return _inner.Headers.ContentLength.HasValue;
        }

        protected override async Task<Stream> CreateContentReadStreamAsync() =>
            new TimeoutStream(await _inner.ReadAsStreamAsync(_timeout.Token).ConfigureAwait(false), _timeout.Token);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _timeout.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TimeoutStream(Stream inner, CancellationToken timeoutToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken);
            await inner.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken);
            return await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken);
            await inner.WriteAsync(buffer, linked.Token).ConfigureAwait(false);
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed record RetryPolicy(int MaxRetries, TimeSpan BaseDelay, TimeSpan MaxDelay);
}
