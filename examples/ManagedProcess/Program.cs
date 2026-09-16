using System.Text;
using CreateOS.Sandbox;

var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-1vcpu-1gb",
    RootFileSystem = "devbox:1",
    EnvironmentVariables = new Dictionary<string, string>
    {
        ["PROCESS_DEMO_BASE"] = "from-sandbox-env",
        ["PROCESS_DEMO_OVERRIDE"] = "declared-at-create"
    }
});
Console.WriteLine($"created: {sandbox.Id}");

try
{
    var processes = sandbox.Processes;
    var pipe = await processes.CreateAsync(new ManagedProcessCreateRequest
    {
        Command = "/bin/sh",
        Arguments = ["-c", "printf 'base:%s\\n' \"$PROCESS_DEMO_BASE\"; printf 'override:%s\\n' \"$PROCESS_DEMO_OVERRIDE\"; printf 'stderr:ready\\n' >&2; IFS= read -r line; printf 'stdin:%s\\n' \"$line\""],
        WorkingDirectory = "/root",
        EnvironmentVariables = new Dictionary<string, string> { ["PROCESS_DEMO_OVERRIDE"] = "from-process-env" }
    });
    Console.WriteLine($"pipe: {pipe.ProcessId}; listed: {(await processes.ListAsync()).Count}");
    await processes.InputAsync(pipe.ProcessId, "hello managed process\n");
    await processes.CloseStandardInputAsync(pipe.ProcessId);
    var pipeDone = await processes.WaitAsync(pipe.ProcessId, new ManagedProcessWaitOptions { Scope = ManagedProcessWaitScope.Tree, WaitTimeout = TimeSpan.FromSeconds(5) });
    var pipeOutput = await CollectAsync(processes.ConnectAsync(pipe.ProcessId));
    Console.WriteLine($"pipe exit: {pipeDone.ExitCode}\nstdout:\n{pipeOutput.Stdout}stderr:\n{pipeOutput.Stderr}");

    var replay = await CollectAsync(processes.ConnectAsync(pipe.ProcessId, new ManagedProcessConnectOptions { AfterSequence = Math.Max(0, pipeOutput.LastSequence - 1) }));
    Console.WriteLine($"replayed frames: {replay.Frames}");

    var pty = await processes.CreateAsync(new ManagedProcessCreateRequest { WorkingDirectory = "/root", Pty = new PtySize(24, 80) });
    await processes.InputAsync(pty.ProcessId, "echo terminal-ready; stty size\n");
    await processes.ResizeAsync(pty.ProcessId, new PtySize(32, 100));
    await processes.InputAsync(pty.ProcessId, "echo after-resize; stty size; exit\n");
    await processes.WaitAsync(pty.ProcessId, new ManagedProcessWaitOptions { Scope = ManagedProcessWaitScope.Tree, WaitTimeout = TimeSpan.FromSeconds(5) });
    var ptyOutput = await CollectAsync(processes.ConnectAsync(pty.ProcessId));
    Console.WriteLine(ptyOutput.Pty);

    var longRunning = await processes.CreateAsync(new ManagedProcessCreateRequest { Command = "/bin/sh", Arguments = ["-c", "trap '' TERM; sleep 300 & wait"] });
    var terminated = await processes.DeleteAsync(longRunning.ProcessId, new ManagedProcessDeleteOptions { GracePeriod = TimeSpan.FromMilliseconds(100) });
    Console.WriteLine($"terminated: leader_exited={terminated.LeaderExited} tree_exited={terminated.TreeExited}");

    if (!pipeOutput.Stdout.Contains("stdin:hello managed process") || !ptyOutput.Pty.Contains("terminal-ready") || !ptyOutput.Pty.Contains("after-resize") || !terminated.TreeExited)
        throw new InvalidOperationException("Managed-process verification failed.");
}
finally
{
    await sandbox.DestroyAsync(CancellationToken.None);
    Console.WriteLine("destroyed");
}

static async Task<Output> CollectAsync(IAsyncEnumerable<ManagedProcessEvent> events)
{
    var stdout = new StringBuilder(); var stderr = new StringBuilder(); var pty = new StringBuilder(); long sequence = 0; var frames = 0;
    await foreach (var item in events)
    {
        if (item.Type == ManagedProcessConnectEventType.Error) throw new InvalidOperationException(item.ErrorMessage ?? "Process stream error.");
        if (item.Type == ManagedProcessConnectEventType.Exit) break;
        if (item.Type != ManagedProcessConnectEventType.Data || item.Data is null) continue;
        var text = Encoding.UTF8.GetString(item.Data);
        if (item.Stream == ManagedProcessStream.Stdout) stdout.Append(text);
        if (item.Stream == ManagedProcessStream.Stderr) stderr.Append(text);
        if (item.Stream == ManagedProcessStream.Pty) pty.Append(text);
        sequence = Math.Max(sequence, item.Sequence); frames++;
    }
    return new(stdout.ToString(), stderr.ToString(), pty.ToString(), sequence, frames);
}

internal sealed record Output(string Stdout, string Stderr, string Pty, long LastSequence, int Frames);
