# CreateOS C# SDK

Launch an isolated cloud sandbox, run real commands, stream output, move files,
open a preview URL, and tear everything down from .NET.

The SDK targets .NET 8 and provides asynchronous APIs with cancellation,
request-level timeouts, retries, typed models, and inspectable errors.

## Your first sandbox

Reference the project while the first NuGet release is being prepared:

```bash
dotnet add reference path/to/createos-csharp-sdk/src/CreateOS.Sandbox/CreateOS.Sandbox.csproj
```

After publication, install the package with:

```bash
dotnet add package CreateOS.Sandbox
```

```csharp
using CreateOS.Sandbox;

var client = new SandboxClient(new SandboxClientOptions
{
    ApiKey = "your-api-key"
});

var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Name = "hello-csharp",
    Shape = "s-4vcpu-4gb",
    RootFileSystem = "devbox:1"
});

try
{
    var response = await sandbox.RunCommandAsync(new RunCommandRequest
    {
        Command = "sh",
        Arguments = ["-c", "printf 'C# says hello from '; uname -m"]
    });

    Console.Write(response.Result.StandardOutput);
}
finally
{
    await sandbox.DestroyAsync(CancellationToken.None);
}
```

```text
C# says hello from x86_64
```

Do not commit a real API key to source control. Inject it through your secret
manager or the `CREATEOS_SANDBOX_API_KEY` environment variable. Explicit
options take precedence over environment variables:

```csharp
var client = new SandboxClient(new SandboxClientOptions
{
    ApiKey = apiKey,
    BaseUri = new Uri("http://localhost:8080"),
    Timeout = TimeSpan.FromSeconds(30),
    MaxRetries = 2
});
```

`CREATEOS_SANDBOX_BASE_URL` optionally configures the endpoint when `BaseUri`
is not supplied.

## Documentation

- [CreateOS Sandbox overview](https://nodeops.network/createos/docs/Sandbox/Overview)
  explains lifecycle, networking, storage, and isolation.
- [CreateOS Sandbox documentation](https://nodeops.network/createos/docs)
  contains the REST API reference and product guides.
- [CreateOS Go SDK](https://github.com/NodeOps-app/createos-go-sdk)
  provides the same sandbox capabilities for Go applications.
- [CreateOS Python SDK](https://github.com/NodeOps-app/createos-python-sdk)
  provides the same sandbox capabilities for Python applications.
- [CreateOS Java SDK](https://github.com/NodeOps-app/createos-java-sdk)
  provides the same sandbox capabilities for Java applications.
- [CreateOS TypeScript SDK](https://github.com/NodeOps-app/createos-sandbox-sdk)
  provides the same capabilities for JavaScript and TypeScript.
- [Runnable examples](#examples) cover commands, streaming, files, snapshots,
  ingress, networks, templates, managed processes, desktop use, and an HTTP
  execution service.

## Stream output as it happens

Long-running commands do not need to disappear behind a buffered HTTP call:

```csharp
await foreach (var item in sandbox.StreamCommandAsync(new RunCommandRequest
{
    Command = "sh",
    Arguments = ["-c", "for n in 1 2 3; do echo step-$n; sleep 1; done"]
}, token: cancellationToken))
{
    switch (item.Type)
    {
        case ExecStreamEventType.Stdout:
            Console.Write(item.Data);
            break;
        case ExecStreamEventType.Stderr:
            Console.Error.Write(item.Data);
            break;
        case ExecStreamEventType.Exit:
            Console.WriteLine($"exit code: {item.ExitCode}");
            break;
        case ExecStreamEventType.Error:
            Console.Error.WriteLine(item.ErrorMessage);
            break;
    }
}
```

Breaking out of the `await foreach` loop disposes the HTTP response and closes
the underlying stream. Pass a `CancellationToken` to stop from another task.

## Move files without shell escaping

```csharp
await using (var source = File.OpenRead("config.json"))
{
    await sandbox.Files.UploadAsync(
        "/workspace/config.json",
        source,
        token: cancellationToken);
}

await using var downloaded = await sandbox.Files.DownloadAsync(
    "/workspace/config.json",
    token: cancellationToken);

using var reader = new StreamReader(downloaded);
var contents = await reader.ReadToEndAsync(cancellationToken);
```

Override transport settings for one large transfer without changing the
client defaults:

```csharp
var transferOptions = new RequestOptions
{
    Timeout = TimeSpan.FromMinutes(30),
    DisableRetry = true
};

await sandbox.Files.UploadAsync(remotePath, source, transferOptions, cancellationToken);
await using var file = await sandbox.Files.DownloadAsync(
    remotePath,
    transferOptions,
    cancellationToken);
```

The timeout remains active until a download reaches EOF or its stream is
disposed. Uploads are not automatically replayed because an arbitrary
`Stream` may not be safe to read again after a partial write.

## Keep a process alive after disconnecting

Managed processes are durable resources rather than fragile terminal
sessions. Start one, reconnect from an output sequence, send input or signals,
and wait for either its leader or complete process tree:

```csharp
var process = await sandbox.Processes.CreateAsync(new ManagedProcessCreateRequest
{
    Command = "python3",
    Arguments = ["-m", "http.server", "8080"]
});

process = await sandbox.Processes.WaitAsync(
    process.ProcessId,
    new ManagedProcessWaitOptions
    {
        Scope = ManagedProcessWaitScope.Tree,
        WaitTimeout = TimeSpan.FromSeconds(30)
    });
```

Connect returns an asynchronous event stream with retained output replay:

```csharp
await foreach (var item in sandbox.Processes.ConnectAsync(
    process.ProcessId,
    new ManagedProcessConnectOptions { AfterSequence = lastSequence },
    cancellationToken))
{
    if (item.Type == ManagedProcessConnectEventType.Data && item.Data is not null)
        Console.Write(Encoding.UTF8.GetString(item.Data));
}
```

Use `PtySize` when creating a process to request a terminal-backed session.
The managed-process example covers PTY input, resize, replay, waiting, signals,
and forced process-tree termination.

## Turn a service into a URL

Create with ingress enabled, wait for the server to listen, then ask the
sandbox for its public URL:

```csharp
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-4vcpu-4gb",
    RootFileSystem = "devbox:1",
    IngressEnabled = true
});

await sandbox.Processes.CreateAsync(new ManagedProcessCreateRequest
{
    Command = "python3",
    Arguments = ["-m", "http.server", "8080", "--bind", "0.0.0.0"]
});

await sandbox.WaitForPortAsync(
    8080,
    host: "127.0.0.1",
    timeout: TimeSpan.FromSeconds(15),
    token: cancellationToken);

Console.WriteLine(sandbox.GetPreviewUri(8080));
```

## Everything is already connected

Account-level services are initialized with `SandboxClient`:

```csharp
var templates = client.Templates;
var networks = client.Networks;
var disks = client.Disks;

var customTemplates = await templates.ListAsync();
Console.WriteLine($"{customTemplates.Count} templates ready");
```

Instance-level services are initialized when a sandbox handle is created or
retrieved:

```csharp
var files = sandbox.Files;
var processes = sandbox.Processes;
var mouse = sandbox.Computer.Mouse;
var keyboard = sandbox.Computer.Keyboard;
var windows = sandbox.Computer.Windows;
var screens = sandbox.Computer.Screens;
```

## Connect sandboxes on a private network

Create an overlay network, attach a running sandbox, and inspect its
membership. Cleanup runs in reverse order so the sandbox disconnects before
the network is deleted:

```csharp
var network = await client.Networks.CreateAsync(
    new NetworkCreateRequest("agent-mesh"));

try
{
    await sandbox.AttachNetworkAsync(network.Id);
    try
    {
        var connected = await client.Networks.GetAsync(network.Id);
        foreach (var member in connected.Members ?? [])
        {
            Console.WriteLine(
                $"sandbox={member.SandboxId} private-ip={member.IpAddress} status={member.Status}");
        }
    }
    finally
    {
        await sandbox.DetachNetworkAsync(network.Id, CancellationToken.None);
    }
}
finally
{
    await client.Networks.DeleteAsync(network.Id, CancellationToken.None);
}
```

## Lifecycle reads like the domain

```csharp
await sandbox.PauseAsync(cancellationToken);
await sandbox.WaitUntilPausedAsync(token: cancellationToken);

var clone = await sandbox.ForkAsync(
    new ForkSandboxRequest { StartPaused = true },
    cancellationToken);

try
{
    await clone.ResumeAsync(cancellationToken);
    await clone.WaitUntilRunningAsync(token: cancellationToken);
}
finally
{
    await clone.DestroyAsync(CancellationToken.None);
}
```

`SandboxInstance` caches the latest server projection safely. Lifecycle
mutations and `RefreshAsync` update it, while `Id`, `Name`, `Status`,
`IpAddress`, and `Data` provide synchronized reads.

Set or disable automatic idle pausing with the same handle:

```csharp
await sandbox.SetAutoPauseAsync(TimeSpan.FromMinutes(30), cancellationToken);
await sandbox.SetAutoPauseAsync(null, cancellationToken);
```

## Build reusable templates

Templates turn Dockerfiles into reusable sandbox root filesystems:

```csharp
var template = await client.Templates.CreateAsync(new TemplateCreateRequest(
    Name: $"tools-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
    Dockerfile: """
        FROM nodeops/sandbox:debian
        RUN apt-get update && apt-get install -y git curl
        """));

await foreach (var item in client.Templates.FollowLogsAsync(
    template.Id,
    new TemplateLogsOptions { Timeout = TimeSpan.FromMinutes(10) },
    cancellationToken))
{
    if (!string.IsNullOrEmpty(item.Line)) Console.WriteLine(item.Line);
    if (item.Final == true) break;
}
```

Poll `Templates.GetAsync` until the status is `TemplateStatus.Ready`, then use
the template ID as `CreateSandboxRequest.RootFileSystem`. Delete the template
when it is no longer needed.

## Automate a desktop

The `desktop:1` root filesystem exposes screenshots, mouse and keyboard
control, clipboard access, window management, multiple screens, and temporary
noVNC connections:

```csharp
var options = new ComputerScreenOptions
{
    ScreenId = ComputerScreenId.Screen0
};

var geometry = await sandbox.Computer.GetScreenAsync(options, cancellationToken);
await sandbox.Computer.Mouse.MoveAsync(
    new ComputerPoint(geometry.Width / 2, geometry.Height / 2),
    options,
    cancellationToken);

await sandbox.Computer.SetClipboardAsync("hello desktop", options, cancellationToken);
var connection = await sandbox.Computer.Screens.ConnectAsync(
    ComputerScreenId.Screen0,
    cancellationToken);

Console.WriteLine(connection.Url);
```

See the desktop example for PNG capture and cropping, cursor verification,
clipboard round-tripping, browser launch, and noVNC connection creation.

## Errors stay inspectable

Non-successful HTTP responses throw `CreateOSApiException`:

```csharp
try
{
    await client.GetSandboxAsync("missing-sandbox", cancellationToken);
}
catch (CreateOSApiException exception)
{
    Console.WriteLine(
        $"HTTP {(int)exception.StatusCode}, code={exception.ApiCode}, request={exception.RequestId}");
}
catch (TimeoutException)
{
    // A lifecycle or readiness wait exhausted its budget.
}
```

A successful HTTP response containing a JSend `fail` or `error` envelope
throws `CreateOSEnvelopeException`, preserving its status, code, and response
data. Cancellation continues to surface as `OperationCanceledException`.

The transport retries rate limits and temporary service-unavailable responses.
Network failures and other retryable status codes are replayed only for
idempotent methods. Use `RequestOptions.DisableRetry` or `Retry` for a
per-request override:

```csharp
var requestOptions = new RequestOptions
{
    Retry = new RetryOptions
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(250),
        MaxDelay = TimeSpan.FromSeconds(10)
    }
};
```

## Examples

Runnable examples live under [`examples/`](examples/):

- [Hello world](examples/HelloWorld/Program.cs)
- [HTTP execution server](examples/ExecutionServer/README.md)
- [Command streaming](examples/CommandStreaming/Program.cs)
- [Files and snapshots](examples/FilesAndSnapshots/Program.cs)
- [Ingress preview](examples/IngressPreview/Program.cs)
- [Private overlay network](examples/Network/Program.cs)
- [Custom template and Docker](examples/CustomTemplate/Program.cs)
- [Managed process lifecycle](examples/ManagedProcess/Program.cs)
- [Desktop and noVNC](examples/Desktop/Program.cs)

Run any example with the API key in the environment:

```bash
export CREATEOS_SANDBOX_API_KEY=your-api-key
dotnet run --project examples/HelloWorld/HelloWorld.csproj
dotnet run --project examples/CommandStreaming/CommandStreaming.csproj
```

Every example cleans up its resources in a `finally` block. Template, desktop,
and snapshot examples may take several minutes. The complete set has been
validated end-to-end against the live CreateOS API.

## Development

Build every project, including all examples:

```bash
dotnet restore CreateOS.Sandbox.sln
dotnet format CreateOS.Sandbox.sln --verify-no-changes --no-restore
dotnet build CreateOS.Sandbox.sln --configuration Release --no-restore
dotnet test CreateOS.Sandbox.sln --configuration Release --no-build
```

Create a local NuGet package:

```bash
dotnet pack src/CreateOS.Sandbox/CreateOS.Sandbox.csproj \
  --configuration Release \
  --output artifacts
```

The repository enables the built-in .NET analyzers, enforces code style during
builds, treats warnings as errors, and pins the .NET 8 SDK feature band.
GitHub Actions verifies formatting, builds the complete solution, runs the test
suite, and packs the SDK on every pull request and push to `main`.

For custom proxy, certificate, or connection settings, provide a fresh
`HttpClientHandler` through `SandboxClientOptions.HttpClientHandler`. The SDK
always disables automatic redirects on that handler so credentials cannot be
forwarded to another origin.

## Package layout

```text
src/CreateOS.Sandbox/          client, resource services, models, and transport
src/CreateOS.Sandbox/Internal/ JSend, enum conversion, retries, and HTTP details
examples/                      independently runnable .NET programs
```

## About CreateOS

[CreateOS](https://createos.sh) is an execution and governance platform for AI
agents and applications. Learn more about isolated Firecracker-based workloads
on the [CreateOS Sandbox product page](https://createos.sh/products/sandbox).

## License

This SDK is available under the [MIT License](LICENSE).
