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

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("accounts/acct/access/apps", handler.Requests[1].Path);
        Assert.Contains("\"uri\":\"demo.example.com\"", handler.Requests[1].Body);
        Assert.Equal("accounts/acct/access/apps/app-id/policies", handler.Requests[3].Path);
        Assert.Contains("a@example.com", handler.Requests[3].Body);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer secret-token", request.Authorization));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body, string Authorization)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.PathAndQuery.TrimStart('/').Replace("client/v4/", ""), request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization!.ToString()));
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"result\":[]}", Encoding.UTF8, "application/json") };
            var id = Requests.Count == 2 ? "app-id" : "policy-id";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"success\":true,\"result\":{{\"id\":\"{id}\"}}}}", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Restart_ReusesApplicationAndUpdatesExistingPolicy()
    {
        var handler = new ExistingPolicyHandler();
        var service = new CloudflareAccessService(new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") });
        await service.ConfigureEmailProtectionAsync("acct", "token", "demo", "demo.example.com", ["new@example.com"]);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Get, HttpMethod.Put }, handler.Methods);
        Assert.EndsWith("/apps/app-id/policies/policy-id", handler.LastPath);
        Assert.Contains("new@example.com", handler.LastBody);
    }

    private sealed class ExistingPolicyHandler : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public string LastPath { get; private set; } = "";
        public string LastBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var result = Methods.Count switch
            {
                1 => "[{\"id\":\"app-id\",\"domain\":\"demo.example.com\"}]",
                2 => "[{\"id\":\"policy-id\",\"name\":\"Platform Tools allowed users\"}]",
                _ => "{\"id\":\"policy-id\"}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"result\":" + result + "}") };
        }
    }
}
