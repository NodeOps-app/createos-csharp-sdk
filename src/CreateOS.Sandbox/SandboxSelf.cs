namespace CreateOS.Sandbox;

public static class SandboxSelf
{
    public static Task PauseAsync(string? reason = null, CancellationToken token = default) => SignalAsync("pause", reason, token);
    public static Task DeleteAsync(string? reason = null, CancellationToken token = default) => SignalAsync("delete", reason, token);

    private static async Task SignalAsync(string action, string? reason, CancellationToken token)
    {
        var uri = new UriBuilder($"http://127.0.0.1:1029/self/{action}");
        if (!string.IsNullOrEmpty(reason)) uri.Query = "reason=" + Uri.EscapeDataString(reason);
        using var client = new HttpClient();
        using var response = await client.PostAsync(uri.Uri, null, token).ConfigureAwait(false);
        if ((int)response.StatusCode != 202) throw new HttpRequestException($"Self-{action} request returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
    }
}
