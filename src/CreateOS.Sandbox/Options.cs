namespace CreateOS.Sandbox;

/// <summary>Configures authentication, transport behavior, and retries for a <see cref="SandboxClient"/>.</summary>
public sealed class SandboxClientOptions
{
    /// <summary>Gets or sets the API key. When omitted, the SDK reads <c>CREATEOS_API_KEY</c>.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Gets or sets the CreateOS API base URI.</summary>
    public Uri? BaseUri { get; set; }
    /// <summary>Gets or sets the default request timeout, including response-body consumption.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets or sets the number of retry attempts after the initial request.</summary>
    public int MaxRetries { get; set; } = 2;
    /// <summary>Gets or sets the initial exponential-backoff delay.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Gets or sets the maximum delay between retry attempts.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the value sent in the <c>User-Agent</c> header.</summary>
    public string UserAgent { get; set; } = "createos-csharp-sdk/0.1.2";
    /// <summary>Gets or sets a custom handler for proxy, TLS, or connection settings. Redirects are always disabled.</summary>
    public HttpClientHandler? HttpClientHandler { get; set; }
}

/// <summary>Overrides exponential-backoff retry behavior for one request.</summary>
public sealed class RetryOptions
{
    /// <summary>Gets the number of retry attempts after the initial request.</summary>
    public int MaxRetries { get; init; }
    /// <summary>Gets the initial delay, or <see langword="null"/> to use the client default.</summary>
    public TimeSpan? BaseDelay { get; init; }
    /// <summary>Gets the maximum delay, or <see langword="null"/> to use the client default.</summary>
    public TimeSpan? MaxDelay { get; init; }
}

/// <summary>Contains transport overrides shared by API operations.</summary>
public class RequestOptions
{
    /// <summary>Gets request headers. Authentication headers cannot be overridden.</summary>
    public IDictionary<string, string>? Headers { get; init; }
    /// <summary>Gets a timeout that overrides the client default for this request.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Gets a retry policy that overrides the client default for this request.</summary>
    public RetryOptions? Retry { get; init; }
    /// <summary>Gets whether retries are disabled for this request.</summary>
    public bool DisableRetry { get; init; }
}

public sealed class CreateSandboxOptions : RequestOptions { }
public sealed class ListSandboxesOptions : RequestOptions
{
    public int? Limit { get; init; }
    public SandboxStatus? Status { get; init; }
}
public sealed class PaginationOptions
{
    public int? Limit { get; init; }
    public int Offset { get; init; }
}
public sealed class ExecOptions : RequestOptions
{
    public string? StandardInput { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
public sealed class WaitOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public RequestOptions? Request { get; init; }
}
public sealed class ManagedProcessConnectOptions : RequestOptions { public long AfterSequence { get; init; } }
public sealed class ManagedProcessWaitOptions : RequestOptions
{
    public ManagedProcessWaitScope Scope { get; init; } = ManagedProcessWaitScope.Leader;
    public TimeSpan? WaitTimeout { get; init; }
}
public sealed class ManagedProcessDeleteOptions : RequestOptions { public TimeSpan? GracePeriod { get; init; } }
public class ComputerScreenOptions : RequestOptions { public ComputerScreenId? ScreenId { get; init; } }
public sealed class ComputerScreenshotOptions : ComputerScreenOptions
{
    public string? WindowId { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}
public sealed class ComputerListWindowsOptions : ComputerScreenOptions { public string? Application { get; init; } }
public sealed class GetTemplateOptions : RequestOptions { public TemplateInclude? Include { get; init; } }
public sealed class TemplateLogsOptions : RequestOptions { public int? Attempt { get; init; } }
public sealed record AttachDiskOptions(string DiskId, string MountPath, string? SubPath = null);
public sealed record DetachDiskOptions(string DiskId, string MountPath);
