using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CreateOS.Sandbox;

public sealed class CreateOSApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? ApiCode { get; }
    public string? RequestId { get; }
    public string ResponseBody { get; }

    internal CreateOSApiException(HttpMethod method, Uri endpoint, HttpStatusCode statusCode, string body, HttpResponseHeaders headers)
        : base(BuildMessage(method, endpoint, statusCode, body))
    {
        StatusCode = statusCode;
        ResponseBody = body;
        RequestId = headers.TryGetValues("X-Request-ID", out var values) ? values.FirstOrDefault() : null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)) ApiCode = value;
        }
        catch (JsonException) { }
    }

    private static string BuildMessage(HttpMethod method, Uri endpoint, HttpStatusCode status, string body)
    {
        var detail = status.ToString();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                detail = message.GetString() ?? detail;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                detail = data.GetString() ?? detail;
            else if (root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object)
            {
                var fields = data.EnumerateObject()
                    .Where(property => property.Value.ValueKind == JsonValueKind.String)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}: {property.Value.GetString()}")
                    .ToArray();
                if (fields.Length > 0) detail = string.Join("; ", fields);
            }
        }
        catch (JsonException) { }
        return $"{method} {endpoint.PathAndQuery}: HTTP {(int)status}: {detail}";
    }
}

public sealed class CreateOSEnvelopeException : Exception
{
    public string Status { get; }
    public int? ApiCode { get; }
    public JsonElement? ResponseData { get; }

    internal CreateOSEnvelopeException(string status, string? message, int? apiCode, JsonElement? data)
        : base(message ?? $"Unexpected JSend status '{status}'.")
    {
        Status = status;
        ApiCode = apiCode;
        ResponseData = data?.Clone();
    }
}
