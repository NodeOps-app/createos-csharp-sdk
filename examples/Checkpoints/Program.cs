using CreateOS.Sandbox;

const string checkpointPath = "/workspace/checkpoint-demo";

using var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-1gb",
    RootFileSystem = "devbox:1"
});
Checkpoint? checkpoint = null;

try
{
    await RunAsync(sandbox, $"mkdir -p {checkpointPath} && printf 'version one' > {checkpointPath}/value");
    checkpoint = await sandbox.Checkpoints.CreateAsync(new CreateCheckpointRequest
    {
        Path = checkpointPath,
        Name = "before-update"
    });
    await RunAsync(sandbox, $"printf 'version two' > {checkpointPath}/value");
    await sandbox.Checkpoints.RestoreAsync(checkpoint.Id);
    var result = await RunAsync(sandbox, $"cat {checkpointPath}/value");
    Console.WriteLine($"Restored {checkpoint.Id}: {result.Result.StandardOutput}");
}
finally
{
    try
    {
        if (checkpoint is not null)
            await sandbox.Checkpoints.DeleteAsync(checkpoint.Id, CancellationToken.None);
    }
    finally
    {
        await sandbox.DestroyAsync(CancellationToken.None);
    }
}

static Task<RunCommandResponse> RunAsync(SandboxInstance sandbox, string script) =>
    sandbox.RunCommandAsync(new RunCommandRequest
    {
        Command = "sh",
        Arguments = ["-c", script]
    });
