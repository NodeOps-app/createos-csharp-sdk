using System.Text;
using CreateOS.Sandbox;

const string basePath = "/root/seed.txt";
const string forkOnlyPath = "/root/fork-only.txt";

var client = new SandboxClient();
var source = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-256mb",
    RootFileSystem = "devbox:1"
});
SandboxInstance? fork = null;
Console.WriteLine($"base created: {source.Id}");

try
{
    await UploadTextAsync(source, basePath, $"seed written at {DateTimeOffset.UtcNow:O}\n");
    Console.WriteLine($"wrote {basePath}: {await ReadAsync(source, basePath)}");

    Console.WriteLine("pausing base...");
    await source.PauseAsync();
    await source.WaitUntilPausedAsync(new WaitOptions { Timeout = TimeSpan.FromMinutes(10) });

    fork = await source.ForkAsync(new ForkSandboxRequest { StartPaused = true });
    await fork.WaitUntilPausedAsync(new WaitOptions { Timeout = TimeSpan.FromMinutes(10) });
    Console.WriteLine($"fork paused: {fork.Id} (forked_from={fork.Data.ForkedFrom})");

    await fork.ResumeAsync();
    await fork.WaitUntilRunningAsync(new WaitOptions { Timeout = TimeSpan.FromMinutes(5) });
    Console.WriteLine($"fork inherits {basePath}: {await ReadAsync(fork, basePath)}");

    await UploadTextAsync(fork, forkOnlyPath, $"written only in fork at {DateTimeOffset.UtcNow:O}\n");
    Console.WriteLine($"fork wrote {forkOnlyPath}: {await ReadAsync(fork, forkOnlyPath)}");

    await source.ResumeAsync();
    await source.WaitUntilRunningAsync(new WaitOptions { Timeout = TimeSpan.FromMinutes(5) });
    Console.WriteLine($"base does not see fork-only file: {await ReadAsync(source, forkOnlyPath)}");
    Console.WriteLine($"base still has {basePath}: {await ReadAsync(source, basePath)}");
}
finally
{
    if (fork is not null) await fork.DestroyAsync(CancellationToken.None);
    await source.DestroyAsync(CancellationToken.None);
}

static async Task UploadTextAsync(SandboxInstance sandbox, string path, string value)
{
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value));
    await sandbox.Files.UploadAsync(path, stream);
}

static async Task<string> ReadAsync(SandboxInstance sandbox, string path)
{
    var result = await sandbox.RunCommandAsync(new RunCommandRequest { Command = "cat", Arguments = [path] });
    return (result.Result.StandardOutput + result.Result.StandardError).Trim();
}
