using System.Text.Json;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class ModelSerializationTests
{
    [Fact]
    public void SerializesEnumMemberValues()
    {
        var json = JsonSerializer.Serialize(new
        {
            signal = ManagedProcessSignal.WindowChange,
            screen = ComputerScreenId.Screen3,
            kind = ManagedProcessKind.Pty,
        }, Transport.Json);

        Assert.Equal("{\"signal\":\"SIGWINCH\",\"screen\":\"screen-3\",\"kind\":\"pty\"}", json);
    }

    [Fact]
    public void OmitsNullOptionalRequestFields()
    {
        var json = JsonSerializer.Serialize(new CreateSandboxRequest { Shape = "s-1vcpu-1gb" }, Transport.Json);

        Assert.Equal("{\"shape\":\"s-1vcpu-1gb\"}", json);
    }

    [Fact]
    public void SerializesAndDeserializesSandboxLabels()
    {
        var json = JsonSerializer.Serialize(new CreateSandboxRequest
        {
            Shape = "s-1vcpu-1gb",
            Labels = new Dictionary<string, string> { ["team"] = "backend" },
            TimeoutMinutes = 10,
            SigtermBeforeExpiry = true,
        }, Transport.Json);
        var sandbox = JsonSerializer.Deserialize<SandboxData>(
            "{\"id\":\"sb-1\",\"status\":\"running\",\"labels\":{\"team\":\"backend\"},\"expires_at\":\"2026-11-23T12:10:00Z\",\"sigterm_before_expiry\":true}",
            Transport.Json);

        Assert.Contains("\"labels\":{\"team\":\"backend\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"timeout_minutes\":10", json, StringComparison.Ordinal);
        Assert.Contains("\"sigterm_before_expiry\":true", json, StringComparison.Ordinal);
        Assert.Equal("backend", sandbox!.Labels!["team"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 12, 10, 0, TimeSpan.Zero), sandbox.ExpiresAt);
        Assert.True(sandbox.SigtermBeforeExpiry);
    }

    [Fact]
    public void DeserializesKnownWireEnumsAndRejectsUnknown()
    {
        var status = JsonSerializer.Deserialize<SandboxStatus>("\"running\"", Transport.Json);

        Assert.Equal(SandboxStatus.Running, status);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SandboxStatus>("\"not-a-status\"", Transport.Json));
    }
}
