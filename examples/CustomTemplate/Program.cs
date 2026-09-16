using CreateOS.Sandbox;

const string dockerfile = """
FROM nodeops/sandbox:debian

RUN apt-get update -qq \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && curl -fsSL https://get.docker.com | sh \
 && rm -rf /var/lib/apt/lists/*
""";

var client = new SandboxClient();
var template = await client.Templates.CreateAsync(new TemplateCreateRequest(
    $"docker-ce-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", dockerfile));
SandboxInstance? sandbox = null;
Console.WriteLine($"[1/5] template id: {template.Id} status: {template.Status}");

try
{
    Console.WriteLine("[2/5] streaming build logs...");
    using var buildTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    try
    {
        await foreach (var item in client.Templates.FollowLogsAsync(template.Id,
            new TemplateLogsOptions { Timeout = TimeSpan.FromMinutes(10) }, buildTimeout.Token))
        {
            if (!string.IsNullOrEmpty(item.Line)) Console.WriteLine(item.Line);
            if (item.Final == true) break;
        }
    }
    catch (Exception exception) when (exception is HttpRequestException or CreateOSApiException)
    {
        Console.Error.WriteLine($"build log stream unavailable: {exception.Message}");
    }

    while (true)
    {
        template = await client.Templates.GetAsync(template.Id, token: buildTimeout.Token);
        if (template.Status == TemplateStatus.Ready) break;
        if (template.Status == TemplateStatus.Failed) throw new InvalidOperationException("Template build failed.");
        await Task.Delay(TimeSpan.FromSeconds(2), buildTimeout.Token);
    }

    Console.WriteLine($"[3/5] creating sandbox from {template.Id}...");
    sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
    {
        Shape = "s-1vcpu-1gb",
        RootFileSystem = template.Id
    });

    Console.WriteLine("[4/5] starting dockerd...");
    await sandbox.ShellAsync("nohup setsid dockerd > /var/log/dockerd.log 2>&1 &");
    for (var attempt = 0; ; attempt++)
    {
        var info = await sandbox.RunCommandAsync(new RunCommandRequest
        {
            Command = "docker",
            Arguments = ["info", "--format", "{{.ServerVersion}}"]
        }, new ExecOptions { Timeout = TimeSpan.FromSeconds(5) });
        if (info.Result.ExitCode == 0) { Console.WriteLine($"dockerd ready: {info.Result.StandardOutput.Trim()}"); break; }
        if (attempt == 29) throw new TimeoutException("dockerd did not start within 60 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    Console.WriteLine("[5/5] running containers...");
    foreach (var (title, arguments) in new[]
    {
        ("docker run hello-world", new[] { "run", "--rm", "hello-world" }),
        ("docker run alpine", new[] { "run", "--rm", "alpine", "sh", "-c", "echo hello from alpine && cat /etc/alpine-release" }),
        ("docker images", new[] { "images" })
    })
    {
        var result = await sandbox.RunCommandAsync(new RunCommandRequest { Command = "docker", Arguments = arguments },
            new ExecOptions { Timeout = TimeSpan.FromMinutes(2) });
        if (result.Result.ExitCode != 0) throw new InvalidOperationException($"{title}: {result.Result.StandardError}");
        Console.WriteLine($"-- {title} --\n{result.Result.StandardOutput.Trim()}");
    }
}
finally
{
    if (sandbox is not null) await sandbox.DestroyAsync(CancellationToken.None);
    await client.Templates.DeleteAsync(template.Id, CancellationToken.None);
}
