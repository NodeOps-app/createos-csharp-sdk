using System.Buffers.Binary;
using CreateOS.Sandbox;

var client = new SandboxClient();
var sandbox = await client.CreateSandboxAsync(new CreateSandboxRequest
{
    Shape = "s-2vcpu-4gb",
    RootFileSystem = "desktop:1",
    IngressEnabled = true
});
Console.WriteLine($"created: {sandbox.Id}");

try
{
    var options = new ComputerScreenOptions { ScreenId = ComputerScreenId.Screen0 };
    ComputerScreenGeometry geometry;
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
    {
        while (true)
        {
            try { geometry = await sandbox.Computer.GetScreenAsync(options, timeout.Token); break; }
            catch (Exception) when (!timeout.IsCancellationRequested) { await Task.Delay(2000, timeout.Token); }
        }
    }

    var screens = await sandbox.Computer.Screens.ListAsync();
    var primary = await sandbox.Computer.Screens.GetAsync(ComputerScreenId.Screen0);
    Console.WriteLine($"geometry: {geometry.Width}x{geometry.Height}");
    Console.WriteLine($"screens: {string.Join(", ", screens.Select(x => x.ScreenId))}");
    Console.WriteLine($"primary display: {primary.Display}, noVNC port {primary.NoVncPort}");

    var fullSize = await PngSizeAsync(await sandbox.Computer.ScreenshotAsync(new ComputerScreenshotOptions
    {
        ScreenId = ComputerScreenId.Screen0,
        Timeout = TimeSpan.FromSeconds(45)
    }));
    var cropSize = await PngSizeAsync(await sandbox.Computer.ScreenshotAsync(new ComputerScreenshotOptions
    {
        ScreenId = ComputerScreenId.Screen0,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 160,
        Timeout = TimeSpan.FromSeconds(45)
    }));
    Console.WriteLine($"screenshots: full={fullSize.Width}x{fullSize.Height} crop={cropSize.Width}x{cropSize.Height}");

    var target = new ComputerPoint(Math.Clamp(geometry.Width / 3, 10, geometry.Width - 1), Math.Clamp(geometry.Height / 3, 10, geometry.Height - 1));
    await sandbox.Computer.Mouse.MoveAsync(target, options);
    var cursor = await sandbox.Computer.GetCursorAsync(options);
    var clipboardText = $"CreateOS desktop {sandbox.Id}";
    await sandbox.Computer.SetClipboardAsync(clipboardText, options);
    var clipboard = await sandbox.Computer.GetClipboardAsync(options);
    await sandbox.Computer.OpenAsync(new ComputerOpenRequest("https://example.com"), options);
    var connection = await sandbox.Computer.Screens.ConnectAsync(ComputerScreenId.Screen0);

    Console.WriteLine($"cursor: {cursor.X},{cursor.Y}");
    Console.WriteLine($"clipboard: {clipboard.Text}");
    Console.WriteLine($"noVNC URL: {connection.Url}");
    if (fullSize != (geometry.Width, geometry.Height) || cropSize != (240, 160) || cursor != target || clipboard.Text != clipboardText || connection.Url is null)
        throw new InvalidOperationException("Desktop verification failed.");
}
finally
{
    await sandbox.DestroyAsync(CancellationToken.None);
    Console.WriteLine("destroyed");
}

static async Task<(int Width, int Height)> PngSizeAsync(Stream stream)
{
    await using (stream)
    {
        var header = new byte[24];
        await stream.ReadExactlyAsync(header);
        return ParsePngSize(header);
    }
}

static (int Width, int Height) ParsePngSize(byte[] header)
{
    byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
    if (!header.AsSpan(0, 8).SequenceEqual(signature)) throw new InvalidDataException("Screenshot was not a PNG.");
    return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
}
