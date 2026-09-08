using System.Net;
using System.Text;
using PlatformTools.App.Services;

namespace PlatformTools.Tests;

public sealed class CloudflareAccessServiceTests
{
    [Fact]
    public async Task Protection_CreatesApplicationThenEmailPolicy()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        var service = new CloudflareAccessService(http);

        await service.ConfigureEmailProtectionAsync("acct", "secret-token", "demo", "demo.example.com", ["a@example.com", "b@example.com"]);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("accounts/acct/access/apps", handler.Requests[0].Path);
        Assert.Contains("\"uri\":\"demo.example.com\"", handler.Requests[0].Body);
        Assert.Equal("accounts/acct/access/apps/app-id/policies", handler.Requests[1].Path);
        Assert.Contains("a@example.com", handler.Requests[1].Body);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer secret-token", request.Authorization));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body, string Authorization)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.PathAndQuery.TrimStart('/').Replace("client/v4/", ""), await request.Content!.ReadAsStringAsync(cancellationToken), request.Headers.Authorization!.ToString()));
            var id = Requests.Count == 1 ? "app-id" : "policy-id";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"success\":true,\"result\":{{\"id\":\"{id}\"}}}}", Encoding.UTF8, "application/json") };
        }
    }
}
