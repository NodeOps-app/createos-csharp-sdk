using System.Text;
using CreateOS.Sandbox;

var client = new SandboxClient();
var health = await client.GetHealthAsync();
var shapes = await client.ListShapesAsync();
Console.WriteLine($"health={health?.Up} shapes={shapes.Count}");

var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Name = $"csharp-{Guid.NewGuid():N}"[..22],
    Shape = "s-1vcpu-1gb",
    RootFileSystem = "devbox:1",
    IngressEnabled = true
});

Console.WriteLine($"sandbox={sandbox.Id} status={sandbox.Status}");
try
{
    var command = await sandbox.RunCommandAsync(new RunCommandRequest
    {
        Command = "sh",
        Arguments = ["-c", "printf 'C# says hello from '; uname -m"]
    });
    Console.Write(command.Result.StandardOutput);

    await foreach (var item in sandbox.StreamCommandAsync(new RunCommandRequest
    {
        Command = "sh",
        Arguments = ["-c", "for n in 1 2 3; do echo step-$n; sleep 1; done"]
    }))
    {
        if (item.Type == ExecStreamEventType.Stdout) Console.Write(item.Data);
        if (item.Type == ExecStreamEventType.Stderr) Console.Error.Write(item.Data);
    }

    await using (var source = new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"production\"}")))
        await sandbox.Files.UploadAsync("/tmp/config.json", source);
    await using (var downloaded = await sandbox.Files.DownloadAsync("/tmp/config.json"))
    using (var reader = new StreamReader(downloaded))
        Console.WriteLine($"download={await reader.ReadToEndAsync()}");

    var process = await sandbox.Processes.CreateAsync(new ManagedProcessCreateRequest
    {
        Command = "python3",
        Arguments = ["-m", "http.server", "8080", "--bind", "0.0.0.0"]
    });
    await sandbox.WaitForPortAsync(8080);
    Console.WriteLine($"preview={sandbox.GetPreviewUri(8080)}");
    await sandbox.Processes.DeleteAsync(process.ProcessId);
}
finally
{
    await sandbox.DestroyAsync();
    Console.WriteLine("sandbox destroyed");
}
