using CreateOS.Sandbox;

var client = new SandboxClient();
var network = await client.Networks.CreateAsync(new NetworkCreateRequest($"csharp-sdk-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"));
SandboxInstance? sandbox = null;
Console.WriteLine($"created network: {network.Id}");

try
{
    sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
    {
        Shape = "s-1vcpu-1gb",
        RootFileSystem = "devbox:1"
    });
    Console.WriteLine($"created sandbox: {sandbox.Id}");

    await sandbox.AttachNetworkAsync(network.Id);
    var connected = await client.Networks.GetAsync(network.Id);
    var member = connected.Members?.SingleOrDefault(item => item.SandboxId == sandbox.Id)
        ?? throw new InvalidOperationException($"Sandbox {sandbox.Id} was not found in network {network.Id}.");
    Console.WriteLine($"verified member: sandbox={member.SandboxId} ip={member.IpAddress} status={member.Status}");
    await sandbox.DetachNetworkAsync(network.Id);
}
finally
{
    if (sandbox is not null) await sandbox.DestroyAsync(CancellationToken.None);
    await client.Networks.DeleteAsync(network.Id, CancellationToken.None);
}
