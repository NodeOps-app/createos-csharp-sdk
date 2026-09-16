using System.Text;
using CreateOS.Sandbox;

var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-256mb",
    RootFileSystem = "devbox:1",
    IngressEnabled = true
});
Console.WriteLine($"created: {sandbox.Id}");

try
{
    await sandbox.RunCommandAsync(new RunCommandRequest { Command = "mkdir", Arguments = ["-p", "/srv"] });
    await using (var page = new MemoryStream(Encoding.UTF8.GetBytes("<h1>hello from CreateOS Sandbox preview URL</h1>")))
        await sandbox.Files.UploadAsync("/srv/index.html", page);

    await sandbox.Processes.CreateAsync(new ManagedProcessCreateRequest
    {
        Command = "python3",
        Arguments = ["-m", "http.server", "8080", "--bind", "0.0.0.0"],
        WorkingDirectory = "/srv"
    });
    await sandbox.WaitForPortAsync(8080, timeout: TimeSpan.FromSeconds(10));

    var preview = sandbox.GetPreviewUri(8080);
    Console.WriteLine($"URL: {preview}");
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    var contents = await http.GetStringAsync(preview);
    Console.WriteLine("--- response ---");
    Console.WriteLine(contents);
}
finally
{
    await sandbox.DestroyAsync(CancellationToken.None);
    Console.WriteLine($"destroyed: {sandbox.Id}");
}
