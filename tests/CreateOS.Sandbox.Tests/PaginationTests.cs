using System.Net;
using System.Text;
using CreateOS.Sandbox.Internal;
using Xunit;

namespace CreateOS.Sandbox.Tests;

public sealed class PaginationTests
{
    [Fact]
    public async Task FetchesEveryServerPage()
    {
        using var handler = new PageHandler();
        using var transport = CreateTransport(handler);

        var items = await SandboxClient.FetchAllAsync<TestItem>(transport, "/v1/items", null, 0, null, null, null, CancellationToken.None);

        Assert.Equal(["one", "two", "three"], items.Select(item => item.Name));
        Assert.Equal([0, 2], handler.Offsets);
    }

    [Fact]
    public async Task HonorsResultLimitAcrossPages()
    {
        using var handler = new PageHandler();
        using var transport = CreateTransport(handler);

        var items = await SandboxClient.FetchAllAsync<TestItem>(transport, "/v1/items", 2, 0, null, null, null, CancellationToken.None);

        Assert.Equal(["one", "two"], items.Select(item => item.Name));
        Assert.Single(handler.Offsets);
    }

    private static Transport CreateTransport(HttpMessageHandler handler) => new(new SandboxClientOptions
    {
        ApiKey = "sdk-key",
        BaseUri = new Uri("https://api.example.test"),
        MaxRetries = 0,
    }, handler);

    private sealed class PageHandler : HttpMessageHandler
    {
        internal List<int> Offsets { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var offsetValue = request.RequestUri!.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2))
                .Single(part => part[0] == "offset")[1];
            var offset = int.Parse(offsetValue, System.Globalization.CultureInfo.InvariantCulture);
            Offsets.Add(offset);
            var items = offset == 0 ? "[{\"name\":\"one\"},{\"name\":\"two\"}]" : "[{\"name\":\"three\"}]";
            var body = $"{{\"status\":\"success\",\"data\":{{\"data\":{items},\"pagination\":{{\"total\":3}}}}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record TestItem(string Name);
}
