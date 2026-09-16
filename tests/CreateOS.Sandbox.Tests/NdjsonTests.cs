using System.Text;
using System.Text.Json;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class NdjsonTests
{
    [Fact]
    public async Task ReadsPlainNdjsonFrames()
    {
        await using var stream = Body("{\"value\":\"one\"}\n{\"value\":\"two\"}\n");

        var frames = await ReadAllAsync(stream);

        Assert.Equal(["one", "two"], frames.Select(frame => frame.Value));
    }

    [Fact]
    public async Task ReadsSseDataAndIgnoresControlLines()
    {
        await using var stream = Body(": keepalive\nevent: output\nid: 7\nretry: 1000\ndata: {\"value\":\"hello\"}\n\n");

        var frames = await ReadAllAsync(stream);

        Assert.Equal("hello", Assert.Single(frames).Value);
    }

    [Fact]
    public async Task RejectsMalformedFrame()
    {
        await using var stream = Body("not-json\n");

        await Assert.ThrowsAsync<JsonException>(async () => await ReadAllAsync(stream));
    }

    private static MemoryStream Body(string value) => new(Encoding.UTF8.GetBytes(value));

    private static async Task<List<TestFrame>> ReadAllAsync(Stream stream)
    {
        var frames = new List<TestFrame>();
        await foreach (var frame in Ndjson.ReadAsync<TestFrame>(stream, CancellationToken.None)) frames.Add(frame);
        return frames;
    }

    private sealed record TestFrame(string Value);
}
