using CreateOS.Sandbox;

using var client = new SandboxClient();
var source = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-1gb",
    RootFileSystem = "alpine:3.20"
});
Snapshot? snapshot = null;
SandboxInstance? restored = null;

try
{
    snapshot = await source.CreateSnapshotAsync(new CreateSnapshotRequest
    {
        Type = SnapshotType.Disk,
        Name = "configured-workspace"
    });

    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    while (snapshot.Status == SnapshotStatus.Creating)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        snapshot = await client.Snapshots.GetAsync(snapshot.Id, timeout.Token);
    }
    if (snapshot.Status != SnapshotStatus.Ready)
        throw new InvalidOperationException($"Snapshot {snapshot.Id} entered {snapshot.Status}.");

    restored = await client.CreateSandboxAsync(new CreateSandboxRequest
    {
        SnapshotId = snapshot.Id
    });
    Console.WriteLine($"Restored sandbox {restored.Id} from snapshot {snapshot.Id}.");
}
finally
{
    if (restored is not null) await restored.DestroyAsync(CancellationToken.None);
    await source.DestroyAsync(CancellationToken.None);
    if (snapshot?.Status is SnapshotStatus.Ready or SnapshotStatus.Failed)
        await client.Snapshots.DeleteAsync(snapshot.Id, CancellationToken.None);
}
