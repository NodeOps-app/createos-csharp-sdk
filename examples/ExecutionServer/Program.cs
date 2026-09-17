using System.Text.Json;
using System.Text.Json.Serialization;
using CreateOS.Sandbox;

const int maximumConcurrency = 4;
const long maximumRequestBytes = 1 << 20;
var executionTimeout = TimeSpan.FromMinutes(2);
var cleanupTimeout = TimeSpan.FromSeconds(30);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CREATEOS_API_KEY")))
    throw new InvalidOperationException("CREATEOS_API_KEY is required.");

var client = new SandboxClient();
var slots = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
};
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumRequestBytes);
builder.WebHost.UseUrls(NormalizeAddress(Environment.GetEnvironmentVariable("EXECUTION_SERVER_ADDRESS")));
var app = builder.Build();

app.MapPost("/v1/execute", async (HttpRequest request, CancellationToken requestAborted) =>
{
    if (!await slots.WaitAsync(0, requestAborted))
        return Results.Json(new ErrorResponse("execution capacity reached"), statusCode: StatusCodes.Status429TooManyRequests);

    try
    {
        if (request.ContentLength > maximumRequestBytes)
            return Results.Json(new ErrorResponse("request body exceeds 1 MiB"), statusCode: StatusCodes.Status413PayloadTooLarge);

        ExecuteRequest? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<ExecuteRequest>(request.Body, json, requestAborted);
        }
        catch (JsonException exception)
        {
            return Results.BadRequest(new ErrorResponse($"invalid JSON request: {exception.Message}"));
        }
        if (string.IsNullOrWhiteSpace(input?.Command)) return Results.BadRequest(new ErrorResponse("command is required"));

        using var execution = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        execution.CancelAfter(executionTimeout);
        SandboxInstance? sandbox = null;
        try
        {
            sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
            {
                Shape = "s-1vcpu-1gb",
                RootFileSystem = "devbox:1"
            }, cancellationToken: execution.Token);
            var result = await sandbox.RunCommandAsync(new RunCommandRequest
            {
                Command = input.Command.Trim(),
                Arguments = input.Arguments,
                StandardInput = input.StandardInput,
                EnvironmentVariables = input.EnvironmentVariables
            }, token: execution.Token);
            return Results.Ok(new ExecuteResponse(result.Result.StandardOutput, result.Result.StandardError,
                result.Result.ExitCode, result.Result.ErrorMessage, result.ExecutionMilliseconds));
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new ErrorResponse("execution timed out"), statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception exception)
        {
            return Results.Json(new ErrorResponse($"execution failed: {exception.Message}"), statusCode: StatusCodes.Status502BadGateway);
        }
        finally
        {
            if (sandbox is not null)
            {
                using var cleanup = new CancellationTokenSource(cleanupTimeout);
                try { await sandbox.DestroyAsync(cleanup.Token); }
                catch (Exception exception) { app.Logger.LogError(exception, "Failed to destroy sandbox {SandboxId}", sandbox.Id); }
            }
        }
    }
    finally
    {
        slots.Release();
    }
});

await app.RunAsync();

static string NormalizeAddress(string? value)
{
    var address = string.IsNullOrWhiteSpace(value) ? "127.0.0.1:8080" : value.Trim();
    return address.Contains("://", StringComparison.Ordinal) ? address : "http://" + address;
}

internal sealed record ExecuteRequest(string Command, IReadOnlyList<string>? Arguments = null,
    string? StandardInput = null, IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
internal sealed record ExecuteResponse(string Stdout, string Stderr, int ExitCode, string? Error,
    double ExecutionMilliseconds);
internal sealed record ErrorResponse(string Error);
