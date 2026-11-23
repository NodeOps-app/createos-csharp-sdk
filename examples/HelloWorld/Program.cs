using CreateOS.Sandbox;

var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-4vcpu-4gb",
    RootFileSystem = "devbox:1",
    TimeoutMinutes = 10,
    SigtermBeforeExpiry = true
});

Console.WriteLine(
    $"expires_at={sandbox.Data.ExpiresAt} " +
    $"sigterm_before_expiry={sandbox.Data.SigtermBeforeExpiry}");

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
