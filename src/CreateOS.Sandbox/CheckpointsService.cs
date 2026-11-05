using System.Text.Json;
using CreateOS.Sandbox.Internal;

namespace CreateOS.Sandbox;

/// <summary>Manages local checkpoints inside one running sandbox.</summary>
public sealed class CheckpointsService
{
    private readonly SandboxInstance _instance;
    private readonly Transport _transport;

    internal CheckpointsService(SandboxInstance instance, Transport transport)
    {
        _instance = instance;
        _transport = transport;
    }

    /// <summary>Captures an eligible absolute directory.</summary>
    public async Task<Checkpoint> CreateAsync(CreateCheckpointRequest request, CancellationToken cancellationToken = default) =>
        await _transport.SendAsync<Checkpoint>(HttpMethod.Post, Path(), request, cancellationToken: cancellationToken).ConfigureAwait(false)
        ?? throw new JsonException("Checkpoint create response contained no data.");

    /// <summary>Lists checkpoints oldest first.</summary>
    public async Task<IReadOnlyList<Checkpoint>> ListAsync(CancellationToken cancellationToken = default) =>
        await _transport.SendAsync<Checkpoint[]>(HttpMethod.Get, Path(), cancellationToken: cancellationToken).ConfigureAwait(false)
        ?? [];

    /// <summary>Restores a checkpoint at its original directory path.</summary>
    public async Task<CheckpointRestoreResponse> RestoreAsync(string checkpointId, CancellationToken cancellationToken = default) =>
        await _transport.SendAsync<CheckpointRestoreResponse>(HttpMethod.Post, Path(checkpointId, "/restore"), cancellationToken: cancellationToken).ConfigureAwait(false)
        ?? throw new JsonException("Checkpoint restore response contained no data.");

    /// <summary>Deletes a checkpoint record while retaining referenced layers.</summary>
    public async Task DeleteAsync(string checkpointId, CancellationToken cancellationToken = default) =>
        _ = await _transport.SendAsync<object>(HttpMethod.Delete, Path(checkpointId), cancellationToken: cancellationToken).ConfigureAwait(false);

    private string Path(string? checkpointId = null, string suffix = "") =>
        _instance.Path("/checkpoints") + (checkpointId is null ? "" : "/" + SandboxClient.Segment(checkpointId)) + suffix;
}
