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
    public void DeserializesKnownWireEnumsAndRejectsUnknown()
    {
        var status = JsonSerializer.Deserialize<SandboxStatus>("\"running\"", Transport.Json);

        Assert.Equal(SandboxStatus.Running, status);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SandboxStatus>("\"not-a-status\"", Transport.Json));
    }
}
