using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace CreateOS.Sandbox;

public enum SandboxStatus { Creating, Running, Pausing, Paused, Resuming, Forking, Error, Destroying, Destroyed, Failed }
public enum HostStatus { Active, Draining, Dead }
public enum ExecStreamEventType { Stdout, Stderr, Exit, Error, Heartbeat }
public enum ManagedProcessKind { Process, Pty }
public enum ManagedProcessState { Starting, Running, Terminating, Exited, Failed }
public enum ManagedProcessWaitScope { Leader, Tree }
#pragma warning disable CA1711 // Matches the CreateOS API's managed-process stream field.
public enum ManagedProcessStream { Stdout, Stderr, Pty }
#pragma warning restore CA1711
public enum ManagedProcessConnectEventType { Data, Exit, Heartbeat, Error }
public enum TemplateStatus { Pending, Building, Ready, Failed }
public enum TemplateInclude { Dockerfile }
public enum DiskKind { S3 }
public enum DiskMountStatus { Pending, Mounted, Error, Unmounting }
public enum ComputerMouseButton { Left, Middle, Right }
public enum ComputerScrollDirection { Up, Down }
public enum ComputerScreenId
{
    [EnumMember(Value = "screen-0")] Screen0, [EnumMember(Value = "screen-1")] Screen1,
    [EnumMember(Value = "screen-2")] Screen2, [EnumMember(Value = "screen-3")] Screen3,
    [EnumMember(Value = "screen-4")] Screen4, [EnumMember(Value = "screen-5")] Screen5,
    [EnumMember(Value = "screen-6")] Screen6, [EnumMember(Value = "screen-7")] Screen7
}
public enum ManagedProcessSignal
{
    [EnumMember(Value = "SIGHUP")] Hangup, [EnumMember(Value = "SIGINT")] Interrupt,
    [EnumMember(Value = "SIGQUIT")] Quit, [EnumMember(Value = "SIGKILL")] Kill,
    [EnumMember(Value = "SIGTERM")] Terminate, [EnumMember(Value = "SIGUSR1")] UserDefined1,
    [EnumMember(Value = "SIGUSR2")] UserDefined2, [EnumMember(Value = "SIGWINCH")] WindowChange
}

public sealed record NetworkEntry([property: JsonPropertyName("id")] string Id);
public sealed record CreateSandboxRequest
{
    [JsonPropertyName("shape")] public required string Shape { get; init; }
    [JsonPropertyName("rootfs")] public string? RootFileSystem { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("networks")] public IReadOnlyList<NetworkEntry>? Networks { get; init; }
    [JsonPropertyName("disk_mib")] public long? DiskMiB { get; init; }
    [JsonPropertyName("egress")] public IReadOnlyList<string>? EgressRules { get; init; }
    [JsonPropertyName("envs")] public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    [JsonPropertyName("ssh_pubkeys")] public IReadOnlyList<string>? SshPublicKeys { get; init; }
    [JsonPropertyName("host_id")] public string? HostId { get; init; }
    [JsonPropertyName("node_selector")] public IReadOnlyDictionary<string, string>? NodeSelector { get; init; }
    [JsonPropertyName("ingress_enabled")] public bool? IngressEnabled { get; init; }
    [JsonPropertyName("disks")] public IReadOnlyList<DiskAttachment>? Disks { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("auto_pause_after_seconds")] public int? AutoPauseAfterSeconds { get; init; }
}
public sealed record ForkSandboxRequest
{
    [JsonPropertyName("start_paused")] public bool? StartPaused { get; init; }
    [JsonPropertyName("ssh_pubkeys")] public IReadOnlyList<string>? SshPublicKeys { get; init; }
    [JsonPropertyName("egress")] public IReadOnlyList<string>? EgressRules { get; init; }
    [JsonPropertyName("ingress_enabled")] public bool? IngressEnabled { get; init; }
    [JsonPropertyName("envs")] public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
public sealed record RunCommandRequest
{
    [JsonPropertyName("cmd")] public required string Command { get; init; }
    [JsonPropertyName("args")] public IReadOnlyList<string>? Arguments { get; init; }
    [JsonPropertyName("stdin")] public string? StandardInput { get; init; }
    [JsonPropertyName("env")] public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    [JsonPropertyName("stream")] public bool? Stream { get; init; }
}
public sealed record PtySize([property: JsonPropertyName("rows")] int Rows, [property: JsonPropertyName("cols")] int Columns);
public sealed record ManagedProcessCreateRequest
{
    [JsonPropertyName("cmd")] public string? Command { get; init; }
    [JsonPropertyName("args")] public IReadOnlyList<string>? Arguments { get; init; }
    [JsonPropertyName("cwd")] public string? WorkingDirectory { get; init; }
    [JsonPropertyName("env")] public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    [JsonPropertyName("pty")] public PtySize? Pty { get; init; }
}
public sealed record ComputerPoint([property: JsonPropertyName("x")] int X, [property: JsonPropertyName("y")] int Y);
public sealed record ComputerClickRequest(ComputerMouseButton? Button = null, int? X = null, int? Y = null, int? Count = null);
public sealed record ComputerScrollRequest(ComputerScrollDirection? Direction = null, int? Amount = null);
public sealed record ComputerDragRequest(ComputerPoint From, ComputerPoint To);
public sealed record ComputerButtonRequest(ComputerMouseButton? Button = null);
public sealed record ComputerTypeRequest([property: JsonPropertyName("text")] string Text, [property: JsonPropertyName("delay_in_ms")] int? DelayInMilliseconds = null);
public sealed record ComputerOpenRequest([property: JsonPropertyName("target")] string Target);
public sealed record ComputerLaunchRequest([property: JsonPropertyName("application")] string Application, [property: JsonPropertyName("uri")] string? Uri = null);
public sealed record ComputerWindowMoveRequest(int X, int Y);
public sealed record ComputerWindowResizeRequest(int Width, int Height);
public sealed record ComputerCreateScreenRequest(int? Width = null, int? Height = null);
public sealed record TemplateCreateRequest(string Name, string Dockerfile, string? Base = null);
public sealed record DiskConfig(string Bucket, string Endpoint, string? Region = null, [property: JsonPropertyName("use_path_style")] bool? UsePathStyle = null);
public sealed record DiskCredentials([property: JsonPropertyName("access_key")] string AccessKey, [property: JsonPropertyName("secret_key")] string SecretKey);
public sealed record DiskCreateRequest(string Name, DiskKind Kind, DiskConfig Config, DiskCredentials Credentials);
public sealed record DiskAttachment([property: JsonPropertyName("disk_id")] string DiskId, [property: JsonPropertyName("mount_path")] string MountPath, [property: JsonPropertyName("sub_path")] string? SubPath = null);
public sealed record NetworkCreateRequest(string Name);

public sealed record HealthResponse(bool Up);
public sealed record ReadinessResponse(bool Ready, string? Reason = null, [property: JsonPropertyName("scheduler_last_ok_ms_ago")] long? SchedulerLastOkMillisecondsAgo = null);
public sealed record WhoAmIStats(int Running, int Paused, int Other, int Total);
public sealed record WhoAmIResponse([property: JsonPropertyName("user_id")] string UserId, WhoAmIStats Stats);
public sealed record SandboxData
{
    public required string Id { get; init; }
    public SandboxStatus Status { get; init; }
    [JsonPropertyName("ip")] public string? IpAddress { get; init; }
    [JsonPropertyName("vcpu")] public int VCpu { get; init; }
    [JsonPropertyName("mem_mib")] public int MemoryMiB { get; init; }
    [JsonPropertyName("disk_mib")] public long DiskMiB { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("ingress_enabled")] public bool IngressEnabled { get; init; }
    [JsonPropertyName("ingress_url_template")] public string? IngressUrlTemplate { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("running_at")] public DateTimeOffset? RunningAt { get; init; }
    [JsonPropertyName("destroyed_at")] public DateTimeOffset? DestroyedAt { get; init; }
    [JsonPropertyName("spawn_ms")] public double? SpawnMilliseconds { get; init; }
    public string? Shape { get; init; }
    [JsonPropertyName("rootfs")] public string? RootFileSystem { get; init; }
    public string? Region { get; init; }
    [JsonPropertyName("egress")] public IReadOnlyList<string>? EgressRules { get; init; }
    [JsonPropertyName("envs")] public IReadOnlyList<string>? EnvironmentVariables { get; init; }
    [JsonPropertyName("ssh_pubkeys")] public IReadOnlyList<string>? SshPublicKeys { get; init; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; init; }
    [JsonPropertyName("bandwidth_ingress_bytes")] public long? BandwidthIngressBytes { get; init; }
    [JsonPropertyName("paused_at")] public DateTimeOffset? PausedAt { get; init; }
    [JsonPropertyName("last_resumed_at")] public DateTimeOffset? LastResumedAt { get; init; }
    [JsonPropertyName("forked_from")] public string? ForkedFrom { get; init; }
    [JsonPropertyName("auto_pause_after_seconds")] public int? AutoPauseAfterSeconds { get; init; }
}
/// <summary>A plaintext delegated credential returned only on creation or rotation.</summary>
public sealed record SandboxAccessTokenCreateResponse(
    string Token, bool Enabled,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("rotated_at")] DateTimeOffset? RotatedAt = null);

/// <summary>Delegated token state without plaintext credential material.</summary>
public sealed record SandboxAccessTokenMetadata(
    bool Enabled,
    [property: JsonPropertyName("token_hint")] string? TokenHint = null,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt = null,
    [property: JsonPropertyName("rotated_at")] DateTimeOffset? RotatedAt = null);
public sealed record CommandResult([property: JsonPropertyName("stdout")] string StandardOutput, [property: JsonPropertyName("stderr")] string StandardError, [property: JsonPropertyName("exit_code")] int ExitCode, [property: JsonPropertyName("error")] string? ErrorMessage = null);
public sealed record RunCommandResponse(CommandResult Result, [property: JsonPropertyName("exec_ms")] double ExecutionMilliseconds);
public sealed record CommandStreamEvent(ExecStreamEventType Type, string? Data = null, int? ExitCode = null, string? ErrorMessage = null);
public sealed record Shape(string Id, [property: JsonPropertyName("vcpu")] int VCpu, [property: JsonPropertyName("mem_mib")] int MemoryMiB, [property: JsonPropertyName("default_disk_mib")] long DefaultDiskMiB, [property: JsonPropertyName("cpu_quota_pct")] int? CpuQuotaPercent = null);
public sealed record RootFileSystemEntry(string Name, string? Description = null, bool? Deprecated = null, string? Successor = null);
public sealed record RootFileSystemCatalog([property: JsonPropertyName("rootfs")] IReadOnlyList<string> RootFileSystems, string Default, IReadOnlyList<RootFileSystemEntry>? Entries = null);
public sealed record Host(string Id, HostStatus Status, [property: JsonPropertyName("free_mib")] int FreeMemoryMiB, [property: JsonPropertyName("vm_count")] int SandboxCount, [property: JsonPropertyName("rootfses")] IReadOnlyList<string>? RootFileSystems = null);
public sealed record ManagedProcessOutputWindow([property: JsonPropertyName("oldest_seq")] long OldestSequence, [property: JsonPropertyName("newest_seq")] long NewestSequence, long Bytes);
public sealed record ManagedProcessForeground([property: JsonPropertyName("pid")] int ProcessId, [property: JsonPropertyName("cmd")] string Command, [property: JsonPropertyName("args")] IReadOnlyList<string>? Arguments = null);
public sealed record ManagedProcess
{
    [JsonPropertyName("process_id")] public required string ProcessId { get; init; }
    public ManagedProcessKind Kind { get; init; }
    public int Pid { get; init; }
    public ManagedProcessState State { get; init; }
    [JsonPropertyName("leader_exited")] public bool LeaderExited { get; init; }
    [JsonPropertyName("tree_exited")] public bool TreeExited { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; init; }
    [JsonPropertyName("exit_code")] public int? ExitCode { get; init; }
    public string? Signal { get; init; }
    [JsonPropertyName("cmd")] public string? Command { get; init; }
    [JsonPropertyName("args")] public IReadOnlyList<string>? Arguments { get; init; }
    [JsonPropertyName("cwd")] public string? WorkingDirectory { get; init; }
    public ManagedProcessForeground? Foreground { get; init; }
    public ManagedProcessOutputWindow? Output { get; init; }
}
public sealed record ManagedProcessEvent(ManagedProcessConnectEventType Type, long Sequence = 0, ManagedProcessStream? Stream = null, byte[]? Data = null, int? ExitCode = null, string? Signal = null, string? ErrorMessage = null, long OldestAvailableSequence = 0);
public sealed record Template(string Id, string Name, string Base, TemplateStatus Status, [property: JsonPropertyName("ext4_size_bytes")] long Ext4SizeBytes, [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt, [property: JsonPropertyName("built_at")] DateTimeOffset? BuiltAt = null, string? Dockerfile = null);
public sealed record TemplateLogEvent([property: JsonPropertyName("ts")] DateTimeOffset? Timestamp = null, string? Level = null, string? Line = null, int? Attempt = null, bool? Final = null, string? Status = null);
public sealed record Disk(string Id, string Name, DiskKind Kind, DiskConfig Config, [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
public sealed record SandboxDisk([property: JsonPropertyName("disk_id")] string DiskId, string Name, DiskKind Kind, DiskConfig Config, [property: JsonPropertyName("mount_path")] string MountPath, [property: JsonPropertyName("sub_path")] string? SubPath, [property: JsonPropertyName("mount_status")] DiskMountStatus MountStatus, [property: JsonPropertyName("mount_error")] string? MountError);
public sealed record NetworkMember([property: JsonPropertyName("sandbox_id")] string SandboxId, string Status, [property: JsonPropertyName("ip")] string? IpAddress = null, string? Name = null);
public sealed record Network(string Id, string Name, [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt, [property: JsonPropertyName("member_count")] int? MemberCount = null, IReadOnlyList<NetworkMember>? Members = null);
public sealed record EgressView(string Id, [property: JsonPropertyName("egress")] IReadOnlyList<string> EgressRules);
public sealed record BandwidthView(string Id, [property: JsonPropertyName("quota_bytes")] long QuotaBytes, [property: JsonPropertyName("used_bytes")] long UsedBytes, [property: JsonPropertyName("ingress_bytes")] long IngressBytes, [property: JsonPropertyName("remaining_bytes")] long RemainingBytes, bool Capped);
public sealed record ComputerScreenGeometry(int Width, int Height);
public sealed record ComputerClipboard(string Text);
public sealed record ComputerWindow(string Id, string? Title = null);
public sealed record ComputerWindowGeometry(string Id, int X, int Y, int Width, int Height, int Screen);
public sealed record ComputerScreen([property: JsonPropertyName("screen_id")] ComputerScreenId ScreenId, string Display, int Width, int Height, [property: JsonPropertyName("vnc_port")] int VncPort, [property: JsonPropertyName("novnc_port")] int NoVncPort);
public sealed record ComputerScreenConnection([property: JsonPropertyName("screen_id")] ComputerScreenId ScreenId, int Port, string Path, string Token, [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt, string? Url = null);
