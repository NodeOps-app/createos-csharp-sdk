using System.Text;
using CreateOS.Sandbox;

const string script = """
import time

for number in range(1, 6):
    print(f"result {number}", flush=True)
    time.sleep(1)
""";

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };

var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-1gb",
    RootFileSystem = "devbox:1"
}, cancellationToken: shutdown.Token);
Console.WriteLine($"created: {sandbox.Id}");

try
{
    await using var source = new MemoryStream(Encoding.UTF8.GetBytes(script));
    await sandbox.Files.UploadAsync("/tmp/script.py", source, token: shutdown.Token);

    Console.WriteLine("--- streaming output ---");
    await foreach (var item in sandbox.StreamCommandAsync(new RunCommandRequest
    {
        Command = "python3",
        Arguments = ["/tmp/script.py"]
    }, token: shutdown.Token))
    {
        switch (item.Type)
        {
            case ExecStreamEventType.Stdout: Console.Write(item.Data); break;
            case ExecStreamEventType.Stderr: Console.Error.Write(item.Data); break;
            case ExecStreamEventType.Error: Console.Error.WriteLine($"agent error: {item.ErrorMessage}"); break;
            case ExecStreamEventType.Exit: Console.WriteLine($"(exited {item.ExitCode})"); break;
        }
    }
}
finally
{
    await sandbox.DestroyAsync(CancellationToken.None);
    Console.WriteLine("destroyed");
}
